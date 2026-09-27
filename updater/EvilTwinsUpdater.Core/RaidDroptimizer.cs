namespace EvilTwinsUpdater.Core;

public sealed record RaidDropResult(RaidMember Member, DropReport Report, string Directory, IReadOnlyList<string> Warnings)
{
    public int Iterations { get; init; }
}
public sealed record LootBenefit(RaidMember Member, DropReport Report, SimUpgrade Item);
public static class RaidDroptimizer
{
    public static bool Matches(RaidMember member, DropReport report)
    {
        var gear = new Dictionary<string, string>(); var talents = "";
        var slots = new HashSet<string> { "head", "neck", "shoulder", "back", "chest", "wrist", "hands", "waist", "legs", "feet", "finger1", "finger2", "trinket1", "trinket2", "main_hand", "off_hand", "shirt", "tabard" };
        foreach (var line in LocalSimulator.PrepareProfile(member.Profile).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('=', 2); var slot = parts[0] switch { "shoulders" => "shoulder", "wrists" => "wrist", _ => parts[0] };
            if (slots.Contains(slot))
            {
                var signature = DroptimizerClient.GearSignature(parts[1]);
                if (signature.Split(';')[0].Length > 0) gear[slot] = signature;
            }
            if (slot == "talents") talents = parts[1];
        }
        var reported = report.Gear.Where(pair => pair.Value.Split(';')[0].Length > 0).ToArray();
        return reported.Length > 0 && reported.Length == gear.Count && talents.Length > 0 && talents == report.Talents
            && reported.All(pair => gear.GetValueOrDefault(pair.Key) == pair.Value);
    }
    public static IEnumerable<LootBenefit> Benefits(IEnumerable<RaidDropResult> results) => results
        .SelectMany(result => result.Report.Items.Select(item => new LootBenefit(result.Member, result.Report, item)))
        .OrderByDescending(value => value.Item.Gain);

    public static async Task<RaidDropResult> RunAsync(string engine, RaidExport raid, DropPlan plan, SimulationOptions options,
        string output, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        var result = await new LocalSimulator().RunSetsAsync(engine, plan.Member.Profile, options, output,
            plan.Cases.Select(value => value.Set).ToArray(), progress, cancellation);
        if (result.Dps <= 0) throw new InvalidDataException("The engine did not return a positive DPS baseline.");
        var byKey = plan.Cases.ToDictionary(value => value.Key);
        var items = result.Comparisons.Where(comparison => comparison.Gain > 0).Select(comparison => {
            var item = byKey[comparison.Set.Key].Item;
            return item with { Gain = comparison.Gain, Percent = comparison.Gain / result.Dps * 100,
                WithinError = comparison.WithinUncertainty != false };
        }).GroupBy(item => (item.DropId, item.InstanceId, item.BossId, item.Difficulty, item.ItemLevel))
            .Select(group => group.OrderByDescending(item => item.Gain).First()).OrderByDescending(item => item.Gain).Take(100).ToArray();
        var gear = new Dictionary<string, string>(); var talents = "";
        var slots = new HashSet<string> { "head", "neck", "shoulder", "back", "chest", "wrist", "hands", "waist", "legs", "feet", "finger1", "finger2", "trinket1", "trinket2", "main_hand", "off_hand", "shirt", "tabard" };
        foreach (var line in LocalSimulator.PrepareProfile(plan.Member.Profile).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('=', 2); var slot = parts[0] switch { "shoulders" => "shoulder", "wrists" => "wrist", _ => parts[0] };
            if (slots.Contains(slot)) gear[slot] = DroptimizerClient.GearSignature(parts[1]);
            if (slot == "talents") talents = parts[1];
        }
        var identity = plan.Member.Key.Split('-', 2);
        var report = new DropReport(identity[0], identity[1], raid.Region, plan.Member.SpecId, "Local" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), plan.Title, "Patchwerk", options.Duration, options.Targets, result.Dps, plan.Scope, talents,
            $"Local CPU; {options.Iterations:N0} iterations; drops as looted; conversions included. Slot enchants retained; intrinsic sockets only.", gear, items);
        _ = report.ImportCode(); // Ensure the result fits the addon's existing report format before marking it complete.
        return new(plan.Member, report, result.Directory, new[] { plan.Notes }.Concat(result.Warnings).ToArray()) { Iterations = options.Iterations };
    }
}
