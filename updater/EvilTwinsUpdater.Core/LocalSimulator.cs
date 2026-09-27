using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvilTwinsUpdater.Core;

public enum SimulationMode { Background, FullSpeed }
public sealed record SimulationOptions(int Iterations = 5000, int Duration = 300, int Targets = 1,
    SimulationMode Mode = SimulationMode.Background)
{
    public int Threads => ThreadCount(Mode, Environment.ProcessorCount);
    public static int ThreadCount(SimulationMode mode, int processors) => mode == SimulationMode.Background
        ? Math.Max(1, processors / 2) : Math.Max(1, processors);
}
public sealed record SimulationResult(string Name, string Specialization, double Dps, double? Error,
    double Confidence, int Iterations, int Threads, string EngineVersion, string EngineRevision,
    TimeSpan Elapsed, string Directory, string HtmlPath, string JsonPath, IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<GearComparison> Comparisons { get; init; } = [];
}

public sealed class LocalSimulator
{
    private static readonly HashSet<string> Classes = new(StringComparer.Ordinal) {
        "death_knight", "deathknight", "demon_hunter", "demonhunter", "druid", "evoker", "hunter", "mage", "monk", "paladin", "priest", "rogue", "shaman", "warlock", "warrior" };
    private static readonly HashSet<string> ProfileOptions = new(StringComparer.Ordinal) {
        "level", "race", "role", "position", "spec", "talents", "class_talents", "spec_talents", "hero_talents",
        "region", "server", "professions", "zandalari_loa", "earthen_mineral", "vulpera_tricks", "omnium_talents",
        "head", "neck", "shoulder", "shoulders", "shirt", "chest", "waist", "legs", "feet", "wrist", "wrists", "hands", "finger1", "finger2",
        "trinket1", "trinket2", "back", "main_hand", "off_hand", "ranged", "tabard",
        "potion", "flask", "food", "augmentation", "temporary_enchant" };

    public static string ValidateEngine(string path)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetFileName(path), "simc.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidOperationException("Choose simc.exe from an extracted Windows SimulationCraft download.");
        return path;
    }

    // Accept one exported character, without file includes, output directives, Armory imports or custom scripts.
    // Bag alternatives remain comments; they must not overwrite the equipped slots during a basic sim.
    public static string PrepareProfile(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile) || Encoding.UTF8.GetByteCount(profile) > 100_000
            || profile.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
            throw new InvalidOperationException("Paste a character's SimC export (maximum 100 KB).");
        var result = new StringBuilder();
        var characters = 0;
        var hasSpec = false;
        foreach (var raw in profile.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator < 1) throw new InvalidOperationException("Paste a SimC character export, not a report or addon import code.");
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (Classes.Contains(key))
            {
                if (++characters != 1 || !Regex.IsMatch(value, "^(?:\"[^\"\\r\\n]{1,100}\"|[^\\s\"=]{1,100})$"))
                    throw new InvalidOperationException("A basic simulation needs exactly one character export.");
            }
            else
            {
                if (characters != 1 || !ProfileOptions.Contains(key))
                    throw new InvalidOperationException($"'{key}' is not supported here. Paste the character export from the addon.");
                if (!Regex.IsMatch(value, "^[A-Za-z0-9_,:=/+.-]*$"))
                    throw new InvalidOperationException($"Unexpected value for '{key}'. Use the original addon export.");
                if (key == "spec" && value.Length > 0) hasSpec = true;
            }
            result.Append(key).Append('=').AppendLine(value);
        }
        if (characters != 1 || !hasSpec) throw new InvalidOperationException("The export must contain one character and its specialization.");
        return result.ToString();
    }

    public async Task<SimulationResult> RunAsync(string engine, string profile, SimulationOptions options,
        string outputRoot, IProgress<string>? progress = null, CancellationToken cancellation = default,
        IReadOnlyCollection<int>? selectedBagItems = null)
    {
        var swaps = selectedBagItems is { Count: > 0 } ? BagComparisons.CreateSets(profile, selectedBagItems) : [];
        return await RunSetsAsync(engine, profile, options, outputRoot, swaps, progress, cancellation);
    }

    // Only trusted, validated catalog code can construct variants; pasted profiles cannot inject profilesets.
    internal async Task<SimulationResult> RunSetsAsync(string engine, string profile, SimulationOptions options,
        string outputRoot, IReadOnlyList<GearSet> swaps, IProgress<string>? progress, CancellationToken cancellation)
    {
        engine = ValidateEngine(engine);
        profile = BagComparisons.AppendSets(PrepareProfile(profile), swaps);
        if (options.Iterations is < 100 or > 100_000 || options.Duration is < 30 or > 900
            || options.Targets is < 1 or > 20 || !Enum.IsDefined(options.Mode))
            throw new InvalidOperationException("Choose valid simulation settings.");
        cancellation.ThrowIfCancellationRequested();
        var run = Path.Combine(Path.GetFullPath(outputRoot), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(run);
        var inputPath = Path.Combine(run, "input.simc");
        await File.WriteAllTextAsync(inputPath, profile, new UTF8Encoding(false), cancellation);
        var start = new ProcessStartInfo(engine) {
            WorkingDirectory = run, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("input.simc");
        foreach (var arg in new[] { "json2=report.json", "html=report.html", "report_details=1", "hosted_html=0",
            "fight_style=Patchwerk", "optimal_raid=1", "fixed_time=1", "vary_combat_length=0.2", "target_error=0", "confidence=0.95",
            "progressbar_type=1",
            "item_db_source=local", "calculate_scale_factors=0", $"iterations={options.Iterations}",
            $"max_time={options.Duration}", $"desired_targets={options.Targets}", $"threads={options.Threads}",
            options.Mode == SimulationMode.Background ? "process_priority=below_normal" : "process_priority=normal" })
            start.ArgumentList.Add(arg);
        if (swaps.Count > 0) start.ArgumentList.Add("profileset_metric=dps");
        using var process = new Process { StartInfo = start };
        var elapsed = Stopwatch.StartNew();
        progress?.Report($"Starting {options.Threads} CPU thread(s)…");
        if (!process.Start()) throw new InvalidOperationException("SimulationCraft could not start.");
        using var registration = cancellation.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        using var log = new StreamWriter(Path.Combine(run, "console.txt"), false, new UTF8Encoding(false));
        var logLock = new object();
        var tail = new StringBuilder();
        void Record(string message)
        {
            lock (logLock)
            {
                log.Write(message);
                tail.Append(message);
                if (tail.Length > 8000) tail.Remove(0, tail.Length - 8000);
            }
            progress?.Report(message);
        }
        var stdout = ReadOutputAsync(process.StandardOutput, Record);
        var stderr = ReadOutputAsync(process.StandardError, Record);
        // Await exit without cancellation: cancellation kills the process, then drains both pipes and closes the log.
        // This guarantees the cancelled engine is gone before the UI enables another run or exits.
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        elapsed.Stop();
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"SimulationCraft exited with code {process.ExitCode}.\n{tail.ToString().Trim()}\nRun files: {run}");
        var jsonPath = Path.Combine(run, "report.json");
        if (!File.Exists(jsonPath) || new FileInfo(jsonPath).Length > 50_000_000)
            throw new InvalidDataException($"SimulationCraft did not produce a readable result. Run files: {run}");
        try
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath, cancellation));
            var baseline = ParseResult(json.RootElement, run, elapsed.Elapsed);
            return baseline with { Comparisons = BagComparisons.ReadResults(json.RootElement, baseline, swaps) };
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"{error.Message}\nRun files: {run}", error);
        }
    }

    private static async Task ReadOutputAsync(StreamReader reader, Action<string> record)
    {
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0) record(new string(buffer, 0, count));
    }

    public static SimulationResult ParseResult(JsonElement root, string directory, TimeSpan elapsed)
    {
        if (!root.TryGetProperty("sim", out var sim) || !sim.TryGetProperty("players", out var players)
            || players.ValueKind != JsonValueKind.Array || players.GetArrayLength() != 1)
            throw new InvalidDataException("Expected one character in the simulation result.");
        var player = players[0];
        if (!player.TryGetProperty("collected_data", out var data) || !data.TryGetProperty("dps", out var dps)
            || !dps.TryGetProperty("mean", out var mean) || !mean.TryGetDouble(out var damage) || !double.IsFinite(damage) || damage < 0)
            throw new InvalidDataException("The engine did not report a valid DPS result for this specialization.");
        var options = sim.GetProperty("options");
        var confidence = options.GetProperty("confidence").GetDouble();
        if (!double.IsFinite(confidence) || confidence <= 0 || confidence >= 1)
            throw new InvalidDataException("The engine reported an invalid confidence level.");
        double? error = null;
        if (dps.TryGetProperty("mean_std_dev", out var deviation) && deviation.TryGetDouble(out var value)
            && options.TryGetProperty("confidence_estimator", out var estimator) && estimator.TryGetDouble(out var factor)
            && double.IsFinite(value) && value >= 0 && double.IsFinite(factor) && factor > 0)
        {
            var adjusted = value * factor;
            if (double.IsFinite(adjusted)) error = adjusted;
        }
        string Text(JsonElement element, string name) => element.TryGetProperty(name, out var field) ? field.GetString() ?? "" : "";
        var warnings = new List<string>();
        if (root.TryGetProperty("logs", out var logs) && logs.ValueKind == JsonValueKind.Array)
            foreach (var entry in logs.EnumerateArray().Take(20))
            {
                var message = Text(entry, "message");
                if (message.Length > 0) warnings.Add(Text(entry, "level").Replace('_', ' ') + ": " + message[..Math.Min(message.Length, 1000)]);
            }
        return new SimulationResult(Text(player, "name"), Text(player, "specialization"), damage, error,
            confidence, options.GetProperty("iterations").GetInt32(),
            options.GetProperty("threads").GetInt32(), Text(root, "version"), Text(root, "git_revision"), elapsed,
            directory, Path.Combine(directory, "report.html"), Path.Combine(directory, "report.json"), warnings);
    }
}
