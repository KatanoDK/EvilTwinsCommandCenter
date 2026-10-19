using System.Net;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives.SevenZip;

namespace EvilTwinsUpdater.Core;

public sealed record SimulationCraftBuild(string FileName, int Version, int Release, string Revision, Uri Download);
public sealed record SimulationCraftInstallation(string EnginePath, SimulationCraftBuild Build, bool AlreadyCurrent);
public sealed record EngineUpdateProgress(string Message, int? Percent = null);

public sealed class SimulationCraftUpdates : IDisposable
{
    // This is the nightly index linked by simulationcraft.org/download.html.
    public static readonly Uri Index = new("http://downloads.simulationcraft.org/nightly/?C=M;O=D");
    private const long MaximumDownload = 350_000_000, MaximumExtracted = 2_000_000_000;
    private readonly HttpClient client;
    private sealed record Installed(string FileName, string EngineRelativePath, string EngineSha256);

    public SimulationCraftUpdates(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromMinutes(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EvilTwinsUpdater/0.1.0");
    }

    public static SimulationCraftBuild ParseLatest(string html)
    {
        var builds = new List<SimulationCraftBuild>();
        foreach (Match anchor in Regex.Matches(html, "href\\s*=\\s*[\"'](?<href>[^\"']+)[\"']", RegexOptions.IgnoreCase))
        {
            if (!Uri.TryCreate(Index, WebUtility.HtmlDecode(anchor.Groups["href"].Value), out var uri)
                || !Official(uri) || uri.Query.Length > 0 || uri.Fragment.Length > 0) continue;
            var file = Path.GetFileName(uri.AbsolutePath);
            var match = Regex.Match(file, @"^simc-(?<version>[0-9]{3,5})(?:\.(?<release>[0-9]{1,3})\.(?<revision>[0-9a-f]{7,40})-win64|-(?<release>[0-9]{1,3})-win64-(?<revision>[0-9a-f]{7,40}))\.7z$");
            if (!match.Success || uri.AbsolutePath != "/nightly/" + file) continue;
            builds.Add(new(file, int.Parse(match.Groups["version"].Value), int.Parse(match.Groups["release"].Value),
                match.Groups["revision"].Value, uri));
        }
        // The official listing is sorted newest first within each game version.
        return builds.OrderByDescending(build => build.Version).ThenByDescending(build => build.Release).FirstOrDefault()
            ?? throw new InvalidDataException("The official nightly listing has no Windows 64-bit SimulationCraft build. Try again later or use Choose engine.");
    }

    private static bool Official(Uri uri) => uri.IsAbsoluteUri && uri.Host == Index.Host
        && uri.Scheme is "http" or "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.AbsolutePath.StartsWith("/nightly/", StringComparison.Ordinal);

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellation)
    {
        for (var redirects = 0; redirects < 5; redirects++)
        {
            if (!Official(uri)) throw new InvalidDataException("SimulationCraft redirected outside its official nightly downloads.");
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } location)
            {
                var next = new Uri(uri, location);
                response.Dispose();
                if (uri.Scheme == "https" && next.Scheme != "https") throw new InvalidDataException("SimulationCraft redirected to an insecure download.");
                uri = next;
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("The SimulationCraft download redirected too many times.");
    }

    public async Task<SimulationCraftBuild> LatestAsync(CancellationToken cancellation = default)
    {
        using var response = await GetAsync(Index, cancellation);
        using var html = new MemoryStream();
        await CopyLimitedAsync(response, html, 2_000_000, null, cancellation);
        return ParseLatest(System.Text.Encoding.UTF8.GetString(html.ToArray()));
    }

    public async Task<SimulationCraftInstallation> UpdateAsync(string root,
        IProgress<EngineUpdateProgress>? progress = null, CancellationToken cancellation = default)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        RejectLink(root);
        // A process-wide file lock prevents concurrent app instances from replacing the engine.
        using var installLock = new FileStream(Path.Combine(root, ".update-lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        progress?.Report(new("Checking the latest official Windows SimulationCraft nightly…"));
        var build = await LatestAsync(cancellation);
        var current = OwnedPath(root, "current");
        var previous = OwnedPath(root, "previous");
        RejectLink(current); RejectLink(previous);
        if (Directory.Exists(current))
        {
            try
            {
                var installed = JsonSerializer.Deserialize<Installed>(await File.ReadAllTextAsync(Path.Combine(current, "engine-build.json"), cancellation));
                if (installed?.FileName == build.FileName)
                {
                    var engine = OwnedPath(current, installed.EngineRelativePath);
                    if (File.Exists(engine) && await HashAsync(engine, cancellation) == installed.EngineSha256)
                        return new(LocalSimulator.ValidateEngine(engine), build, true);
                }
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException) { /* Reinstall an incomplete managed copy. */ }
        }
        var id = Guid.NewGuid().ToString("N");
        var archive = OwnedPath(root, "download-" + id + ".7z");
        var stage = OwnedPath(root, "stage-" + id);
        try
        {
            using (var response = await GetAsync(build.Download, cancellation))
            await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write))
                await CopyLimitedAsync(response, output, MaximumDownload, progress, cancellation);
            progress?.Report(new("Extracting SimulationCraft…"));
            var relative = await Task.Run(() => Extract(archive, stage, cancellation), cancellation);
            var executable = OwnedPath(stage, relative);
            await File.WriteAllTextAsync(Path.Combine(stage, "engine-build.json"),
                JsonSerializer.Serialize(new Installed(build.FileName, relative, await HashAsync(executable, cancellation))), cancellation);
            cancellation.ThrowIfCancellationRequested();
            // Only complete installations replace current. A failed move restores the old engine.
            if (Directory.Exists(previous)) Directory.Delete(previous, true);
            var moved = false;
            if (Directory.Exists(current)) { Directory.Move(current, previous); moved = true; }
            try { Directory.Move(stage, current); }
            catch
            {
                if (moved) Directory.Move(previous, current);
                throw;
            }
            return new(LocalSimulator.ValidateEngine(OwnedPath(current, relative)), build, false);
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }

    private static async Task CopyLimitedAsync(HttpResponseMessage response, Stream output, long limit,
        IProgress<EngineUpdateProgress>? progress, CancellationToken cancellation)
    {
        var expected = response.Content.Headers.ContentLength;
        if (expected > limit) throw new InvalidDataException("The SimulationCraft download is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        var buffer = new byte[131072];
        long length = 0;
        var percent = -1;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
        {
            length += count;
            if (length > limit) throw new InvalidDataException("The SimulationCraft download is too large.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
            var next = expected > 0 ? (int)Math.Min(100, length * 100 / expected.Value) : (int)(length / 1_048_576);
            if (next == percent) continue;
            percent = next;
            progress?.Report(new($"Downloading SimulationCraft: {length / 1_048_576:N0} MB"
                + (expected > 0 ? $" / {expected / 1_048_576:N0} MB" : ""), expected > 0 ? next : null));
        }
        if (length == 0 || (expected.HasValue && length != expected.Value))
            throw new InvalidDataException("The SimulationCraft download was incomplete. Try again.");
    }

    private static string Extract(string archivePath, string stage, CancellationToken cancellation)
    {
        Directory.CreateDirectory(stage);
        using var archive = SevenZipArchive.OpenArchive(archivePath);
        using var reader = archive.ExtractAllEntries();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var engines = new List<string>();
        long total = 0;
        var count = 0;
        var buffer = new byte[131072];
        while (reader.MoveToNextEntry())
        {
            cancellation.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (++count > 20_000 || entry.IsEncrypted || entry.LinkTarget != null)
                throw new InvalidDataException("The SimulationCraft archive contains unsupported entries.");
            var key = (entry.Key ?? "").Replace('\\', '/').TrimEnd('/');
            var pieces = key.Split('/');
            if (pieces.Any(part => part is "" or "." or ".." || part.Any(char.IsControl)
                || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)
                || part.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0
                || part.EndsWith(' ') || part.EndsWith('.')) || !names.Add(key))
                throw new InvalidDataException("The SimulationCraft archive contains an invalid or duplicate path.");
            var path = OwnedPath(stage, key);
            if (entry.IsDirectory) { Directory.CreateDirectory(path); continue; }
            if (entry.Size < 0 || entry.Size > MaximumExtracted - total)
                throw new InvalidDataException("The extracted SimulationCraft archive is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = reader.OpenEntryStream();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            long written = 0;
            int size;
            while ((size = input.Read(buffer)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                total += size; written += size;
                if (total > MaximumExtracted) throw new InvalidDataException("The extracted SimulationCraft archive is too large.");
                output.Write(buffer, 0, size);
            }
            if (written != entry.Size) throw new InvalidDataException("The SimulationCraft archive is incomplete.");
            if (Path.GetFileName(path).Equals("simc.exe", StringComparison.OrdinalIgnoreCase)) engines.Add(key);
        }
        if (engines.Count != 1) throw new InvalidDataException("The SimulationCraft archive must contain one simc.exe.");
        using var file = File.OpenRead(OwnedPath(stage, engines[0]));
        using var pe = new PEReader(file);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || pe.PEHeaders.PEHeader == null
            || (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0)
            throw new InvalidDataException("The download did not contain a Windows 64-bit SimulationCraft engine.");
        return engines[0];
    }

    private static string OwnedPath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The SimulationCraft path is outside its installation folder.");
        return path;
    }
    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The SimulationCraft installation folder must not be a link.");
    }
    private static async Task<string> HashAsync(string path, CancellationToken cancellation)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation));
    }
    public void Dispose() => client.Dispose();
}
