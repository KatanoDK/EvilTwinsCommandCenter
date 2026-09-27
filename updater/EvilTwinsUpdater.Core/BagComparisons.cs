using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvilTwinsUpdater.Core;

public sealed record BagItem(int Key, string Name, string Slot, int ItemId, string Value,
    IReadOnlyList<string> ReplaceSlots, string UnavailableReason)
{
    public bool CanCompare => ReplaceSlots.Count > 0;
}
public sealed record GearChange(BagItem Item, string Slot);
public sealed record GearSet(string Key, IReadOnlyList<GearChange> Changes)
{
    public string Name => string.Join(" + ", Changes.Select(change => change.Item.Name));
    public string Slots => string.Join(", ", Changes.Select(change => change.Slot));
}
public sealed record GearComparison(GearSet Set, double Dps, double Gain,
    double? Percent, double? GainError, int Iterations)
{
    public bool? WithinUncertainty => GainError.HasValue ? Math.Abs(Gain) <= GainError.Value : null;
}

public static class BagComparisons
{
    public const int MaximumSelectedItems = 20;
    public const int MaximumCombinations = 500;
    private static readonly HashSet<string> Slots = new(StringComparer.Ordinal) {
        "head", "neck", "shoulder", "back", "chest", "wrist", "hands", "waist", "legs", "feet",
        "finger1", "finger2", "trinket1", "trinket2", "main_hand", "off_hand", "ranged" };
    private static string Canonical(string slot) => slot switch { "shoulders" => "shoulder", "wrists" => "wrist", _ => slot };
    private static string VariantKey(string value) => string.Join(',', value.Split(',')
        .Where(field => field.Contains('=')).Order(StringComparer.Ordinal));
    private static int ItemId(string value)
    {
        var matches = Regex.Matches(value, "(?:^|,)id=([0-9]+)(?=,|$)");
        return matches.Count == 1 && int.TryParse(matches[0].Groups[1].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0;
    }

    public static IReadOnlyList<BagItem> ReadItems(string export)
    {
        var prepared = LocalSimulator.PrepareProfile(export);
        var equipped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in prepared.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            var slot = Canonical(line[..separator]);
            if (Slots.Contains(slot)) equipped[slot] = line[(separator + 1)..].Trim();
        }
        var items = new List<BagItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inBags = false;
        var name = "";
        foreach (var raw in export.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("###", StringComparison.Ordinal))
            {
                inBags = line.Equals("### Gear from Bags", StringComparison.OrdinalIgnoreCase);
                name = "";
                continue;
            }
            if (!inBags || !line.StartsWith('#')) continue;
            var comment = line[1..].Trim();
            if (comment.Length == 0) { name = ""; continue; }
            var separator = comment.IndexOf('=');
            var slot = separator > 0 ? Canonical(comment[..separator].Trim()) : "";
            if (!Slots.Contains(slot))
            {
                // First comment is the item name; later comments may describe its upgrade track.
                if (name.Length == 0 && separator < 0) name = comment[..Math.Min(comment.Length, 180)];
                continue;
            }
            var value = comment[(separator + 1)..].Trim();
            var id = ItemId(value);
            if (id == 0 || value.Length > 2000 || !Regex.IsMatch(value, "^[A-Za-z0-9_,:=/+.-]+$"))
                throw new InvalidOperationException("A bag item has invalid SimC data. Copy a fresh export from the addon.");
            var group = slot.StartsWith("finger", StringComparison.Ordinal) ? "finger"
                : slot.StartsWith("trinket", StringComparison.Ordinal) ? "trinket" : slot;
            if (!seen.Add(group + "=" + VariantKey(value))) { name = ""; continue; }
            if (items.Count >= 200) throw new InvalidOperationException("This export has more than 200 bag gear variants. Use a smaller export.");
            var destinations = group is "finger" or "trinket" ? new[] { group + "1", group + "2" } : new[] { slot };
            var reason = "";
            if (slot is "main_hand" or "off_hand" or "ranged")
            {
                destinations = [];
                reason = "Weapon swaps are not supported yet: the export does not identify hand restrictions or weapon pairs.";
            }
            else
            {
                destinations = destinations.Where(destination => {
                    if (equipped.TryGetValue(destination, out var current) && VariantKey(current) == VariantKey(value)) return false;
                    return true;
                }).ToArray();
                if (destinations.Length == 0) reason = "This exact variant is already equipped.";
            }
            items.Add(new BagItem(items.Count, name.Length > 0 ? name : "Item #" + id,
                slot, id, value, destinations, reason));
            name = "";
        }
        return items;
    }

    public static IReadOnlyList<GearSet> CreateSets(string export, IReadOnlyCollection<int> selected)
    {
        if (selected.Count > MaximumSelectedItems || selected.Distinct().Count() != selected.Count)
            throw new InvalidOperationException($"Select up to {MaximumSelectedItems} distinct bag items.");
        var items = ReadItems(export).ToDictionary(item => item.Key);
        var options = new SortedDictionary<string, List<BagItem>>(StringComparer.Ordinal);
        foreach (var key in selected.Order())
        {
            if (!items.TryGetValue(key, out var item) || !item.CanCompare)
                throw new InvalidOperationException("The bag selection no longer matches this export. Load the bag items again.");
            foreach (var slot in item.ReplaceSlots)
            {
                if (!options.TryGetValue(slot, out var choices)) options.Add(slot, choices = []);
                choices.Add(item);
            }
        }
        var equipped = LocalSimulator.PrepareProfile(export).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2)).Where(parts => Slots.Contains(Canonical(parts[0])))
            .GroupBy(parts => Canonical(parts[0])).ToDictionary(group => group.Key, group => group.Last()[1].Trim());
        var slots = options.Keys.ToArray();
        var changes = new List<GearChange>();
        var sets = new List<GearSet>();
        void Add(int index)
        {
            if (index < slots.Length)
            {
                Add(index + 1); // Keep the equipped item in this slot.
                foreach (var choice in options[slots[index]])
                {
                    changes.Add(new GearChange(choice, slots[index]));
                    Add(index + 1);
                    changes.RemoveAt(changes.Count - 1);
                }
                return;
            }
            if (changes.Count == 0) return; // The baseline is always run separately.
            foreach (var group in new[] { "finger", "trinket" })
            {
                int Id(string slot) => changes.FirstOrDefault(change => change.Slot == slot)?.Item.ItemId
                    ?? (equipped.TryGetValue(slot, out var value) ? ItemId(value) : 0);
                var first = Id(group + "1");
                if (first > 0 && first == Id(group + "2")) return;
            }
            if (sets.Count >= MaximumCombinations)
                throw new InvalidOperationException($"This selection exceeds {MaximumCombinations} gear combinations. Deselect some items or slots before running.");
            sets.Add(new GearSet("gear_" + sets.Count, changes.ToArray()));
        }
        Add(0);
        return sets;
    }

    internal static string AppendSets(string prepared, IReadOnlyList<GearSet> sets)
    {
        var result = new StringBuilder(prepared);
        foreach (var set in sets)
            for (var index = 0; index < set.Changes.Count; index++)
            {
                var change = set.Changes[index];
                result.Append("profileset.").Append(set.Key).Append(index == 0 ? "=" : "+=")
                    .Append(change.Slot).Append('=').AppendLine(change.Item.Value);
            }
        return result.ToString();
    }

    internal static IReadOnlyList<GearComparison> ReadResults(JsonElement root, SimulationResult baseline, IReadOnlyList<GearSet> swaps)
    {
        if (swaps.Count == 0) return [];
        if (!root.GetProperty("sim").TryGetProperty("profilesets", out var sets)
            || !sets.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.String
            || metric.GetString() is not "dps" and not "Damage per Second"
            || !sets.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() != swaps.Count)
            throw new InvalidDataException("The engine did not return all selected bag comparisons. Check the run's console log.");
        var pending = swaps.ToDictionary(swap => swap.Key);
        var comparisons = new List<GearComparison>();
        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !pending.Remove(name.GetString()!, out var swap)
                || !result.TryGetProperty("mean", out var mean) || !mean.TryGetDouble(out var dps) || !double.IsFinite(dps) || dps < 0
                || !result.TryGetProperty("iterations", out var iterations) || !iterations.TryGetInt32(out var count) || count < 1)
                throw new InvalidDataException("The engine returned an invalid or mismatched bag comparison.");
            double? error = null;
            if (baseline.Error.HasValue && result.TryGetProperty("mean_error", out var uncertainty)
                && uncertainty.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0)
            {
                var combined = Math.Sqrt(baseline.Error.Value * baseline.Error.Value + value * value);
                if (double.IsFinite(combined)) error = combined;
            }
            var gain = dps - baseline.Dps;
            comparisons.Add(new GearComparison(swap, dps, gain,
                baseline.Dps > 0 ? gain / baseline.Dps * 100 : null, error, count));
        }
        return comparisons.OrderByDescending(item => item.Gain).ThenBy(item => item.Set.Key, StringComparer.Ordinal).ToArray();
    }
}
