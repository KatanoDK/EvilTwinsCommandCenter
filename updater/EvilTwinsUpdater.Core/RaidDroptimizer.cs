namespace EvilTwinsUpdater.Core;

public sealed record RaidDropResult(RaidMember Member, DropReport Report, string Directory, IReadOnlyList<string> Warnings)
{
    public int Iterations { get; init; }
    public bool AutomaticRefinement { get; init; }
    public int ScreenedCases { get; init; }
    public int RefinedCases { get; init; }
}
public sealed record RaidSimulationPhase(string Name, int Cases, int Iterations, double StartFraction, double Weight);
public sealed record LootBenefit(RaidMember Member, DropReport Report, SimUpgrade Item);
public static class RaidDroptimizer
{
    public const int ScreeningIterations = 1000;

    public static bool MatchesSettings(RaidDropResult result, string scope, SimulationOptions options, bool automaticRefinement) =>
        result.Report.Scope == scope && result.Iterations == options.Iterations && result.AutomaticRefinement == automaticRefinement
        && result.Report.FightStyle == "Patchwerk" && result.Report.Duration == options.Duration && result.Report.Enemies == options.Targets;

    private static (int Drop, int Instance, int Boss, string Difficulty, int Level) DropIdentity(SimUpgrade item) =>
        (item.DropId, item.InstanceId, item.BossId, item.Difficulty, item.ItemLevel);

    public static IReadOnlyList<DropCase> RefinementCases(DropPlan plan, IReadOnlyList<GearComparison> comparisons)
    {
        var byKey = comparisons.ToDictionary(value => value.Set.Key);
        if (byKey.Count != plan.Cases.Count || plan.Cases.Any(value => !byKey.ContainsKey(value.Key)))
            throw new InvalidDataException("The screening results do not match the loot candidates.");
        // Reject only clearly negative drops. Unknown error always survives screening.
        // Keeping every variant of a surviving drop avoids eliminating an alternate ring slot or conversion.
        var retained = plan.Cases.Where(value => {
            var comparison = byKey[value.Key];
            return !comparison.GainError.HasValue || comparison.Gain + 2 * comparison.GainError.Value >= 0;
        }).Select(value => DropIdentity(value.Item)).ToHashSet();
        return plan.Cases.Where(value => retained.Contains(DropIdentity(value.Item))).ToArray();
    }
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
        string output, IProgress<string>? progress = null, CancellationToken cancellation = default,
        bool automaticRefinement = false, IProgress<RaidSimulationPhase>? phases = null)
    {
        if (automaticRefinement && options.Iterations <= ScreeningIterations)
            throw new InvalidOperationException("Automatic refinement needs a final precision above 1,000 iterations.");
        var runner = new LocalSimulator();
        var finalCases = plan.Cases;
        SimulationResult result;
        string? screeningDirectory = null;
        if (automaticRefinement)
        {
            var screeningShare = (double)ScreeningIterations / (ScreeningIterations + options.Iterations);
            phases?.Report(new("Screening", plan.Cases.Count, ScreeningIterations, 0, screeningShare));
            var screened = await runner.RunSetsAsync(engine, plan.Member.Profile, options with { Iterations = ScreeningIterations },
                output, plan.Cases.Select(value => value.Set).ToArray(), progress, cancellation);
            if (screened.Dps <= 0) throw new InvalidDataException("The engine did not return a positive screening baseline.");
            screeningDirectory = screened.Directory;
            finalCases = RefinementCases(plan, screened.Comparisons);
            if (finalCases.Count > 0)
            {
                var screenWork = (double)(plan.Cases.Count + 1) * ScreeningIterations;
                var refineWork = (double)(finalCases.Count + 1) * options.Iterations;
                var start = screenWork / (screenWork + refineWork);
                phases?.Report(new("Refining", finalCases.Count, options.Iterations, start, 1 - start));
                result = await runner.RunSetsAsync(engine, plan.Member.Profile, options, output,
                    finalCases.Select(value => value.Set).ToArray(), progress, cancellation);
                result = result with { Warnings = screened.Warnings.Concat(result.Warnings).Distinct().ToArray() };
            }
            else result = screened with { Comparisons = [] };
        }
        else
        {
            phases?.Report(new("Simulating", plan.Cases.Count, options.Iterations, 0, 1));
            result = await runner.RunSetsAsync(engine, plan.Member.Profile, options, output,
                finalCases.Select(value => value.Set).ToArray(), progress, cancellation);
        }
        cancellation.ThrowIfCancellationRequested();
        if (result.Dps <= 0) throw new InvalidDataException("The engine did not return a positive DPS baseline.");
        var byKey = finalCases.ToDictionary(value => value.Key);
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
        var precision = automaticRefinement
            ? $"Automatic: {ScreeningIterations:N0} screening iterations for {plan.Cases.Count} candidates; {finalCases.Count} refined at {options.Iterations:N0}."
            : $"Local CPU; {options.Iterations:N0} iterations.";
        var report = new DropReport(identity[0], identity[1], raid.Region, plan.Member.SpecId, "Local" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), plan.Title, "Patchwerk", options.Duration, options.Targets, result.Dps, plan.Scope, talents,
            precision + " Drops as looted; conversions included. Slot enchants retained; intrinsic sockets only.", gear, items);
        _ = report.ImportCode(); // Ensure the result fits the addon's existing report format before marking it complete.
        var warnings = new List<string> { plan.Notes };
        if (automaticRefinement)
            warnings.Add($"Automatic screening skipped {plan.Cases.Count - finalCases.Count} clearly negative candidates. Screening can miss small gains; use a fixed precision run to test every candidate. Screening files: {screeningDirectory}");
        warnings.AddRange(result.Warnings);
        return new(plan.Member, report, result.Directory, warnings.ToArray()) { Iterations = options.Iterations,
            AutomaticRefinement = automaticRefinement, ScreenedCases = automaticRefinement ? plan.Cases.Count : 0,
            RefinedCases = finalCases.Count };
    }
}
