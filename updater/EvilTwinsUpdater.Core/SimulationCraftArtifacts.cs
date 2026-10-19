using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvilTwinsUpdater.Core;

// Metadata and digests come only from GitHub. nightly.link supplies anonymous access to the
// exact artifact ZIP; its contents are untrusted until the installer verifies GitHub's digest.
internal static class SimulationCraftArtifacts
{
    private const string Repository = "/repos/simulationcraft/simc-publish/";
    private const string ArtifactName = "simc-nightly-win64-midnight";
    internal const long MaximumDownload = 350_000_000;
    internal static readonly Uri Index = new("https://api.github.com" + Repository
        + "actions/workflows/nightly.yml/runs?branch=master&status=success&per_page=5");

    internal static async Task<SimulationCraftBuild> LatestAsync(HttpClient client, CancellationToken cancellation)
    {
        using var runs = await JsonAsync(client, Index, cancellation);
        foreach (var run in runs.RootElement.GetProperty("workflow_runs").EnumerateArray().Take(5))
        {
            if (run.GetProperty("status").GetString() != "completed" || run.GetProperty("conclusion").GetString() != "success"
                || run.GetProperty("head_branch").GetString() != "master"
                || run.GetProperty("path").GetString() != ".github/workflows/nightly.yml"
                || run.GetProperty("event").GetString() is not ("schedule" or "workflow_dispatch")
                || run.GetProperty("repository").GetProperty("full_name").GetString() != "simulationcraft/simc-publish"
                || run.GetProperty("head_repository").GetProperty("full_name").GetString() != "simulationcraft/simc-publish") continue;
            var runId = run.GetProperty("id").GetInt64();
            if (runId <= 0) continue;
            var uri = new Uri($"https://api.github.com{Repository}actions/runs/{runId}/artifacts?per_page=100");
            using var artifacts = await JsonAsync(client, uri, cancellation);
            var matching = artifacts.RootElement.GetProperty("artifacts").EnumerateArray()
                .Where(value => value.GetProperty("name").GetString() == ArtifactName && !value.GetProperty("expired").GetBoolean()).ToArray();
            if (matching.Length == 0) continue;
            if (matching.Length != 1) throw new InvalidDataException("GitHub returned ambiguous SimulationCraft artifacts.");
            var artifact = matching[0];
            var id = artifact.GetProperty("id").GetInt64();
            var digest = artifact.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() ?? "" : "";
            var size = artifact.GetProperty("size_in_bytes").GetInt64();
            if (id <= 0 || size <= 0 || size > MaximumDownload || !Regex.IsMatch(digest, @"\Asha256:[0-9a-fA-F]{64}\z")
                || artifact.GetProperty("workflow_run").GetProperty("id").GetInt64() != runId
                || !artifact.TryGetProperty("expires_at", out var expiration)
                || !DateTimeOffset.TryParse(expiration.GetString(), out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidDataException("GitHub did not provide valid verification data for the SimulationCraft build. Your existing engine will be kept.");
            return new($"SimulationCraft nightly #{runId}", 0, 0, "", new Uri($"https://nightly.link/simulationcraft/simc-publish/actions/artifacts/{id}.zip"))
                { ArtifactId = id, ArtifactSha256 = digest[7..].ToUpperInvariant(), ArtifactSize = size };
        }
        throw new InvalidDataException("No verified Windows x64 SimulationCraft nightly is available. Try again later or use Choose engine.");
    }

    private static async Task<JsonDocument> JsonAsync(HttpClient client, Uri uri, CancellationToken cancellation)
    {
        using var response = await GetAsync(client, uri, true, cancellation);
        using var bytes = new MemoryStream();
        await SimulationCraftUpdates.CopyLimitedAsync(response, bytes, 2_000_000, null, cancellation);
        return JsonDocument.Parse(bytes.ToArray());
    }

    internal static async Task<HttpResponseMessage> GetAsync(HttpClient client, Uri uri, bool metadata, CancellationToken cancellation)
    {
        for (var redirects = 0; redirects < 5; redirects++)
        {
            if (!Allowed(uri, metadata)) throw new InvalidDataException("SimulationCraft redirected outside its permitted HTTPS download sources.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (metadata) {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            }
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } location)
            {
                var next = new Uri(uri, location); response.Dispose(); uri = next; continue;
            }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) {
                response.Dispose();
                throw new HttpRequestException("The SimulationCraft download service is temporarily busy or rate limited. Try again later; no login is needed.");
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("The SimulationCraft download redirected too many times.");
    }

    private static bool Allowed(Uri uri, bool metadata)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        if (metadata) return uri.Host == "api.github.com" && uri.AbsolutePath.StartsWith(Repository, StringComparison.Ordinal);
        if (uri.Host == "nightly.link") return Regex.IsMatch(uri.AbsolutePath, @"\A/simulationcraft/simc-publish/actions/artifacts/[1-9][0-9]*\.zip\z") && uri.Query.Length == 0;
        // GitHub uses signed, short-lived Azure/GitHub storage URLs for artifact ZIPs.
        // The separately fetched digest authenticates the payload even if the broker is compromised.
        return uri.Host.EndsWith(".blob.core.windows.net", StringComparison.Ordinal)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }
}
