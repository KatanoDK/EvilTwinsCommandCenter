using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EvilTwinsUpdater.Core;

public sealed record BuildInfo(
    [property: JsonPropertyName("addon")] string Addon,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("commit")] string Commit,
    [property: JsonPropertyName("dirty")] bool Dirty);

public static class AddonInstaller
{
    public const string AddonName = "EvilTwinsCommandCenter";
    private const string BackupName = ".EvilTwinsCommandCenter-backup";

    public static string ValidateRetail(string path)
    {
        var root = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(root) || !Path.GetFileName(root).Equals("_retail_", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(root, "Wow.exe")))
            throw new InvalidOperationException("Choose the _retail_ folder containing Wow.exe.");
        EnsureNoLinks(root);
        return root;
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Linked folders are not supported. Choose the physical WoW folder.");
    }

    private static string AddonsPath(string retail)
    {
        var path = Path.Combine(ValidateRetail(retail), "Interface", "AddOns");
        EnsureNoLinks(path);
        return path;
    }

    private static void CheckTree(string path)
    {
        EnsureNoLinks(path);
        if (!Directory.Exists(path)) return;
        // Check each directory before walking into it.
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("The addon contains a linked file or folder; update was stopped.");
            if (attributes.HasFlag(FileAttributes.Directory)) CheckTree(entry);
        }
    }

    public static BuildInfo? InstalledBuild(string retail)
    {
        var path = Path.Combine(AddonsPath(retail), AddonName, "build.json");
        if (!File.Exists(path)) return null;
        CheckTree(Path.GetDirectoryName(path)!);
        return JsonSerializer.Deserialize<BuildInfo>(File.ReadAllText(path));
    }

    public static bool HasBackup(string retail) => Directory.Exists(Path.Combine(AddonsPath(retail), BackupName));

    private static BuildInfo ExtractValidated(string zipFile, string stage, string? expectedCommit)
    {
        using var archive = ZipFile.OpenRead(zipFile);
        if (archive.Entries.Count is < 2 or > 256) throw new InvalidDataException("Unexpected addon archive size.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.Contains('\\') || name.Contains(':') || name.StartsWith('/')
                || name.Split('/').Any(part => part is "." or "..")
                || !name.StartsWith(AddonName + "/", StringComparison.Ordinal)
                || !names.Add(name) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("Unsafe addon archive path.");
            if (name.EndsWith('/')) continue;
            var relative = name[(AddonName.Length + 1)..];
            if (relative.Length == 0 || relative.Split('/').Any(part => part.Length == 0 || part.EndsWith('.') || part.EndsWith(' '))
                || !(relative.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                    || relative == AddonName + ".toc" || relative == "build.json"))
                throw new InvalidDataException("Archive contains an unexpected file.");
            total += entry.Length;
            if (entry.Length > 5_000_000 || total > 20_000_000) throw new InvalidDataException("Addon archive is too large.");
            var destination = Path.GetFullPath(Path.Combine(stage, relative));
            if (!destination.StartsWith(Path.GetFullPath(stage) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive file is outside the addon folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination);
        }
        var buildPath = Path.Combine(stage, "build.json");
        var tocPath = Path.Combine(stage, AddonName + ".toc");
        if (!File.Exists(buildPath) || !File.Exists(tocPath) || !File.Exists(Path.Combine(stage, "Core.lua")))
            throw new InvalidDataException("Archive is missing its manifest, TOC, or Core.lua.");
        var build = JsonSerializer.Deserialize<BuildInfo>(File.ReadAllText(buildPath))
            ?? throw new InvalidDataException("Missing build information.");
        if (build.Addon != AddonName || build.Dirty || !Regex.IsMatch(build.Commit ?? "", "^[0-9a-f]{40}$")
            || (expectedCommit != null && build.Commit != expectedCommit))
            throw new InvalidDataException("Build does not match the published commit, or contains uncommitted changes.");
        var toc = File.ReadAllLines(tocPath);
        var version = toc.FirstOrDefault(line => line.StartsWith("## Version:"))?[11..].Trim();
        if (string.IsNullOrEmpty(build.Version) || version != build.Version
            || !File.ReadAllText(Path.Combine(stage, "Core.lua")).Contains($"ET.version = \"{build.Version}\""))
            throw new InvalidDataException("Addon version information does not match.");
        foreach (var line in toc.Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')))
        {
            if (line.Contains('\\') || line.Contains(':') || line.StartsWith('/') || line.Split('/').Any(part => part is "." or "..")
                || !File.Exists(Path.Combine(stage, line))) throw new InvalidDataException("TOC references an invalid or missing file.");
        }
        return build;
    }

    public static BuildInfo Install(string retail, string zipFile, string? expectedCommit = null)
    {
        var addons = AddonsPath(retail);
        Directory.CreateDirectory(addons);
        var target = Path.Combine(addons, AddonName);
        var backup = Path.Combine(addons, BackupName);
        var stage = Path.Combine(addons, ".EvilTwinsCommandCenter-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var build = ExtractValidated(zipFile, stage, expectedCommit);
            CheckTree(target);
            CheckTree(backup);
            if (Directory.Exists(target))
            {
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.Move(target, backup);
            }
            try { Directory.Move(stage, target); }
            catch
            {
                if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            return build;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    public static void Rollback(string retail)
    {
        var addons = AddonsPath(retail);
        var target = Path.Combine(addons, AddonName);
        var backup = Path.Combine(addons, BackupName);
        if (!Directory.Exists(backup)) throw new InvalidOperationException("There is no previous installation to restore.");
        CheckTree(target);
        CheckTree(backup);
        var displaced = Path.Combine(addons, ".EvilTwinsCommandCenter-rollback-" + Guid.NewGuid().ToString("N"));
        var moved = Directory.Exists(target);
        if (moved) Directory.Move(target, displaced);
        try { Directory.Move(backup, target); }
        catch { if (moved) Directory.Move(displaced, target); throw; }
        if (moved) Directory.Move(displaced, backup);
    }
}
