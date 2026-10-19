using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives.SevenZip;

namespace EvilTwinsUpdater.Core;

public sealed record SimulationCraftBuild(string FileName, int Version, int Release, string Revision, Uri Download)
{
    public long ArtifactId { get; init; }
    public string ArtifactSha256 { get; init; } = "";
    public long ArtifactSize { get; init; }
}
public sealed record SimulationCraftInstallation(string EnginePath, SimulationCraftBuild Build, bool AlreadyCurrent);
public sealed record EngineUpdateProgress(string Message, int? Percent = null);

public sealed class SimulationCraftUpdates : IDisposable
{
    public static readonly Uri Index = SimulationCraftArtifacts.Index;
    private const long MaximumExtracted = 2_000_000_000;
    private readonly HttpClient client;
    private sealed record Installed(string FileName, string EngineRelativePath, string EngineSha256, string ArtifactSha256);

    public SimulationCraftUpdates(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        client.Timeout = TimeSpan.FromMinutes(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EvilTwinsUpdater/0.1.0");
    }

    private static SimulationCraftBuild PackageBuild(SimulationCraftBuild build, string name)
    {
        var match = Regex.Match(name, @"\Asimc-(?<version>[0-9]{3,5})(?:\.(?<release>[0-9]{1,3})\.(?<revision>[0-9a-f]{7,40})-win64|-(?<release>[0-9]{1,3})-win64-(?<revision>[0-9a-f]{7,40}))\.7z\z");
        if (!match.Success) throw new InvalidDataException("The verified artifact does not contain a recognized Windows x64 SimulationCraft package.");
        return build with { FileName = name, Version = int.Parse(match.Groups["version"].Value),
            Release = int.Parse(match.Groups["release"].Value), Revision = match.Groups["revision"].Value };
    }

    public Task<SimulationCraftBuild> LatestAsync(CancellationToken cancellation = default) =>
        SimulationCraftArtifacts.LatestAsync(client, cancellation);

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
                // Copies installed by the old HTTP updater have no verified artifact digest and are replaced.
                if (installed?.ArtifactSha256 == build.ArtifactSha256)
                {
                    var engine = OwnedPath(current, installed.EngineRelativePath);
                    if (File.Exists(engine) && await HashAsync(engine, cancellation) == installed.EngineSha256)
                        return new(LocalSimulator.ValidateEngine(engine), PackageBuild(build, installed.FileName), true);
                }
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException) { /* Reinstall an incomplete managed copy. */ }
        }
        var id = Guid.NewGuid().ToString("N");
        var artifact = OwnedPath(root, "download-" + id + ".zip");
        var archive = OwnedPath(root, "download-" + id + ".7z");
        var stage = OwnedPath(root, "stage-" + id);
        try
        {
            using (var response = await SimulationCraftArtifacts.GetAsync(client, build.Download, false, cancellation))
            await using (var output = new FileStream(artifact, FileMode.CreateNew, FileAccess.Write))
                await CopyLimitedAsync(response, output, build.ArtifactSize, progress, cancellation);
            progress?.Report(new("Verifying SimulationCraft against GitHub's SHA-256 checksum…"));
            if (new FileInfo(artifact).Length != build.ArtifactSize || await HashAsync(artifact, cancellation) != build.ArtifactSha256)
                throw new InvalidDataException("SimulationCraft verification failed: the download does not match GitHub's SHA-256 checksum. Nothing was installed; your existing engine was kept.");
            build = await UnwrapArtifactAsync(build, artifact, archive, cancellation);
            progress?.Report(new("Extracting SimulationCraft…"));
            var relative = await Task.Run(() => Extract(archive, stage, cancellation), cancellation);
            var executable = OwnedPath(stage, relative);
            await File.WriteAllTextAsync(Path.Combine(stage, "engine-build.json"),
                JsonSerializer.Serialize(new Installed(build.FileName, relative, await HashAsync(executable, cancellation), build.ArtifactSha256)), cancellation);
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
            if (File.Exists(artifact)) File.Delete(artifact);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }

    private static async Task<SimulationCraftBuild> UnwrapArtifactAsync(SimulationCraftBuild build, string artifact, string archive, CancellationToken cancellation)
    {
        using var zip = ZipFile.OpenRead(artifact);
        // Do not extract ZIP paths. Copy only the single expected package into a generated local filename.
        if (zip.Entries.Count is < 1 or > 16) throw new InvalidDataException("The verified SimulationCraft artifact has unexpected contents.");
        var packages = zip.Entries.Where(entry => entry.FullName.EndsWith(".7z", StringComparison.Ordinal)).ToArray();
        if (packages.Length != 1) throw new InvalidDataException("The verified SimulationCraft artifact must contain one engine package.");
        var package = packages[0];
        if (package.FullName != package.Name || package.Length <= 0 || package.Length > SimulationCraftArtifacts.MaximumDownload
            || zip.Entries.Any(entry => entry != package && (entry.FullName != "simc-version" || entry.Length > 128)))
            throw new InvalidDataException("The verified SimulationCraft artifact has unexpected contents.");
        build = PackageBuild(build, package.Name);
        await using var input = package.Open();
        await using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write);
        var buffer = new byte[131072]; long length = 0; int count;
        while ((count = await input.ReadAsync(buffer, cancellation)) > 0) {
            length += count;
            if (length > package.Length || length > SimulationCraftArtifacts.MaximumDownload)
                throw new InvalidDataException("The SimulationCraft package is too large.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
        }
        if (length != package.Length) throw new InvalidDataException("The SimulationCraft package is incomplete.");
        return build;
    }

    internal static async Task CopyLimitedAsync(HttpResponseMessage response, Stream output, long limit,
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
