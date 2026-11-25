using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace FoxuMarsDisplay.Core.Themes;

public enum CloudSourceMode
{
    AutoSoeyiWithHfFallback,
    SoeyiOnly,
    HuggingFaceOnly,
    Both
}

public sealed record CloudThemeItem(
    string Id,
    string Name,
    string Resolution,
    int Width,
    int Height,
    ThemeOrientation Orientation,
    string Source,
    string Format,
    string ThumbnailUrl,
    string DownloadUrl,
    string FileName,
    long SizeBytes,
    string LocalFilePath,
    bool IsDownloaded);

public sealed record CloudCatalogResult(
    List<CloudThemeItem> Items,
    string ActiveSourceLabel,
    bool UsedFallback,
    string? ErrorMessage);

/// <summary>
/// Downloads official .photo themes from SOEYI (soeyiphoto.com) with automatic
/// fallback to the HuggingFace mirror (snakefoxu/soeyi-themes).
/// </summary>
public sealed class CloudThemeService
{
    public const string SoeyiBaseUrl = "https://www.soeyiphoto.com";
    public const string HuggingFaceBaseUrl = "https://huggingface.co/datasets/snakefoxu/soeyi-themes/resolve/main";

    private readonly HttpClient _http;
    private readonly string _themesDir;
    private readonly string _cacheDir;
    private readonly string _thumbsDir;

    private static readonly Regex ApiDataRegex = new(
        "name=[\"']api_data[\"']\\s+content=['\"]([^'\"]+)['\"]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CloudThemeService(HttpClient? httpClient = null, string? customThemesDir = null, string? customCacheDir = null)
    {
        _http = httpClient ?? CreateDefaultHttpClient();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _themesDir = customThemesDir ?? Path.Combine(localAppData, "FoxuMarsDisplay", "Themes");
        _cacheDir = customCacheDir ?? Path.Combine(localAppData, "FoxuMarsDisplay", "cache");
        _thumbsDir = Path.Combine(_cacheDir, "cloud_thumbs");
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FoxuMarsDisplay/1.0");
        return client;
    }

    public async Task<CloudCatalogResult> FetchCatalogAsync(CloudSourceMode mode, CancellationToken ct = default)
    {
        if (mode == CloudSourceMode.HuggingFaceOnly)
        {
            try
            {
                var hf = await FetchHuggingFaceCatalogAsync(ct).ConfigureAwait(false);
                return new CloudCatalogResult(hf, "HuggingFace (SnakeFoxu)", false, null);
            }
            catch (Exception ex)
            {
                return new CloudCatalogResult([], "HuggingFace (Error)", false, ex.Message);
            }
        }

        if (mode == CloudSourceMode.SoeyiOnly)
        {
            try
            {
                var soeyi = await FetchSoeyiCatalogAsync(ct).ConfigureAwait(false);
                return new CloudCatalogResult(soeyi, "SOEYI Oficial (soeyiphoto.com)", false, null);
            }
            catch (Exception ex)
            {
                return new CloudCatalogResult([], "SOEYI Oficial (Error)", false, ex.Message);
            }
        }

        if (mode == CloudSourceMode.Both)
        {
            List<CloudThemeItem> soeyi = [];
            List<CloudThemeItem> hf = [];
            string? err = null;

            try { soeyi = await FetchSoeyiCatalogAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { err = ex.Message; }

            try { hf = await FetchHuggingFaceCatalogAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { err = err is null ? ex.Message : $"{err}; {ex.Message}"; }

            var merged = new List<CloudThemeItem>(soeyi.Count + hf.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in soeyi.Concat(hf))
            {
                if (seen.Add($"{item.Name}|{item.Width}x{item.Height}"))
                    merged.Add(item);
            }

            string label = soeyi.Count > 0 && hf.Count > 0
                ? "SOEYI + HuggingFace"
                : soeyi.Count > 0 ? "SOEYI Oficial" : "HuggingFace (Fallback)";
            return new CloudCatalogResult(merged, label, soeyi.Count == 0 && hf.Count > 0, err);
        }

        // Default: AutoSoeyiWithHfFallback
        try
        {
            var soeyi = await FetchSoeyiCatalogAsync(ct).ConfigureAwait(false);
            if (soeyi.Count > 0)
                return new CloudCatalogResult(soeyi, "SOEYI Oficial (soeyiphoto.com)", false, null);
        }
        catch (Exception soeyiEx)
        {
            try
            {
                var hf = await FetchHuggingFaceCatalogAsync(ct).ConfigureAwait(false);
                return new CloudCatalogResult(hf, "HuggingFace (Fallback automático)", true, $"SOEYI no disponible: {soeyiEx.Message}");
            }
            catch (Exception hfEx)
            {
                return new CloudCatalogResult([], "Sin conexión", true, $"SOEYI: {soeyiEx.Message} | HF: {hfEx.Message}");
            }
        }

        try
        {
            var hfFallback = await FetchHuggingFaceCatalogAsync(ct).ConfigureAwait(false);
            return new CloudCatalogResult(hfFallback, "HuggingFace (Fallback automático)", true, "SOEYI devolvió catálogo vacío");
        }
        catch (Exception ex)
        {
            return new CloudCatalogResult([], "Sin conexión", true, ex.Message);
        }
    }

    public async Task<List<CloudThemeItem>> FetchSoeyiCatalogAsync(CancellationToken ct = default)
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string homeHtml = await _http.GetStringAsync($"{SoeyiBaseUrl}/?_={ts}", ct).ConfigureAwait(false);

        var match = ApiDataRegex.Match(homeHtml);
        if (!match.Success)
            throw new InvalidOperationException("No se encontró api_data en soeyiphoto.com");

        string decodedJson = HttpUtility.HtmlDecode(match.Groups[1].Value);
        using var apiDoc = JsonDocument.Parse(decodedJson);
        var root = apiDoc.RootElement;
        string appid = root.GetProperty("appid").GetString() ?? "webapi";
        string timestamp = root.GetProperty("timestamp").GetString() ?? ts.ToString();
        string signature = root.GetProperty("signature").GetString() ?? "";
        string acode = root.TryGetProperty("acode", out var ac) ? (ac.GetString() ?? "cn") : "cn";

        var all = new List<CloudThemeItem>();
        int totalExpected = int.MaxValue;
        const int pageSize = 50;

        for (int page = 1; page <= 20 && all.Count < totalExpected; page++)
        {
            string url = $"{SoeyiBaseUrl}/api.php/list/1?page={page}&num={pageSize}" +
                         $"&appid={Uri.EscapeDataString(appid)}" +
                         $"&timestamp={Uri.EscapeDataString(timestamp)}" +
                         $"&signature={Uri.EscapeDataString(signature)}" +
                         $"&acode={Uri.EscapeDataString(acode)}";

            string pageJson = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            var (items, rowTotal) = ParseSoeyiListResponse(pageJson, _themesDir);
            if (rowTotal > 0) totalExpected = rowTotal;
            if (items.Count == 0) break;

            all.AddRange(items);
            if (items.Count < pageSize) break;
        }

        return all;
    }

    public static (List<CloudThemeItem> Items, int RowTotal) ParseSoeyiListResponse(string json, string themesDir)
    {
        var items = new List<CloudThemeItem>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 1)
            return (items, 0);

        int rowTotal = 0;
        if (root.TryGetProperty("rowtotal", out var rtEl))
        {
            if (rtEl.ValueKind == JsonValueKind.Number) rowTotal = rtEl.GetInt32();
            else if (rtEl.ValueKind == JsonValueKind.String) int.TryParse(rtEl.GetString(), out rowTotal);
        }

        if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
            return (items, rowTotal);

        foreach (var el in dataEl.EnumerateArray())
        {
            string enclosure = el.TryGetProperty("enclosure", out var encEl) ? (encEl.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(enclosure))
                continue;

            string id = el.TryGetProperty("id", out var idEl) ? idEl.ToString() : Guid.NewGuid().ToString("N")[..8];
            string title = el.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? $"Theme_{id}") : $"Theme_{id}";
            string ico = el.TryGetProperty("ico", out var icoEl) ? (icoEl.GetString() ?? "") : "";
            string res = el.TryGetProperty("ext_type", out var extEl) ? (extEl.GetString() ?? "Auto") : "Auto";
            if (string.IsNullOrWhiteSpace(res)) res = "Auto";

            ThemeLibraryScanner.TryParseResolution(res, out int w, out int h);
            var ori = ThemeLibraryScanner.ClassifyOrientation(w, h);

            string safeTitle = SanitizeFileName(title);
            string ext = Path.GetExtension(enclosure);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".photo";
            string fileName = $"{safeTitle}_{id}{ext}";
            string localPath = Path.Combine(themesDir, fileName);

            string downloadUrl = enclosure.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? enclosure
                : $"{SoeyiBaseUrl}{(enclosure.StartsWith('/') ? "" : "/")}{enclosure}";
            string thumbUrl = string.IsNullOrWhiteSpace(ico)
                ? string.Empty
                : (ico.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? ico
                    : $"{SoeyiBaseUrl}{(ico.StartsWith('/') ? "" : "/")}{ico}");

            items.Add(new CloudThemeItem(
                Id: id,
                Name: title,
                Resolution: w > 0 && h > 0 ? $"{w}x{h}" : res,
                Width: w,
                Height: h,
                Orientation: ori,
                Source: "SOEYI",
                Format: ext.TrimStart('.').ToUpperInvariant(),
                ThumbnailUrl: thumbUrl,
                DownloadUrl: downloadUrl,
                FileName: fileName,
                SizeBytes: 0,
                LocalFilePath: localPath,
                IsDownloaded: File.Exists(localPath)));
        }

        return (items, rowTotal);
    }

    public async Task<List<CloudThemeItem>> FetchHuggingFaceCatalogAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_cacheDir);
        string cacheFile = Path.Combine(_cacheDir, "hf_catalog.json");
        string json;

        try
        {
            json = await _http.GetStringAsync($"{HuggingFaceBaseUrl}/catalog.json", ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(cacheFile, json, ct).ConfigureAwait(false);
        }
        catch
        {
            if (File.Exists(cacheFile))
                json = await File.ReadAllTextAsync(cacheFile, ct).ConfigureAwait(false);
            else
                throw;
        }

        return ParseHuggingFaceCatalog(json, _themesDir);
    }

    public static List<CloudThemeItem> ParseHuggingFaceCatalog(string json, string themesDir)
    {
        var items = new List<CloudThemeItem>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var el in arr.EnumerateArray())
        {
            string downloadRel = el.TryGetProperty("download", out var dlEl) ? (dlEl.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(downloadRel)) continue;

            string id = el.TryGetProperty("id", out var idEl) ? idEl.ToString() : Guid.NewGuid().ToString("N")[..8];
            string name = el.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? id) : id;
            string previewRel = el.TryGetProperty("preview", out var pEl) ? (pEl.GetString() ?? "") : "";
            string res = el.TryGetProperty("resolution", out var rEl) ? (rEl.GetString() ?? "Auto") : "Auto";
            string type = el.TryGetProperty("type", out var tyEl) ? (tyEl.GetString() ?? "photo") : "photo";
            long size = el.TryGetProperty("size", out var sEl) && sEl.TryGetInt64(out var sv) ? sv : 0;

            ThemeLibraryScanner.TryParseResolution(res, out int w, out int h);
            var ori = ThemeLibraryScanner.ClassifyOrientation(w, h);

            string rawName = Path.GetFileName(downloadRel);
            if (string.IsNullOrWhiteSpace(rawName))
                rawName = $"{SanitizeFileName(name)}.{type}";
            string localPath = Path.Combine(themesDir, rawName);

            items.Add(new CloudThemeItem(
                Id: id,
                Name: name,
                Resolution: w > 0 && h > 0 ? $"{w}x{h}" : res,
                Width: w,
                Height: h,
                Orientation: ori,
                Source: "HF",
                Format: type.ToUpperInvariant(),
                ThumbnailUrl: string.IsNullOrWhiteSpace(previewRel) ? string.Empty : $"{HuggingFaceBaseUrl}/{previewRel.TrimStart('/')}",
                DownloadUrl: $"{HuggingFaceBaseUrl}/{downloadRel.TrimStart('/')}",
                FileName: rawName,
                SizeBytes: size,
                LocalFilePath: localPath,
                IsDownloaded: File.Exists(localPath)));
        }

        return items;
    }

    public async Task<string> DownloadThemeAsync(CloudThemeItem item, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_themesDir);
        string destPath = item.LocalFilePath;
        string tmpPath = destPath + ".tmp";

        using var resp = await _http.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        long total = resp.Content.Headers.ContentLength ?? item.SizeBytes;
        long received = 0;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buf = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                if (total > 0 && progress != null)
                    progress.Report((int)Math.Clamp((received * 100L) / total, 0, 100));
            }
        }

        File.Move(tmpPath, destPath, overwrite: true);
        progress?.Report(100);
        return destPath;
    }

    public async Task<string?> GetOrDownloadThumbnailAsync(CloudThemeItem item, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.ThumbnailUrl))
            return null;

        Directory.CreateDirectory(_thumbsDir);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.ThumbnailUrl)))[..16];
        string cachePath = Path.Combine(_thumbsDir, $"{hash}.img");
        if (File.Exists(cachePath))
            return cachePath;

        try
        {
            byte[] bytes = await _http.GetByteArrayAsync(item.ThumbnailUrl, ct).ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            string tmp = cachePath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, cachePath, overwrite: true);
            return cachePath;
        }
        catch
        {
            return null;
        }
    }

    public static string SanitizeFileName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Theme" : cleaned;
    }
}
