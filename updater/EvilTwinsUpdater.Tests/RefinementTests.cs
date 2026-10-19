using System.Text.Json;
using EvilTwinsUpdater.Core;

internal static class RefinementTests
{
    public static async Task Run(string engine, string runs)
    {
        var member = new RaidMember("FixtureRefinement-Realm", "Player-1", "DAMAGER", 252,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "deathknight=\"FixtureRefinement\"\nspec=unholy\nlevel=90\ntalents=FixtureTalent\nmain_hand=,id=268198\nhead=,id=271474,bonus_id=12845\n", "Ready");
        var raid = RaidExport.Parse(RaidTests.Code(member));
        var original = RaidTests.Catalog().Plan(member, 1320, "Heroic");
        var template = original.Cases[0];
        DropCase Case(string key, int drop) => template with { Key = key,
            Item = template.Item with { DropId = drop }, Set = template.Set with { Key = key } };
        var cases = new[] { Case("upgrade", 1001), Case("negative", 1002), Case("uncertain", 1003),
            Case("variant", 1001), Case("unknown", 1004), Case("boundary", 1005), Case("flat", 1006) };
        var plan = original with { Cases = cases };
        GearComparison Comparison(int index, double gain, double? error) => new(cases[index].Set, 10000 + gain, gain, gain / 100, error, 1000);
        var comparisons = new[] { Comparison(0, 100, 10), Comparison(1, -500, 20), Comparison(2, -10, 20),
            Comparison(3, -2000, 10), Comparison(4, -500, null), Comparison(5, -40, 20), Comparison(6, 0, 0) };
        var kept = RaidDroptimizer.RefinementCases(plan, comparisons).Select(value => value.Key).ToHashSet();
        Assert(kept.SetEquals(["upgrade", "uncertain", "variant", "unknown", "boundary", "flat"]),
            "Screening must retain positive, uncertain, unknown-error, boundary and alternate-slot/conversion candidates.");
        try { RaidDroptimizer.RefinementCases(plan, comparisons.Take(3).ToArray()); throw new Exception("Incomplete screening accepted."); }
        catch (InvalidDataException) { }

        plan = plan with { Cases = cases.Take(4).ToArray() };
        var root = Path.Combine(runs, "automatic refinement");
        var phases = new List<RaidSimulationPhase>();
        var tracked = new List<SimulationProgress>();
        SimulationProgress? current = null;
        var phaseProgress = new Callback<RaidSimulationPhase>(value => {
            phases.Add(value); current = new SimulationProgress(value.Cases + 1); tracked.Add(current);
        });
        var options = new SimulationOptions(5000, Mode: SimulationMode.FullSpeed);
        var result = await RaidDroptimizer.RunAsync(engine, raid, plan, options, root,
            new Callback<string>(value => current?.Append(value)), automaticRefinement: true, phases: phaseProgress);
        Assert(phases.Count == 2 && phases[0].Name == "Screening" && phases[0].Iterations == 1000
            && phases[1].Name == "Refining" && phases[1].Iterations == 5000 && phases[1].Cases == 3,
            "Automatic mode must screen all cases and refine only surviving drop groups.");
        Assert(phases[1].StartFraction >= phases[0].Weight && phases[1].StartFraction + phases[1].Weight == 1,
            "Stage transitions must not move progress backwards.");
        Assert(tracked.All(value => value.Fraction == 1), "Each stage must parse its own native progress totals.");
        Assert(Directory.GetDirectories(root).Length == 2 && result.AutomaticRefinement && result.Iterations == 5000
            && result.ScreenedCases == 4 && result.RefinedCases == 3, "Retain precision and screening metadata.");
        Assert(result.Report.Baseline == 20000 && result.Report.Items.Single(value => value.DropId == 1001).Gain == 100,
            "Published upgrades must use the new refined baseline and gains, never the screening estimates.");
        Assert(result.Report.Items.Count == 2 && result.Report.Items.Single(value => value.DropId == 1003).WithinError,
            "Refined comparisons that remain uncertain must stay marked.");
        Assert(result.Report.Assumptions.Contains("1,000") && result.Report.Assumptions.Contains("5,000")
            && result.Report.ImportCode().StartsWith("ETD1:"), "Shared reports must disclose the two stages and remain addon-compatible.");
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Directory, "report.json"))))
            Assert(json.RootElement.GetProperty("sim").GetProperty("options").GetProperty("threads").GetInt32() == Environment.ProcessorCount,
                "Full speed must reach the native engine in automatic mode.");
        var restored = JsonSerializer.Deserialize<RaidDropResult>(JsonSerializer.Serialize(result))!;
        Assert(restored.AutomaticRefinement && restored.RefinedCases == 3
            && RaidDroptimizer.MatchesSettings(restored, plan.Scope, options with { Mode = SimulationMode.Background }, true),
            "Saved results must retain automatic precision, and CPU changes must not invalidate completed results.");
        Assert(!RaidDroptimizer.MatchesSettings(restored, plan.Scope, options, false)
            && !RaidDroptimizer.MatchesSettings(restored, plan.Scope, options with { Iterations = 20000 }, true),
            "Changing precision or automatic/fixed mode must invalidate completed results.");

        var noneMember = member with { Key = "FixtureRefinementNone-Realm", Profile = member.Profile.Replace("FixtureRefinement", "FixtureRefinementNone") };
        var noneRoot = Path.Combine(runs, "automatic none");
        var none = await RaidDroptimizer.RunAsync(engine, raid, plan with { Member = noneMember }, options, noneRoot, automaticRefinement: true);
        Assert(none.RefinedCases == 0 && none.Report.Items.Count == 0 && Directory.GetDirectories(noneRoot).Length == 1,
            "A screen with no surviving drops must finish without a redundant refinement process.");

        var failMember = member with { Key = "FixtureRefinementFail-Realm", Profile = member.Profile.Replace("FixtureRefinement", "FixtureRefinementFail") };
        try
        {
            await RaidDroptimizer.RunAsync(engine, raid, plan with { Member = failMember }, options,
                Path.Combine(runs, "automatic failure"), automaticRefinement: true);
            throw new Exception("A failed refinement was reported as complete.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("Refinement fixture failure")) { }
        using var stop = new CancellationTokenSource();
        try
        {
            await RaidDroptimizer.RunAsync(engine, raid, plan, options, Path.Combine(runs, "automatic cancellation"),
                cancellation: stop.Token, automaticRefinement: true, phases: new Callback<RaidSimulationPhase>(value => {
                    if (value.Name == "Refining") stop.Cancel();
                }));
            throw new Exception("Cancellation between stages was ignored.");
        }
        catch (OperationCanceledException) { }
        var fixedRoot = Path.Combine(runs, "fixed precision");
        var fixedResult = await RaidDroptimizer.RunAsync(engine, raid, plan, options, fixedRoot);
        Assert(!fixedResult.AutomaticRefinement && Directory.GetDirectories(fixedRoot).Length == 1,
            "Fixed precision must keep the original single-pass behavior.");
        Console.WriteLine("PASS: conservative screening, variant retention, refined baselines, CPU selection, stage progress, saved precision, no-upgrade shortcut, refinement failure, cancellation, and fixed runs.");
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Callback<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
