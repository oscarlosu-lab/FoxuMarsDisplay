using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FoxuMarsDisplay.Core.Host;

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string LatestTag,
    string ReleaseUrl,
    string? InstallerDownloadUrl,
    string? ExpectedSha256);

/// <summary>
/// Checks GitHub Releases for newer FoxuMarsDisplay versions and verifies downloaded installer SHA-256 hashes.
/// </summary>
public static class UpdateChecker
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/oscarlosu-lab/FoxuMarsDisplay/releases/latest";
    private static readonly Regex Sha256Regex = new(@"\b[0-9a-fA-F]{64}\b", RegexOptions.Compiled);

    public static async Task<UpdateCheckResult?> CheckLatestAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FoxuMarsDisplay-Updater/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await http.GetAsync(LatestReleaseApiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseReleaseJson(json, currentVersion);
        }
        catch
        {
            return null;
        }
    }

    public static UpdateCheckResult ParseReleaseJson(string json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var tEl) ? (tEl.GetString() ?? "") : "";
        string htmlUrl = root.TryGetProperty("html_url", out var uEl) ? (uEl.GetString() ?? "") : "";
        string body = root.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";

        string? installerUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    installerUrl = asset.TryGetProperty("browser_download_url", out var dEl) ? dEl.GetString() : null;
                    break;
                }
            }
        }

        string? sha = null;
        var m = Sha256Regex.Match(body);
        if (m.Success)
            sha = m.Value.ToUpperInvariant();

        bool newer = IsNewerVersion(currentVersion, tag);
        return new UpdateCheckResult(newer, tag, htmlUrl, installerUrl, sha);
    }

    public static bool IsNewerVersion(string currentVersion, string latestTag)
    {
        static Version? ParseSemver(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string clean = raw.Trim().TrimStart('v', 'V');
            int dash = clean.IndexOf('-');
            if (dash > 0) clean = clean[..dash];
            return Version.TryParse(clean, out var v) ? v : null;
        }

        var cur = ParseSemver(currentVersion);
        var lat = ParseSemver(latestTag);
        return cur != null && lat != null && lat > cur;
    }

    public static bool VerifyFileSha256(string filePath, string expectedSha256)
    {
        if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(expectedSha256))
            return false;

        using var fs = File.OpenRead(filePath);
        string actual = Convert.ToHexString(SHA256.HashData(fs));
        return string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
