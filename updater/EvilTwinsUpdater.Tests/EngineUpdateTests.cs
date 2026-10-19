using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using EvilTwinsUpdater.Core;

internal static class EngineUpdateTests
{
    private const string First = "simc-1210.01.aaaaaaa-win64.7z", Second = "simc-1210.01.bbbbbbb-win64.7z";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Archive(params (string Path, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream();
        using (var writer = WriterFactory.OpenWriter(output, ArchiveType.SevenZip,
            new SevenZipWriterOptions(CompressionType.LZMA2) { LeaveStreamOpen = true }))
            foreach (var (path, bytes) in entries) {
                using var input = new MemoryStream(bytes); writer.Write(path, input);
            }
        return output.ToArray();
    }
    private static byte[] Zip(params (string Path, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (path, bytes) in entries) {
                using var entry = zip.CreateEntry(path, System.IO.Compression.CompressionLevel.NoCompression).Open(); entry.Write(bytes);
            }
        return output.ToArray();
    }

    // The broker's payload and GitHub's metadata are independently controlled.
    private sealed class Server
    {
        public byte[] Payload = [];
        public string Digest = "", Event = "schedule", Branch = "master", ArtifactName = "simc-nightly-win64-midnight";
        public long Id = 11, ArtifactRunId = 42;
        public bool Expired, Truncated;
        public Uri? DownloadRedirect, MetadataRedirect;
        public int Downloads, ArtifactQueries, UnsafeRequests;
        public void Publish(string name, byte[] archive) {
            Payload = Zip((name, archive), ("simc-version", "1210.01"u8.ToArray())); Digest = "sha256:" + Hash(Payload);
        }
        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var uri = request.RequestUri!;
            if (uri.Scheme != "https" || request.Headers.Authorization != null
                || (uri.Host != "api.github.com" && uri.Host != "productionresultstest.blob.core.windows.net"
                    && (uri.Host != "nightly.link" || uri.AbsolutePath != $"/simulationcraft/simc-publish/actions/artifacts/{Id}.zip"))) UnsafeRequests++;
            Assert(uri.Scheme == "https" && request.Headers.Authorization == null, "Use HTTPS without credentials.");
            if (uri.Host == "api.github.com") {
                Assert(request.Headers.Accept.Any(value => value.MediaType == "application/vnd.github+json"), "Read GitHub metadata directly.");
                if (MetadataRedirect != null) return Redirect(MetadataRedirect);
                if (uri.AbsolutePath.EndsWith("/runs")) return Json(new { workflow_runs = new[] {
                    new { id = 42L, status = "completed", conclusion = "success", head_branch = Branch,
                        path = ".github/workflows/nightly.yml", @event = Event,
                        repository = new { full_name = "simulationcraft/simc-publish" },
                        head_repository = new { full_name = "simulationcraft/simc-publish" } } } });
                Assert(uri.AbsolutePath == "/repos/simulationcraft/simc-publish/actions/runs/42/artifacts", "Pin the official workflow run.");
                ArtifactQueries++;
                return Json(new { artifacts = new[] { new { id = Id, name = ArtifactName, expired = Expired, digest = Digest,
                    size_in_bytes = (long)Payload.Length, expires_at = DateTimeOffset.UtcNow.AddDays(3), workflow_run = new { id = ArtifactRunId } } } });
            }
            Assert(uri.Host == "nightly.link" || uri.Host == "productionresultstest.blob.core.windows.net", "Only contact approved artifact hosts.");
            if (uri.Host == "nightly.link") {
                Assert(uri.AbsolutePath == $"/simulationcraft/simc-publish/actions/artifacts/{Id}.zip", "Pin anonymous downloads to the verified artifact ID.");
                if (DownloadRedirect != null) return Redirect(DownloadRedirect);
            }
            Downloads++; var content = new ByteArrayContent(Payload);
            if (Truncated) content.Headers.ContentLength = Payload.Length + 10;
            return new(HttpStatusCode.OK) { Content = content };
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        private static HttpResponseMessage Redirect(Uri uri) {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = uri; return response;
        }
    }

    public static async Task Run(string testRoot)
    {
        var engineBytes = File.ReadAllBytes(Environment.ProcessPath!);
        var newerBytes = engineBytes.ToArray(); newerBytes[0x20] ^= 1; // Retain a valid x64 PE.
        var firstArchive = Archive(("nightly/simc.exe", engineBytes), ("nightly/runtime.dll", [1, 2, 3]), ("nightly/COPYING", [4, 5]));
        var secondArchive = Archive(("new-nightly/simc.exe", newerBytes));
        var root = Path.Combine(testRoot, "managed engine with spaces");
        var manual = Path.Combine(testRoot, "manual-simc.exe"); File.WriteAllText(manual, "keep this manual install");
        var server = new Server(); server.Publish(First, firstArchive);
        using var updates = new SimulationCraftUpdates(new FixtureHandler(server.Respond));
        var latest = await updates.LatestAsync();
        Assert(latest.ArtifactId == 11 && latest.ArtifactSha256 == Hash(server.Payload), "Use GitHub's independently obtained digest.");
        var messages = new List<EngineUpdateProgress>();
        var installed = await updates.UpdateAsync(root, new InlineProgress(messages.Add));
        Assert(!installed.AlreadyCurrent && installed.Build.FileName == First && installed.Build.Version == 1210
            && installed.Build.Revision == "aaaaaaa" && File.ReadAllBytes(installed.EnginePath).SequenceEqual(engineBytes), "Install the verified engine and retain its version.");
        Assert(File.Exists(Path.Combine(Path.GetDirectoryName(installed.EnginePath)!, "runtime.dll")), "Retain dependencies and license files.");
        Assert(messages.Any(value => value.Percent == 100) && messages.Any(value => value.Message.StartsWith("Verifying")), "Show download and verification progress.");
        var current = await updates.UpdateAsync(root);
        Assert(current.AlreadyCurrent && server.Downloads == 1 && current.EnginePath == installed.EnginePath, "Reuse an intact verified build.");
        File.WriteAllText(current.EnginePath, "damaged engine");
        var repaired = await updates.UpdateAsync(root);
        Assert(!repaired.AlreadyCurrent && server.Downloads == 2 && File.ReadAllBytes(repaired.EnginePath).SequenceEqual(engineBytes), "Repair a damaged engine.");
        var manifest = Path.Combine(root, "current", "engine-build.json");
        var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(manifest))!;
        metadata.Remove("ArtifactSha256"); File.WriteAllText(manifest, JsonSerializer.Serialize(metadata));
        Assert(!(await updates.UpdateAsync(root)).AlreadyCurrent && server.Downloads == 3, "Reinstall legacy HTTP copies through the verified path.");
        server.Id++; server.Publish(Second, secondArchive);
        server.DownloadRedirect = new Uri("https://productionresultstest.blob.core.windows.net/actions-results/artifact.zip?signature=fixture");
        var upgraded = await updates.UpdateAsync(root); server.DownloadRedirect = null;
        Assert(!upgraded.AlreadyCurrent && File.ReadAllBytes(upgraded.EnginePath).SequenceEqual(newerBytes), "Follow GitHub HTTPS storage redirects.");
        Assert(File.ReadAllBytes(Path.Combine(root, "previous", "nightly", "simc.exe")).SequenceEqual(engineBytes), "Keep the previous engine.");
        void Preserved() {
            Assert(File.ReadAllBytes(upgraded.EnginePath).SequenceEqual(newerBytes), "Failed updates must keep the working engine.");
            Assert(!Directory.GetDirectories(root, "stage-*").Any() && !Directory.GetFiles(root, "download-*").Any(), "Clean failed staging/download files.");
        }
        server.Id++; server.Publish(First, firstArchive);
        var authenticPayload = server.Payload;
        // Serve altered but well-formed bytes while GitHub still reports the authentic digest.
        server.Payload = authenticPayload.ToArray(); server.Payload[10] ^= 1; // ZIP timestamp; same size, different digest.
        var mismatch = await Reject(() => updates.UpdateAsync(root), "Checksum mismatch");
        Assert(mismatch.Message.Contains("SHA-256"), "Explain failed verification."); Preserved(); server.Payload = authenticPayload;
        foreach (var digest in new[] { "", "sha256:abc", "sha1:" + new string('a', 64), "sha256:" + new string('g', 64) }) {
            server.Digest = digest; var before = server.Downloads;
            await Reject(() => updates.UpdateAsync(root), "Missing or invalid trusted digest");
            Assert(server.Downloads == before, "Reject bad metadata before contacting the broker."); Preserved();
        }
        server.Digest = "sha256:" + Hash(server.Payload);
        server.Expired = true; await Reject(() => updates.LatestAsync(), "Expired artifact"); server.Expired = false;
        server.ArtifactRunId = 999; await Reject(() => updates.LatestAsync(), "Wrong run"); server.ArtifactRunId = 42;
        server.ArtifactName = "simc-nightly-winarm64-midnight"; await Reject(() => updates.LatestAsync(), "Wrong architecture"); server.ArtifactName = "simc-nightly-win64-midnight";
        server.Event = "pull_request"; var queries = server.ArtifactQueries;
        await Reject(() => updates.LatestAsync(), "Pull-request workflow"); Assert(server.ArtifactQueries == queries, "Skip untrusted workflow events."); server.Event = "schedule";
        server.Branch = "untrusted"; await Reject(() => updates.LatestAsync(), "Wrong branch"); server.Branch = "master";
        foreach (var location in new[] { "http://nightly.link/simulationcraft/simc-publish/actions/artifacts/13.zip",
            "https://example.com/engine.zip", "https://evil.blob.core.windows.net.attacker.example/engine.zip" }) {
            server.DownloadRedirect = new Uri(location); await Reject(() => updates.UpdateAsync(root), "Insecure or unapproved redirect"); Preserved();
        }
        server.DownloadRedirect = null; server.MetadataRedirect = new Uri("https://nightly.link/metadata.json");
        await Reject(() => updates.LatestAsync(), "The broker cannot supply trusted metadata"); server.MetadataRedirect = null;
        server.Truncated = true; await Reject(() => updates.UpdateAsync(root), "Truncated download"); server.Truncated = false; Preserved();
        foreach (var invalid in new[] {
            new byte[] { 0, 1, 2 }, Archive(("../outside.exe", engineBytes)), Archive(("nightly/simc.exe", [0, 1, 2])),
            Archive(("nightly/readme.txt", [1, 2, 3])), Archive(("one/simc.exe", engineBytes), ("two/simc.exe", engineBytes)),
            Archive(("nightly/simc.exe", engineBytes), ("nightly/SIMC.EXE", engineBytes)),
            Archive(("nightly/simc.exe:payload", engineBytes)), Archive(("nightly/CON.txt", engineBytes)) }) {
            server.Publish(First, invalid); await Reject(() => updates.UpdateAsync(root), "Unsafe inner archive"); Preserved();
        }
        foreach (var zip in new[] {
            Zip(("../" + First, firstArchive)), Zip((First, firstArchive), (Second, secondArchive)),
            Zip(("simc-1210.01.aaaaaaa-winarm64.7z", firstArchive)), Zip((First, firstArchive), ("unexpected.exe", engineBytes)), new byte[] { 0, 1, 2 } }) {
            server.Payload = zip; server.Digest = "sha256:" + Hash(zip);
            await Reject(() => updates.UpdateAsync(root), "Unsafe ZIP wrapper"); Preserved();
        }
        server.Publish("simc-1210-01-win64-aaaaaaa.7z", firstArchive);
        var older = await updates.UpdateAsync(root);
        Assert(older.Build.Revision == "aaaaaaa", "Support older official filenames.");
        server.Id++; server.Publish(Second, secondArchive);
        using (var stopping = new CancellationTokenSource()) {
            await Reject(() => updates.UpdateAsync(root, new InlineProgress(value => { if (value.Percent.HasValue) stopping.Cancel(); }), stopping.Token), "Cancelled download");
            Assert(stopping.IsCancellationRequested && File.ReadAllBytes(older.EnginePath).SequenceEqual(engineBytes), "Cancellation preserves the working engine.");
        }
        using (var installLock = new FileStream(Path.Combine(root, ".update-lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Reject(() => updates.UpdateAsync(root), "Concurrent install");
        Assert(File.ReadAllText(manual) == "keep this manual install", "Leave external manual installs alone.");
        Assert(server.UnsafeRequests == 0, "Block unsafe redirects before issuing any request to their target.");
        Console.WriteLine("PASS: login-free HTTPS artifacts, independent GitHub SHA-256, official workflow selection, tamper rejection, legacy migration, cache/repair, redirects, safe ZIP/7z extraction, cancellation and concurrency.");
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task<Exception> Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception error) when (error is not OutOfMemoryException) { return error; }
        throw new Exception("Expected rejection: " + message);
    }
    private sealed class InlineProgress(Action<EngineUpdateProgress> report) : IProgress<EngineUpdateProgress>
    {
        public void Report(EngineUpdateProgress value) => report(value);
    }
}
