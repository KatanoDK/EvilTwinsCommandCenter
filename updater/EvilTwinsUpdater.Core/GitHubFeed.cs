using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvilTwinsUpdater.Core;

public sealed record PublishedBuild(string Commit, string DownloadUrl, string Sha256, DateTimeOffset Published);

public sealed class GitHubFeed : IDisposable
{
    private readonly HttpClient client;
    public GitHubFeed(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(2);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EvilTwinsUpdater/0.1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static string ValidateRepository(string repository)
    {
        repository = repository.Trim();
        if (!Regex.IsMatch(repository, "^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$"))
            throw new InvalidOperationException("Enter a GitHub repository as owner/repository.");
        return repository;
    }

    public async Task<PublishedBuild> LatestAsync(string repository, CancellationToken cancellation = default)
    {
        repository = ValidateRepository(repository);
        using var response = await client.GetAsync($"https://api.github.com/repos/{repository}/releases?per_page=30", cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("The addon downloads are unavailable. Try again later or use Install ZIP.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("GitHub has limited update checks. Try again later.");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        foreach (var release in json.RootElement.EnumerateArray()
                     .Where(r => !r.GetProperty("draft").GetBoolean())
                     .OrderByDescending(r => r.GetProperty("published_at").GetDateTimeOffset()))
        {
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, "^build-[0-9a-f]{40}$")) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != "EvilTwinsCommandCenter.zip") continue;
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com"
                    || !uri.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase)
                    || !Regex.IsMatch(digest, "^sha256:[0-9a-f]{64}$"))
                    throw new InvalidDataException("The published addon is missing a valid download or checksum.");
                return new PublishedBuild(tag[6..], url, digest[7..], release.GetProperty("published_at").GetDateTimeOffset());
            }
        }
        throw new InvalidOperationException("No published addon build is available yet.");
    }

    public async Task DownloadAsync(PublishedBuild build, string destination, CancellationToken cancellation = default)
    {
        using var response = await client.GetAsync(build.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 25_000_000) throw new InvalidDataException("Addon download is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite);
        var buffer = new byte[65536];
        long length = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
        {
            length += count;
            if (length > 25_000_000) throw new InvalidDataException("Addon download is too large.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
        }
        await output.FlushAsync(cancellation);
        output.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(output, cancellation)).ToLowerInvariant();
        if (digest != build.Sha256) throw new InvalidDataException("Downloaded addon checksum does not match. Installation was stopped.");
    }

    public void Dispose() => client.Dispose();
}
