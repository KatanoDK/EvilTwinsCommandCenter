using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EvilTwinsUpdater.Core;

if (args.Length is 2 or 3 && args[0] == "--verify-report")
{
    using var reports = new DroptimizerClient();
    var report = await reports.FetchAsync(args[1]);
    if (args.Length == 3) File.WriteAllText(args[2], report.ImportCode());
    Console.WriteLine($"Verified live Droptimizer: {report.Items.Count} positive upgrades; import size {report.ImportCode().Length} characters.");
    foreach (var item in report.Items) Console.WriteLine($"Drop {item.DropId}, simulated {item.SimulatedId}, ilvl {item.ItemLevel}, +{item.Percent:F4}%.");
    return;
}

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
    await DropTests.Run(testRoot);
    await AppUpdateTests.Run(testRoot);
    var first = Zip("first.zip", firstCommit);
    var second = Zip("second.zip", secondCommit);
    AddonInstaller.Install(retail, first, firstCommit);
    var corePath = Path.Combine(retail, "Interface", "AddOns", AddonInstaller.AddonName, "Core.lua");
    // If another program locks a file, leave the current installation intact for a retry.
    using (var lockedFile = new FileStream(corePath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Reject(() => AddonInstaller.Install(retail, second, secondCommit), "Locked addon file");
        Assert(AddonInstaller.InstalledBuild(retail)?.Commit == firstCommit, "Locked-file failure changed installed build.");
    }
    AddonInstaller.Install(retail, second, secondCommit);
    Assert(File.ReadAllText(corePath).Contains(secondCommit), "Retry must install the new code.");
    Assert(AddonInstaller.InstalledBuild(retail)?.Commit == secondCommit, "Second build must be installed.");
    Assert(AddonInstaller.HasBackup(retail), "Previous build must be backed up.");
    using (var lockedFile = new FileStream(corePath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Reject(() => AddonInstaller.Rollback(retail), "Locked addon file during restore");
        Assert(AddonInstaller.InstalledBuild(retail)?.Commit == secondCommit, "Locked-file restore failure changed installed build.");
        Assert(AddonInstaller.HasBackup(retail), "Locked-file restore failure removed the backup.");
    }
    AddonInstaller.Rollback(retail);
    Assert(AddonInstaller.InstalledBuild(retail)?.Commit == firstCommit, "Rollback must restore first build.");
    Assert(File.ReadAllText(Path.Combine(wtf, "SavedVariables.lua")) == "keep me", "SavedVariables changed.");
    Assert(File.ReadAllText(Path.Combine(other, "Other.lua")) == "keep me too", "Another addon changed.");
    foreach (var (filename, entry) in new[] {
        ("traversal.zip", "EvilTwinsCommandCenter/../../WTF/attack.lua"),
        ("outside.zip", "OtherAddon/attack.lua"),
        ("executable.zip", "EvilTwinsCommandCenter/attack.exe"),
        ("duplicate.zip", "EvilTwinsCommandCenter/core.lua") })
        Reject(() => AddonInstaller.Install(retail, Zip(filename, secondCommit, entry)), filename);
    Reject(() => AddonInstaller.Install(retail, Zip("dirty.zip", secondCommit, dirty: true)), "Dirty build");
    Reject(() => AddonInstaller.Install(retail, Zip("missing.zip", secondCommit, missingFile: true)), "Missing TOC file");
    Reject(() => AddonInstaller.Install(retail, second, firstCommit), "Wrong published commit");
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
    Console.WriteLine("PASS: installation, rollback, locked-file failures and retry, backup, path validation, saved data preservation, public feed, and checksums.");
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
