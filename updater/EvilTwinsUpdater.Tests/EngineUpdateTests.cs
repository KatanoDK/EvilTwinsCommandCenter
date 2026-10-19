using System.Net;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using EvilTwinsUpdater.Core;

internal static class EngineUpdateTests
{
    private const string First = "simc-1210.01.aaaaaaa-win64.7z";
    private const string Second = "simc-1210.01.bbbbbbb-win64.7z";
    private static string Index(string name) => $"<a href='{name}'>{name}</a>";
    private static byte[] Archive(params (string Path, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream();
        using (var writer = WriterFactory.OpenWriter(output, ArchiveType.SevenZip,
            new SevenZipWriterOptions(CompressionType.LZMA2) { LeaveStreamOpen = true }))
            foreach (var (path, bytes) in entries)
            {
                using var input = new MemoryStream(bytes);
                writer.Write(path, input);
            }
        return output.ToArray();
    }

    public static async Task Run(string testRoot)
    {
        var parsed = SimulationCraftUpdates.ParseLatest(Index("simc-1205.01.ccccccc-win64.7z") + Index(First)
            + Index("simc-9999.01.fffffff-winarm64.7z") + Index("simc-9999-01-macos-fffffff.dmg")
            + Index("https://example.com/nightly/simc-9999.01.fffffff-win64.7z")
            + Index("//downloads.simulationcraft.org.evil.example/nightly/simc-9999.01.fffffff-win64.7z")
            + Index("../simc-9999.01.fffffff-win64.7z"));
        Assert(parsed.FileName == First && parsed.Version == 1210 && parsed.Revision == "aaaaaaa", "Choose the newest official Windows x64 build.");
        Assert(SimulationCraftUpdates.ParseLatest(Index("simc-1210-01-win64-aaaaaaa.7z")).Revision == "aaaaaaa", "Older official filename format.");
        await Reject(() => Task.FromResult(SimulationCraftUpdates.ParseLatest(Index("simc-1210.01.aaaaaaa-winarm64.7z"))), "No x64 build");

        var engineBytes = File.ReadAllBytes(Environment.ProcessPath!);
        var newerBytes = engineBytes.ToArray(); newerBytes[0x20] ^= 1; // Unused DOS header byte; retain a valid x64 PE.
        var firstArchive = Archive(("nightly/simc.exe", engineBytes), ("nightly/runtime.dll", [1, 2, 3]), ("nightly/COPYING", [4, 5]));
        var secondArchive = Archive(("new-nightly/simc.exe", newerBytes));
        var root = Path.Combine(testRoot, "managed engine with spaces");
        var manual = Path.Combine(testRoot, "manual-simc.exe"); File.WriteAllText(manual, "keep this manual install");
        var latest = First;
        var payload = firstArchive;
        var downloads = 0;
        using var updates = new SimulationCraftUpdates(new FixtureHandler(request => {
            Assert(request.RequestUri!.Host == "downloads.simulationcraft.org", "Never download from a third-party host.");
            Assert(request.Headers.Authorization == null, "SimulationCraft checks must not send credentials.");
            if (request.RequestUri.AbsolutePath == "/nightly/") return new(HttpStatusCode.OK) { Content = new StringContent(Index(latest)) };
            downloads++;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }));
        var messages = new List<EngineUpdateProgress>();
        var installed = await updates.UpdateAsync(root, new InlineProgress(messages.Add));
        Assert(!installed.AlreadyCurrent && File.ReadAllBytes(installed.EnginePath).SequenceEqual(engineBytes), "Install a complete engine automatically.");
        Assert(File.Exists(Path.Combine(Path.GetDirectoryName(installed.EnginePath)!, "runtime.dll")), "Retain package dependencies and license files.");
        Assert(messages.Any(value => value.Percent == 100), "Report download progress.");
        var current = await updates.UpdateAsync(root);
        Assert(current.AlreadyCurrent && downloads == 1 && current.EnginePath == installed.EnginePath, "Repeat clicks must skip the archive download.");
        File.WriteAllText(current.EnginePath, "damaged engine");
        var repaired = await updates.UpdateAsync(root);
        Assert(!repaired.AlreadyCurrent && downloads == 2 && File.ReadAllBytes(repaired.EnginePath).SequenceEqual(engineBytes), "Repair a corrupted managed engine.");
        latest = Second; payload = secondArchive;
        var upgraded = await updates.UpdateAsync(root);
        Assert(!upgraded.AlreadyCurrent && File.ReadAllBytes(upgraded.EnginePath).SequenceEqual(newerBytes), "Select the newly installed executable, including a changed archive subfolder.");
        Assert(File.ReadAllBytes(Path.Combine(root, "previous", "nightly", "simc.exe")).SequenceEqual(engineBytes), "Keep the previous engine after upgrading.");

        latest = "simc-1210.01.ccccccc-win64.7z";
        foreach (var invalid in new[] {
            new byte[] { 0, 1, 2 },
            Archive(("../outside.exe", engineBytes)),
            Archive(("nightly/simc.exe", [0, 1, 2])),
            Archive(("nightly/readme.txt", [1, 2, 3])),
            Archive(("one/simc.exe", engineBytes), ("two/simc.exe", engineBytes)),
            Archive(("nightly/simc.exe", engineBytes), ("nightly/SIMC.EXE", engineBytes)),
            Archive(("nightly/simc.exe:payload", engineBytes)),
            Archive(("nightly/CON.txt", engineBytes)),
        })
        {
            payload = invalid;
            await Reject(() => updates.UpdateAsync(root), "Corrupt, incomplete, duplicate or unsafe archive");
            Assert(File.ReadAllBytes(upgraded.EnginePath).SequenceEqual(newerBytes), "A failed update must leave the working engine intact.");
            Assert(!Directory.GetDirectories(root, "stage-*").Any() && !Directory.GetFiles(root, "download-*").Any(), "Clean failed staging/download files.");
        }
        payload = firstArchive;
        using (var stopping = new CancellationTokenSource())
        {
            await Reject(() => updates.UpdateAsync(root, new InlineProgress(value => {
                if (value.Percent.HasValue) stopping.Cancel();
            }), stopping.Token), "Cancelled download");
            Assert(stopping.IsCancellationRequested && File.ReadAllBytes(upgraded.EnginePath).SequenceEqual(newerBytes), "Cancellation must preserve the selected working engine.");
        }
        using (var installLock = new FileStream(Path.Combine(root, ".update-lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Reject(() => updates.UpdateAsync(root), "Concurrent install");
        using var redirected = new SimulationCraftUpdates(new FixtureHandler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://example.com/engine.7z"); return response;
        }));
        await Reject(() => redirected.LatestAsync(), "Offsite redirect");
        using var truncated = new SimulationCraftUpdates(new FixtureHandler(request => {
            if (request.RequestUri!.AbsolutePath == "/nightly/") return new(HttpStatusCode.OK) { Content = new StringContent(Index(latest)) };
            var content = new ByteArrayContent(firstArchive); content.Headers.ContentLength = firstArchive.Length + 10;
            return new(HttpStatusCode.OK) { Content = content };
        }));
        await Reject(() => truncated.UpdateAsync(root), "Truncated download");
        Assert(File.ReadAllText(manual) == "keep this manual install", "Never overwrite a user-selected external engine.");
        Console.WriteLine("PASS: official Windows nightly selection, 7z installation, cache/repair, progress, previous engine, cancellation, concurrency, redirects, and invalid/truncated archive preservation.");
    }

    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception error) when (error is not OutOfMemoryException) { return; }
        throw new Exception("Expected rejection: " + message);
    }
    private sealed class InlineProgress(Action<EngineUpdateProgress> report) : IProgress<EngineUpdateProgress>
    {
        public void Report(EngineUpdateProgress value) => report(value);
    }
}
