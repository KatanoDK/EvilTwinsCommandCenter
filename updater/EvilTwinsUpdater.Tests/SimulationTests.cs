using System.Diagnostics;
using System.Text.Json;
using EvilTwinsUpdater.Core;

internal static class SimulationTests
{
    private const string Profile = "# addon export\ndeathknight=\"Fixture\"\nspec=unholy\nlevel=90\nrace=human\n"
        + "talents=TEST+/=\nhead=,id=123,enchant_id=456,gem_id=789\n# Gear from Bags\n# head=,id=999\n";

    public static async Task Run(string testRoot)
    {
        var prepared = LocalSimulator.PrepareProfile(Profile);
        Assert(prepared.Contains("id=123") && !prepared.Contains("id=999"), "Bag comments must not replace equipped gear.");
        Assert(LocalSimulator.PrepareProfile(Profile.Replace("deathknight", "death_knight")).Contains("death_knight"), "Class aliases must work.");
        Assert(LocalSimulator.PrepareProfile(Profile + "shoulders=,id=1\nwrists=,id=2\n").Contains("wrists"), "Upstream slot aliases must work.");
        foreach (var extra in new[] { "input=outside.simc", "json2=outside.json", "html=outside.html", "armory=us,realm,name",
            "warrior=Other", "actions=/attack", "race=human input=outside.simc", "threads=999", "profileset.test=head=,id=999" })
            Reject(() => LocalSimulator.PrepareProfile(Profile + extra), "Unexpected profile directive: " + extra);
        Reject(() => LocalSimulator.PrepareProfile("ETD1:abcd"), "Addon code is not a character export");
        Reject(() => LocalSimulator.PrepareProfile("warrior=Test\nlevel=90"), "Missing specialization");
        Reject(() => LocalSimulator.PrepareProfile(Profile + new string('x', 100_000)), "Oversized profile");
        Assert(SimulationOptions.ThreadCount(SimulationMode.Background, 16) == 8
            && SimulationOptions.ThreadCount(SimulationMode.FullSpeed, 16) == 16
            && SimulationOptions.ThreadCount(SimulationMode.Background, 1) == 1, "CPU modes must respect available threads.");

        var engineDirectory = Path.Combine(testRoot, "fixture-engine");
        Directory.CreateDirectory(engineDirectory);
        // Rename the test apphost, retaining its DLL/runtime files, to exercise the real process runner without SimC in CI.
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
            File.Copy(file, Path.Combine(engineDirectory, Path.GetFileName(file)), true);
        var engine = Path.Combine(engineDirectory, "simc.exe");
        File.Copy(Path.Combine(engineDirectory, "EvilTwinsUpdater.Tests.exe"), engine, true);
        File.WriteAllText(Path.Combine(engineDirectory, ".fake-simc"), "test fixture");
        var runner = new LocalSimulator();
        var runs = Path.Combine(testRoot, "simulations with spaces");
        var result = await runner.RunAsync(engine, Profile, new SimulationOptions(1000), runs);
        Assert(result.Name == "Fixture" && result.Dps == 12345 && Math.Abs(result.Error!.Value - 19.6) < 0.001,
            "Result must use mean DPS and the confidence-adjusted standard error.");
        Assert(result.Iterations == 1000 && result.Threads == new SimulationOptions().Threads
            && result.EngineRevision == "fixture-revision", "Result must retain engine and run settings.");
        Assert(result.Warnings.Count == 1 && result.Warnings[0].Contains("Fixture mechanic is experimental"),
            "Engine warnings must remain visible alongside successful results.");
        Assert(File.Exists(result.HtmlPath) && File.Exists(result.JsonPath)
            && File.ReadAllText(Path.Combine(result.Directory, "console.txt")).Contains("fixture-ready"), "Reports and console output must be retained.");
        var other = await runner.RunAsync(engine, Profile, new SimulationOptions(1000, Mode: SimulationMode.FullSpeed), runs);
        Assert(other.Directory != result.Directory && File.Exists(result.HtmlPath), "New runs must not overwrite old results.");
        Assert(other.Threads == Environment.ProcessorCount, "Full speed must use all available CPU threads.");
        await BagComparisonTests.Run(runner, engine, runs);
        await RejectAsync(() => runner.RunAsync(engine, Profile.Replace("Fixture", "FixtureFail"), new SimulationOptions(), runs),
            error => error.Message.Contains("code 40") && error.Message.Contains("Unsupported specialization"), "Engine error details");
        await RejectAsync(() => runner.RunAsync(engine, Profile.Replace("Fixture", "FixtureInvalid"), new SimulationOptions(), runs),
            error => error is InvalidDataException, "Malformed result must not appear successful");
        using var cancellation = new CancellationTokenSource();
        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = runner.RunAsync(engine, Profile.Replace("Fixture", "FixtureSlow"), new SimulationOptions(), runs,
            new InlineProgress(text => {
                var match = System.Text.RegularExpressions.Regex.Match(text, "fixture-ready:(\\d+)");
                if (match.Success) ready.TrySetResult(int.Parse(match.Groups[1].Value));
            }), cancellation.Token);
        try
        {
            var pid = await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            cancellation.Cancel();
            try { await task; throw new Exception("Cancellation was ignored."); }
            catch (OperationCanceledException) { }
            try { using var process = Process.GetProcessById(pid); Assert(process.HasExited, "Cancelled engine must exit before runner completes."); }
            catch (ArgumentException) { }
        }
        finally { cancellation.Cancel(); try { await task; } catch (OperationCanceledException) { } }
        Console.WriteLine("PASS: local simulator profiles, CPU modes, native process execution, report parsing, errors, saved runs, and cancellation.");
    }

    public static async Task FakeEngine(string[] args)
    {
        var input = File.ReadAllText("input.simc");
        Console.WriteLine($"fixture-ready:{Environment.ProcessId}");
        Console.Out.Flush();
        if (input.Contains("FixtureSlow")) { await Task.Delay(TimeSpan.FromMinutes(2)); return; }
        if (input.Contains("FixtureFail")) { Console.Error.WriteLine("Unsupported specialization fixture."); Environment.ExitCode = 40; return; }
        if (input.Contains("FixtureInvalid")) { File.WriteAllText("report.json", "{}"); return; }
        Assert(args.Contains("item_db_source=local") && args.Contains("fight_style=Patchwerk")
            && args.Contains("target_error=0") && args.Contains("json2=report.json")
            && args.Contains("progressbar_type=1"), "Expected controlled simulation settings and parseable progress output.");
        int Option(string key) => int.Parse(args.Single(arg => arg.StartsWith(key + "="))[(key.Length + 1)..]);
        var sets = input.Split('\n').Where(line => line.StartsWith("profileset.", StringComparison.Ordinal))
            .GroupBy(line => line[11..line.IndexOf('=')].TrimEnd('+'))
            .Select(group => new { name = input.Contains("FixtureMismatch") ? "unknown" : group.Key,
                mean = 12345.0 + group.Count() * 150, mean_error = 20.0, iterations = Option("iterations") }).ToArray();
        if (input.Contains("FixtureMissing")) sets = [];
        Console.WriteLine($"Baseline\t1\t{sets.Length + 1}\t{Option("iterations")}\t{Option("iterations")}\t100\t1");
        for (var index = 0; index < sets.Length; index++)
        {
            if (input.Contains("FixtureQueue")) await Task.Delay(20);
            Console.WriteLine($"Profileset\t{sets[index].name}\t{index + 2}\t{sets.Length + 1}\t{Option("iterations")}\t{Option("iterations")}\t100\t1");
        }
        var report = new { version = "fixture", git_revision = "fixture-revision",
            logs = new[] { new { level = "implementation_not_yet_verified", message = "Fixture mechanic is experimental." } }, sim = new {
            options = new { iterations = Option("iterations"), threads = Option("threads"), confidence = 0.95, confidence_estimator = 1.96 },
            profilesets = new { metric = "Damage per Second", results = sets },
            players = new[] { new { name = "Fixture", specialization = "unholy", collected_data = new { dps = new { mean = 12345.0, mean_std_dev = 10.0 } } } } } };
        File.WriteAllText("report.json", JsonSerializer.Serialize(report));
        File.WriteAllText("report.html", "<html><body>Fixture report</body></html>");
    }

    public static async Task VerifyReal(string engine, string profilePath, string outputRoot)
    {
        var runner = new LocalSimulator();
        foreach (var mode in new[] { SimulationMode.Background, SimulationMode.FullSpeed })
        {
            var profile = File.ReadAllText(profilePath);
            var selected = BagComparisons.ReadItems(profile).Where(item => item.CanCompare).Select(item => item.Key).ToArray();
            var tracker = new SimulationProgress(BagComparisons.CreateSets(profile, selected).Count + 1);
            var result = await runner.RunAsync(engine, profile, new SimulationOptions(1000, Mode: mode), outputRoot,
                new InlineProgress(tracker.Append), selectedBagItems: selected);
            Assert(result.Dps > 0 && result.Error.HasValue && File.Exists(result.HtmlPath), "Live engine did not produce a usable result.");
            Assert(tracker.HasProgress && tracker.Fraction == 1 && tracker.CompletedRuns == tracker.TotalRuns, "Live engine progress must reach all completed runs.");
            Assert(result.Comparisons.Count == tracker.TotalRuns - 1, "All live gear combinations must be returned.");
            Console.WriteLine($"{mode}: {result.Threads} threads, 1000 iterations, {result.Elapsed.TotalSeconds:F2}s, {result.Dps:F0} DPS ± {result.Error:F0}; engine {result.EngineVersion} {result.EngineRevision}");
            Console.WriteLine("Run files: " + result.Directory);
            foreach (var comparison in result.Comparisons)
                Console.WriteLine($"{comparison.Set.Changes.Count} change(s), {comparison.Set.Slots}: {comparison.Gain:+0;-0;0} DPS ± {comparison.GainError:F0}");
        }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected rejection: " + message);
    }
    private static async Task RejectAsync(Func<Task> action, Func<Exception, bool> expected, string message)
    {
        try { await action(); } catch (Exception error) when (expected(error)) { return; }
        throw new Exception("Expected rejection: " + message);
    }
    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    { public void Report(string value) => report(value); }
}
