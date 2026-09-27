using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack;
using Velopack.Locators;

internal static class AppUpdateTests
{
    public static async Task Run(string testRoot)
    {
        // This locator prepares updates in the fake installation; it never replaces the running app.
        var source = Path.Combine(testRoot, "app-feed");
        var installation = Path.Combine(testRoot, "app-installation");
        Directory.CreateDirectory(source); Directory.CreateDirectory(installation);
        const string packageName = "EvilTwinsUpdater-0.1.0-dev.12-full.nupkg";
        var package = Path.Combine(source, packageName);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("EvilTwinsUpdater.nuspec").Open());
            writer.Write("<package><metadata><id>EvilTwinsUpdater</id><version>0.1.0-dev.12</version><authors>Evil Twins</authors><description>Test package</description></metadata></package>");
        }
        var bytes = File.ReadAllBytes(package);
        var feed = new { Assets = new[] { new {
            PackageId = "EvilTwinsUpdater", Version = "0.1.0-dev.12", Type = "Full", FileName = packageName,
            SHA1 = Convert.ToHexString(SHA1.HashData(bytes)), SHA256 = Convert.ToHexString(SHA256.HashData(bytes)), Size = bytes.Length
        } } };
        File.WriteAllText(Path.Combine(source, "releases.win.json"), JsonSerializer.Serialize(feed));
        var manager = new UpdateManager(source, null, new TestVelopackLocator("EvilTwinsUpdater", "0.1.0-dev.2", installation));
        Assert(manager.IsInstalled, "Test installation was not recognized.");
        var update = await manager.CheckForUpdatesAsync();
        Assert(update != null && update.TargetFullRelease.Version.ToString() == "0.1.0-dev.12", "Development build ordering is wrong.");
        await manager.DownloadUpdatesAsync(update!);
        Assert(manager.UpdatePendingRestart?.Version.ToString() == "0.1.0-dev.12", "Verified package was not staged for restart.");
        var newer = new UpdateManager(source, null, new TestVelopackLocator("EvilTwinsUpdater", "0.1.0-dev.13", Path.Combine(testRoot, "newer-app")));
        Assert(await newer.CheckForUpdatesAsync() == null, "The feed must not downgrade a newer installed app.");
        var equal = new UpdateManager(source, null, new TestVelopackLocator("EvilTwinsUpdater", "0.1.0-dev.12", Path.Combine(testRoot, "equal-app")));
        Assert(await equal.CheckForUpdatesAsync() == null, "The same app build must not update repeatedly.");
        // Corruption must be rejected before the app can be replaced.
        File.AppendAllText(package, "corrupted");
        var broken = new UpdateManager(source, null, new TestVelopackLocator("EvilTwinsUpdater", "0.1.0-dev.2", Path.Combine(testRoot, "broken-app")));
        var rejected = false;
        try { await broken.DownloadUpdatesAsync((await broken.CheckForUpdatesAsync())!); }
        catch { rejected = true; }
        Assert(rejected && broken.UpdatePendingRestart == null, "Corrupted app package was staged.");
        Console.WriteLine("PASS: installed app feed, numeric development version ordering, verified staging, no repeat/downgrade, and corrupt package rejection.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
