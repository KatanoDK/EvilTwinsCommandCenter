using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EvilTwinsUpdater.Core;

var testRoot = Path.Combine(Path.GetFullPath(args.Length > 0 ? args[0] : ".test-work"), Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var retail = Path.Combine(testRoot, "_retail_");
Directory.CreateDirectory(retail);
File.WriteAllText(Path.Combine(retail, "Wow.exe"), "Test fixture; not executable");
var wtf = Path.Combine(retail, "WTF");
Directory.CreateDirectory(wtf);
File.WriteAllText(Path.Combine(wtf, "SavedVariables.lua"), "keep me");
var other = Path.Combine(retail, "Interface", "AddOns", "OtherAddon");
Directory.CreateDirectory(other);
File.WriteAllText(Path.Combine(other, "Other.lua"), "keep me too");
var firstCommit = new string('a', 40);
var secondCommit = new string('b', 40);
string Zip(string filename, string commit, string? extra = null, bool dirty = false, bool missingFile = false)
{
    var path = Path.Combine(testRoot, filename);
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    void Add(string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
    const string prefix = "EvilTwinsCommandCenter/";
    Add(prefix + "build.json", JsonSerializer.Serialize(new BuildInfo(AddonInstaller.AddonName, "0.1.0", commit, dirty)));
    Add(prefix + "EvilTwinsCommandCenter.toc", "## Version: 0.1.0\nCore.lua\n" + (missingFile ? "Missing.lua\n" : ""));
    Add(prefix + "Core.lua", "ET.version = \"0.1.0\"\n-- " + commit);
    if (extra != null) Add(extra, "bad file");
    return path;
}
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception error) when (error is InvalidOperationException or InvalidDataException or IOException) { return; }
    throw new Exception("Expected rejection: " + message);
}
try
{
    var first = Zip("first.zip", firstCommit);
    var second = Zip("second.zip", secondCommit);
    Reject(() => AddonInstaller.Install(retail, first, () => true), "WoW running");
    Assert(AddonInstaller.InstalledBuild(retail) == null, "Running game must not install.");
    AddonInstaller.Install(retail, first, () => false, firstCommit);
    AddonInstaller.Install(retail, second, () => false, secondCommit);
    Assert(AddonInstaller.InstalledBuild(retail)?.Commit == secondCommit, "Second build must be installed.");
    Assert(AddonInstaller.HasBackup(retail), "Previous build must be backed up.");
    AddonInstaller.Rollback(retail, () => false);
    Assert(AddonInstaller.InstalledBuild(retail)?.Commit == firstCommit, "Rollback must restore first build.");
    Assert(File.ReadAllText(Path.Combine(wtf, "SavedVariables.lua")) == "keep me", "SavedVariables changed.");
    Assert(File.ReadAllText(Path.Combine(other, "Other.lua")) == "keep me too", "Another addon changed.");
    foreach (var (filename, entry) in new[] {
        ("traversal.zip", "EvilTwinsCommandCenter/../../WTF/attack.lua"),
        ("outside.zip", "OtherAddon/attack.lua"),
        ("executable.zip", "EvilTwinsCommandCenter/attack.exe"),
        ("duplicate.zip", "EvilTwinsCommandCenter/core.lua") })
        Reject(() => AddonInstaller.Install(retail, Zip(filename, secondCommit, entry), () => false), filename);
    Reject(() => AddonInstaller.Install(retail, Zip("dirty.zip", secondCommit, dirty: true), () => false), "Dirty build");
    Reject(() => AddonInstaller.Install(retail, Zip("missing.zip", secondCommit, missingFile: true), () => false), "Missing TOC file");
    Reject(() => AddonInstaller.Install(retail, second, () => false, firstCommit), "Wrong published commit");
    var probes = 0;
    Reject(() => AddonInstaller.Install(retail, second, () => ++probes > 1), "Game starts while staging");
    Assert(AddonInstaller.InstalledBuild(retail)?.Commit == firstCommit, "Rejected update changed installed build.");
    var bytes = File.ReadAllBytes(second);
    var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    var metadata = JsonSerializer.Serialize(new[] { new {
        draft = false, tag_name = "build-" + secondCommit, published_at = "2026-09-26T12:00:00Z",
        assets = new[] { new { name = "EvilTwinsCommandCenter.zip", digest = "sha256:" + digest,
            browser_download_url = "https://github.com/Test/Downloads/releases/download/build-" + secondCommit + "/EvilTwinsCommandCenter.zip" } }
    } });
    using var feed = new GitHubFeed(new FixtureHandler(request => request.RequestUri!.Host == "api.github.com"
        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(metadata) }
        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
    var build = await feed.LatestAsync("Test/Downloads");
    Assert(build.Commit == secondCommit, "Prerelease/build feed selected wrong commit.");
    await feed.DownloadAsync(build, Path.Combine(testRoot, "download.zip"));
    var checksumRejected = false;
    try { await feed.DownloadAsync(build with { Sha256 = new string('0', 64) }, Path.Combine(testRoot, "wrong-hash.zip")); }
    catch (InvalidDataException) { checksumRejected = true; }
    Assert(checksumRejected, "Download checksum mismatch was accepted.");
    Console.WriteLine("PASS: installation, backup, rollback, game gates, path validation, saved data preservation, public feed, and checksums.");
}
finally
{
    // testRoot is an absolute, generated child of the explicitly selected test workspace.
    Directory.Delete(testRoot, true);
}

sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Headers.Authorization != null) throw new Exception("Public downloads must not send credentials.");
        return Task.FromResult(respond(request));
    }
}
