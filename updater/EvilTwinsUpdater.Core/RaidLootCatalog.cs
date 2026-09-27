using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvilTwinsUpdater.Core;

public sealed record RaidLootSource(int Id, string Name)
{
    public override string ToString() => Name;
}
public sealed record DropCase(string Key, SimUpgrade Item, GearSet Set);
public sealed record DropPlan(RaidMember Member, IReadOnlyList<DropCase> Cases, string Scope, string Title, string Notes);

// Public Raidbots game data. Downloaded tables stay on disk so subsequent queues can work offline.
public sealed class RaidLootCatalog
{
    private readonly JsonElement instances, encounters, equipment, conversions, season, upgrades, weapons;
    private readonly Dictionary<int, JsonElement> byId;
    public IReadOnlyList<RaidLootSource> Sources { get; }
    public string SeasonName => Text(season, "name");
    private static JsonElement Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;
    private static string Text(JsonElement value, string name) => Field(value, name).ValueKind == JsonValueKind.String ? Field(value, name).GetString()! : "";
    private static int Int(JsonElement value, string name) => Field(value, name).TryInt();
    private static IEnumerable<JsonElement> Array(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static bool Has(JsonElement item, string name, int id) => Array(Field(item, name)).Any(value => value.TryInt() == id);

    public RaidLootCatalog(IReadOnlyDictionary<string, JsonElement> tables)
    {
        instances = tables["instances"]; encounters = tables["encounter-items"]; equipment = tables["equippable-items"];
        season = Array(tables["seasons"]).Single(value => Field(value, "active").ValueKind == JsonValueKind.True);
        upgrades = tables["bonus-upgrade-sets"]; weapons = tables["weapon-specs"];
        conversions = Field(tables["item-conversions"], Int(season, "itemConversionId").ToString());
        // Boss sequence-to-track rules have been verified against current completed Raidbots reports.
        // A new season must be verified before producing loot recommendations with different rules.
        if (Int(season, "id") != 37 || conversions.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Local loot data for this season has not been verified yet. Use completed Raidbots reports instead.");
        byId = Array(equipment).Concat(Array(Field(conversions, "items"))).Concat(Array(encounters))
            .GroupBy(item => Int(item, "id")).ToDictionary(group => group.Key, group => group.Last());
        var tierIds = Array(Field(conversions, "items")).Where(item => Int(item, "itemSetId") > 0).Select(item => Int(item, "id")).ToHashSet();
        var ids = Array(encounters).Where(item => Array(Field(item, "contains")).Any(id => tierIds.Contains(id.TryInt())))
            .SelectMany(item => Array(Field(item, "sources"))).Select(source => Int(source, "instanceId")).ToHashSet();
        Sources = Array(instances).Where(instance => Text(instance, "type") == "raid" && ids.Contains(Int(instance, "id")))
            .Select(instance => new RaidLootSource(Int(instance, "id"), Text(instance, "name"))).OrderBy(source => source.Name).ToArray();
        if (Sources.Count == 0) throw new InvalidDataException("No verified current-season raids were found in the loot catalog.");
    }

    public static async Task<RaidLootCatalog> LoadAsync(string cache, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(cache);
        var names = new[] { "instances", "encounter-items", "equippable-items", "item-conversions", "seasons", "bonus-upgrade-sets", "weapon-specs" };
        using var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
            { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EvilTwinsUpdater/0.1.0");
        var tables = new Dictionary<string, JsonElement>();
        var cachePath = Path.Combine(cache, "catalog.json");
        if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromHours(24))
        {
            if (new FileInfo(cachePath).Length > 80_000_000) throw new InvalidDataException("Cached loot data exceeds the size limit.");
            using var cached = JsonDocument.Parse(await File.ReadAllBytesAsync(cachePath, cancellation));
            foreach (var name in names) tables[name] = cached.RootElement.GetProperty(name).Clone();
            return new RaidLootCatalog(tables);
        }
        // Validate and atomically replace one complete snapshot; interrupted refreshes cannot mix tables.
        foreach (var file in names)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            using var response = await client.GetAsync($"https://www.raidbots.com/static/data/live/{file}.json", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            var maximum = file == "equippable-items" ? 100_000_000 : 50_000_000;
            if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Loot data exceeds the download limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var memory = new MemoryStream(); var buffer = new byte[65536]; int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (memory.Length + read > maximum) throw new InvalidDataException("Loot data exceeds the download limit.");
                memory.Write(buffer, 0, read);
            }
            using var json = JsonDocument.Parse(memory.ToArray()); tables[file] = json.RootElement.Clone();
        }
        var catalog = new RaidLootCatalog(tables);
        var snapshot = JsonSerializer.SerializeToUtf8Bytes(tables);
        if (snapshot.Length > 80_000_000) throw new InvalidDataException("Loot data exceeds the snapshot size limit.");
        await File.WriteAllBytesAsync(cachePath + ".tmp", snapshot, cancellation);
        File.Move(cachePath + ".tmp", cachePath, true);
        return catalog;
    }

    private bool Fits(JsonElement item, int specId)
    {
        var spec = DpsSpecs.All[specId];
        if (Field(item, "allowableClasses").ValueKind == JsonValueKind.Array && !Has(item, "allowableClasses", spec.ClassId)) return false;
        if (Field(item, "specs").ValueKind == JsonValueKind.Array && !Has(item, "specs", specId)) return false;
        var type = Int(item, "inventoryType"); var itemClass = Int(item, "itemClass"); var sub = Int(item, "itemSubClass");
        if (itemClass == 4 && type is not (2 or 11 or 12 or 14 or 16 or 23) && sub != spec.Armor) return false;
        if (itemClass == 2 || type is 14 or 23)
        {
            var match = Array(weapons).FirstOrDefault(value => Int(value, "itemClass") == itemClass && Int(value, "itemSubClass") == sub);
            if (!Has(match, "specsCanDrop", specId)) return false;
        }
        var stats = Array(Field(item, "stats")).Select(value => Int(value, "id")).ToArray();
        var primary = stats.Where(id => id is 3 or 4 or 5 or 71 or 72 or 73 or 74).ToArray();
        return primary.Length == 0 || primary.Any(id => id == spec.PrimaryStat || id == 71
            || id == 72 && spec.PrimaryStat is 3 or 4 || id == 73 && spec.PrimaryStat is 3 or 5 || id == 74 && spec.PrimaryStat is 4 or 5);
    }

    public DropPlan Plan(RaidMember member, int sourceId, string difficulty)
    {
        if (!member.Ready) throw new InvalidOperationException("This member has no ready DPS profile.");
        var source = Sources.SingleOrDefault(value => value.Id == sourceId) ?? throw new InvalidOperationException("Choose a verified current-season raid.");
        var track = difficulty switch { "LFR" => "Veteran", "Normal" => "Champion", "Heroic" => "Hero", "Mythic" => "Myth", _ => throw new InvalidOperationException("Choose a raid difficulty.") };
        var ranks = Array(season.GetProperty("bonusListGroups")).SelectMany(id => Array(Field(upgrades, id.GetInt32().ToString())))
            .Where(rank => Text(rank, "name") == track).ToDictionary(rank => Int(rank, "level"));
        var instance = Array(instances).Single(value => Int(value, "id") == source.Id);
        var bosses = Array(instance.GetProperty("encounters")).ToDictionary(value => Int(value, "id"));
        var equipped = LocalSimulator.PrepareProfile(member.Profile).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split('=', 2)).ToDictionary(pair => pair[0] switch { "shoulders" => "shoulder", "wrists" => "wrist", _ => pair[0] }, pair => pair[1]);
        int EquippedId(string slot) => equipped.TryGetValue(slot, out var value) && int.TryParse(Regex.Match(value, "(?:^|,)id=([0-9]+)").Groups[1].Value, out var id) ? id : 0;
        string Property(string slot, string property) => equipped.TryGetValue(slot, out var value) ? Regex.Match(value, "(?:^|,)" + property + "=([0-9/]+)").Groups[1].Value : "";
        int WeaponType(string slot) => byId.TryGetValue(EquippedId(slot), out var item) ? Int(item, "inventoryType") : 0;
        var cases = new List<DropCase>(); var skippedWeapons = 0;
        var difficultyKey = "raid-" + difficulty.ToLowerInvariant();
        foreach (var drop in Array(encounters))
        foreach (var origin in Array(Field(drop, "sources")).Where(value => Int(value, "instanceId") == sourceId))
        {
            if (!Fits(drop, member.SpecId)) continue;
            var bossId = Int(origin, "encounterId");
            if (!bosses.TryGetValue(bossId, out var boss)) throw new InvalidDataException("A drop has an unknown encounter; refresh the loot catalog.");
            var sequence = Int(boss, "itemSequenceLevel");
            if (Field(boss, "trash").ValueKind == JsonValueKind.True) sequence = 1;
            if (sequence < 1) throw new InvalidDataException("A boss has no verified loot level.");
            if (Field(origin, "veryRare").ValueKind == JsonValueKind.True) sequence++;
            var contained = Array(Field(drop, "contains")).ToArray();
            var variants = contained.Length > 0 ? contained.Select(id => byId.TryGetValue(id.TryInt(), out var item) ? item : throw new InvalidDataException("A tier-token reward is missing from the catalog."))
                : new[] { drop }.Concat(Array(Field(conversions, "items")).Where(item => Int(item, "inventoryType") == Int(drop, "inventoryType") && Fits(item, member.SpecId)));
            foreach (var item in variants.Where(item => Fits(item, member.SpecId)).GroupBy(item => Int(item, "id")).Select(group => group.First()))
            {
                var id = Int(item, "id"); var inventory = Int(item, "inventoryType");
                var slots = inventory switch {
                    1 => new[] { "head" }, 2 => ["neck"], 3 => ["shoulder"], 5 or 20 => ["chest"], 6 => ["waist"],
                    7 => ["legs"], 8 => ["feet"], 9 => ["wrist"], 10 => ["hands"], 11 => ["finger1", "finger2"],
                    12 => ["trinket1", "trinket2"], 16 => ["back"], 13 => member.SpecId is 251 or 259 or 260 or 261 or 263 or 269 or 577 or 1480 ? ["main_hand", "off_hand"] : ["main_hand"],
                    17 => member.SpecId == 72 ? ["main_hand", "off_hand"] : ["main_hand"], 15 or 21 or 26 => ["main_hand"], 14 or 22 or 23 => ["off_hand"], _ => System.Array.Empty<string>() };
                foreach (var slot in slots)
                {
                    var isWeapon = slot is "main_hand" or "off_hand";
                    // A single drop cannot supply the missing half of a staff-to-one-hand conversion.
                    if (isWeapon && (WeaponType("main_hand") == 0 || inventory is 13 or 21 or 22 or 23 && WeaponType("main_hand") == 17
                        || slot == "off_hand" && WeaponType("off_hand") == 0 || inventory == 17 && slot == "off_hand" && WeaponType("off_hand") != 17))
                    { skippedWeapons++; continue; }
                    var otherSlot = slot switch { "finger1" => "finger2", "finger2" => "finger1", "trinket1" => "trinket2", "trinket2" => "trinket1", "main_hand" => "off_hand", "off_hand" => "main_hand", _ => "" };
                    if (Field(item, "uniqueEquipped").ValueKind == JsonValueKind.True && otherSlot.Length > 0 && EquippedId(otherSlot) == id) continue;
                    var offset = Field(Field(drop, "slotItemSequenceOffsets"), slot).TryInt();
                    if (!ranks.TryGetValue(Math.Max(1, sequence + offset), out var rank)) throw new InvalidDataException("A loot level is not present in the season's upgrade track.");
                    var level = Int(rank, "itemLevel"); var bonus = Int(rank, "bonusId");
                    if (id <= 0 || id > 10_000_000 || bonus <= 0 || level is < 1 or > 2000) throw new InvalidDataException("Invalid loot identifiers or levels.");
                    var bonuses = Array(Field(item, "bonusLists")).Select(value => value.TryInt()).Where(value => value > 0).Concat([4799, 4786, bonus]).Distinct().ToArray();
                    var bonusIds = string.Join('/', bonuses);
                    var value = $",id={id},bonus_id={bonusIds}";
                    var enchant = Property(slot, "enchant_id"); if (enchant.Length > 0) value += ",enchant_id=" + enchant;
                    var socketCount = Array(Field(Field(item, "socketInfo"), "sockets")).Count();
                    var gems = Property(slot, "gem_id").Split('/', StringSplitOptions.RemoveEmptyEntries).Take(socketCount).ToArray();
                    if (gems.Length > 0) value += ",gem_id=" + string.Join('/', gems);
                    var key = "gear_" + cases.Count;
                    var bagItem = new BagItem(cases.Count, Text(item, "name"), slot, id, value, [slot], "");
                    var changes = new List<GearChange> { new(bagItem, slot) };
                    if (inventory == 17 && member.SpecId != 72 && EquippedId("off_hand") > 0)
                        changes.Add(new(new BagItem(-1, "Empty off hand", "off_hand", 0, "none", ["off_hand"], ""), "off_hand"));
                    var displayName = Text(drop, "name");
                    if (id != Int(drop, "id")) displayName += " → " + Text(item, "name");
                    cases.Add(new(key, new SimUpgrade(Int(drop, "id"), id, displayName, source.Name, source.Id, bossId,
                        Text(boss, "name"), difficultyKey, level, slot, 0, 0, true, bonusIds), new GearSet(key, changes)));
                }
            }
        }
        if (cases.Count is 0 or > 500) throw new InvalidOperationException($"This player has {cases.Count} candidates; local queues require 1–500 per player.");
        return new(member, cases, sourceId + "/" + difficultyKey, "Local Droptimizer • " + source.Name + " • " + difficulty,
            "Drops at their original level; tier/catalyst conversions included; existing slot enchants; gems only in built-in sockets; no equipped upgrades or bag optimization. "
            + (skippedWeapons > 0 ? $"Skipped {skippedWeapons} weapon variants needing a different weapon pair. " : "")
            + (member.SpecId == 1473 ? "Augmentation uses the engine's single-player model; raid contribution is not fully represented. " : "")
            + "Profile captured " + DateTimeOffset.FromUnixTimeSeconds(member.Updated).ToString("u"));
    }
}

internal static class JsonNumber
{
    internal static int TryInt(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
