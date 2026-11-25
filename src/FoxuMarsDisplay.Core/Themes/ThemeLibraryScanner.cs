using System.IO.Compression;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Themes;

public enum ThemeOrientation
{
    Unknown,
    Vertical,
    Horizontal,
    Square
}

public sealed record ThemeLibraryEntry(
    string Name,
    string Format,
    string Resolution,
    string ThemePath,
    string? SchemeJsonPath,
    string? ThumbnailPath,
    int Width = 0,
    int Height = 0,
    ThemeOrientation Orientation = ThemeOrientation.Unknown);

public static class ThemeLibraryScanner
{
    private const string PhotoPassword = "vmax2025";

    public static ThemeOrientation ClassifyOrientation(int width, int height)
    {
        if (width <= 0 || height <= 0) return ThemeOrientation.Unknown;
        int max = Math.Max(width, height);
        if (Math.Abs(width - height) <= max * 0.08)
            return ThemeOrientation.Square;
        return height > width ? ThemeOrientation.Vertical : ThemeOrientation.Horizontal;
    }

    public static bool TryParseResolution(string? res, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(res)) return false;
        var parts = res.Split(['x', 'X', '×', '*'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width > 0 && height > 0;
    }

    private static ThemeLibraryEntry CreateEntry(
        string name,
        string format,
        string res,
        string themePath,
        string? schemeJsonPath,
        string? thumbPath)
    {
        if (!TryParseResolution(res, out int w, out int h) && !string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
        {
            if (TryReadImageDimensions(thumbPath, out w, out h))
                res = $"{w}x{h}";
        }

        var orientation = ClassifyOrientation(w, h);
        return new ThemeLibraryEntry(name, format, res, themePath, schemeJsonPath, thumbPath, w, h, orientation);
    }

    public static List<ThemeLibraryEntry> ScanAll(IEnumerable<string>? extraFolders = null)
    {
        var results = new List<ThemeLibraryEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var soeyiProg = Path.Combine(localAppData, "SOEYI", "Programme");
        var soeyiScheme = Path.Combine(localAppData, "SOEYI", "ThemeScheme");

        // 1. SOEYI installed themes
        if (Directory.Exists(soeyiProg))
        {
            foreach (var dir in Directory.GetDirectories(soeyiProg))
            {
                var name = Path.GetFileName(dir);
                string? scheme = FindMatchingSchemeJson(soeyiScheme, name);
                string res = DetectFolderResolution(dir, scheme);
                string? thumb = FindPreviewInDir(dir);
                if (seen.Add($"SOEYI|{name}"))
                    results.Add(CreateEntry(name, "SOEYI", res, dir, scheme, thumb));
            }
        }

        // 2. FoxuMarsDisplay local Themes folder
        var foxuThemes = Path.Combine(localAppData, "FoxuMarsDisplay", "Themes");
        ScanFolder(foxuThemes, results, seen);

        // 3. User Documents SnakeMarsTheme Downloads (if present)
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var smtRes = Path.Combine(docs, "SnakeMarsTheme", "resources");
        ScanFolder(Path.Combine(smtRes, "ThemesPhoto"), results, seen);
        ScanFolder(Path.Combine(smtRes, "Themes_SMTHEME"), results, seen);
        ScanFolder(Path.Combine(smtRes, "themes"), results, seen);

        if (extraFolders != null)
        {
            foreach (var folder in extraFolders)
                ScanFolder(folder, results, seen);
        }

        return results.OrderBy(r => r.Format).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ScanFolder(string folder, List<ThemeLibraryEntry> results, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        foreach (var photo in Directory.GetFiles(folder, "*.photo"))
        {
            var name = Path.GetFileNameWithoutExtension(photo);
            if (seen.Add($"PHOTO|{name}"))
            {
                var (res, thumb) = InspectPhoto(photo);
                results.Add(CreateEntry(name, ".photo", res, photo, null, thumb));
            }
        }

        foreach (var sm in Directory.GetFiles(folder, "*.smtheme"))
        {
            var name = Path.GetFileNameWithoutExtension(sm);
            var (res, thumb) = InspectSmtheme(sm);
            if (seen.Add($"TURZX|{name}"))
                results.Add(CreateEntry(name, "TURZX", res, sm, null, thumb));
        }

        foreach (var dir in Directory.GetDirectories(folder))
        {
            var name = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, "Setting.txt")) ||
                File.Exists(Path.Combine(dir, "settings.txt")) ||
                File.Exists(Path.Combine(dir, "theme.yaml")) ||
                File.Exists(Path.Combine(dir, "back.png")) ||
                File.Exists(Path.Combine(dir, "background.png")))
            {
                if (seen.Add($"DIR|{name}"))
                    results.Add(CreateEntry(name, "Carpeta", DetectFolderResolution(dir, null), dir, null, FindPreviewInDir(dir)));
            }
        }
    }

    private static (string Resolution, string? ThumbPath) InspectPhoto(string photoPath)
    {
        string res = "Auto";
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDir = Path.Combine(localAppData, "FoxuMarsDisplay", "cache", "previews");
        string safeName = string.Concat(Path.GetFileNameWithoutExtension(photoPath)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string cachedThumb = Path.Combine(cacheDir, $"photo_{safeName}.png");
        string cachedMeta = Path.Combine(cacheDir, $"photo_{safeName}.meta");

        if (File.Exists(cachedMeta))
        {
            try
            {
                res = File.ReadAllText(cachedMeta).Trim();
                return (string.IsNullOrEmpty(res) ? "Auto" : res, File.Exists(cachedThumb) ? cachedThumb : null);
            }
            catch { }
        }

        string? resultThumb = null;
        try
        {
            Directory.CreateDirectory(cacheDir);
            using var archive = ArchiveFactory.OpenArchive(photoPath, new ReaderOptions { Password = PhotoPassword });

            IArchiveEntry? previewEntry = null;
            IArchiveEntry? settingEntry = null;
            foreach (var e in archive.Entries)
            {
                if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
                string fn = Path.GetFileName(e.Key);
                if (fn.Equals("Setting.txt", StringComparison.OrdinalIgnoreCase) ||
                    fn.Equals("settings.txt", StringComparison.OrdinalIgnoreCase))
                {
                    settingEntry ??= e;
                }
                else if (fn.Equals("demo.png", StringComparison.OrdinalIgnoreCase) ||
                         fn.Equals("preview.png", StringComparison.OrdinalIgnoreCase))
                {
                    previewEntry = e;
                }
                else if (previewEntry == null &&
                         (fn.Equals("back.png", StringComparison.OrdinalIgnoreCase) ||
                          fn.Equals("background.png", StringComparison.OrdinalIgnoreCase) ||
                          fn.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                          fn.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
                {
                    previewEntry = e;
                }
            }

            if (settingEntry != null)
            {
                using var sr = new StreamReader(settingEntry.OpenEntryStream());
                string w = "", h = "";
                for (int i = 0; i < 12; i++)
                {
                    var line = sr.ReadLine();
                    if (line == null) break;
                    if (line.StartsWith("width:", StringComparison.OrdinalIgnoreCase)) w = line[6..].Trim();
                    else if (line.StartsWith("height:", StringComparison.OrdinalIgnoreCase)) h = line[7..].Trim();
                }
                if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                    res = $"{w}x{h}";
            }

            if (previewEntry != null)
            {
                using var es = previewEntry.OpenEntryStream();
                using var fs = File.Create(cachedThumb);
                es.CopyTo(fs);
                resultThumb = cachedThumb;

                if (res == "Auto" && TryReadImageDimensions(cachedThumb, out int iw, out int ih))
                    res = $"{iw}x{ih}";
            }

            File.WriteAllText(cachedMeta, res);
        }
        catch { }

        return (res, resultThumb);
    }

    private static (string Resolution, string? ThumbPath) InspectSmtheme(string smthemePath)
    {
        string res = "Auto";
        string? thumbPath = null;
        try
        {
            using var z = ZipFile.OpenRead(smthemePath);
            var manifest = z.GetEntry("manifest.json");
            if (manifest != null)
            {
                using var sr = new StreamReader(manifest.Open());
                using var doc = JsonDocument.Parse(sr.ReadToEnd());
                if (doc.RootElement.TryGetProperty("Resolution", out var rEl) && rEl.ValueKind == JsonValueKind.String)
                    res = rEl.GetString() ?? "Auto";
            }

            var prev = z.GetEntry("preview.png") ?? z.GetEntry("demo.png");
            if (prev != null)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var cacheDir = Path.Combine(localAppData, "FoxuMarsDisplay", "cache", "previews");
                Directory.CreateDirectory(cacheDir);
                thumbPath = Path.Combine(cacheDir, $"{Path.GetFileNameWithoutExtension(smthemePath)}.png");
                if (!File.Exists(thumbPath))
                    prev.ExtractToFile(thumbPath, overwrite: true);
            }
        }
        catch { }
        return (res, thumbPath);
    }

    private static string? FindMatchingSchemeJson(string schemeRoot, string themeName)
    {
        if (!Directory.Exists(schemeRoot)) return null;
        foreach (var sub in Directory.GetDirectories(schemeRoot))
        {
            var cand = Path.Combine(sub, $"{themeName}.json");
            if (File.Exists(cand)) return cand;
        }
        var rootCand = Path.Combine(schemeRoot, $"{themeName}.json");
        return File.Exists(rootCand) ? rootCand : null;
    }

    private static string DetectFolderResolution(string dir, string? schemeJson)
    {
        foreach (var settingName in new[] { "Setting.txt", "settings.txt" })
        {
            var setting = Path.Combine(dir, settingName);
            if (File.Exists(setting))
            {
                string w = "", h = "";
                foreach (var line in File.ReadLines(setting).Take(12))
                {
                    if (line.StartsWith("width:", StringComparison.OrdinalIgnoreCase)) w = line[6..].Trim();
                    else if (line.StartsWith("height:", StringComparison.OrdinalIgnoreCase)) h = line[7..].Trim();
                }
                if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                    return $"{w}x{h}";
            }
        }

        var yaml = Path.Combine(dir, "theme.yaml");
        if (File.Exists(yaml))
        {
            string w = "", h = "";
            foreach (var raw in File.ReadLines(yaml).Take(25))
            {
                var line = raw.Trim();
                if (line.StartsWith("DISPLAY_WIDTH:", StringComparison.OrdinalIgnoreCase)) w = line[14..].Trim();
                else if (line.StartsWith("DISPLAY_HEIGHT:", StringComparison.OrdinalIgnoreCase)) h = line[15..].Trim();
            }
            if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                return $"{w}x{h}";
        }

        if (!string.IsNullOrEmpty(schemeJson) && File.Exists(schemeJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(schemeJson));
                if (doc.RootElement.TryGetProperty("Width", out var wEl) &&
                    doc.RootElement.TryGetProperty("Height", out var hEl))
                    return $"{wEl.GetDouble():0}x{hEl.GetDouble():0}";
            }
            catch { }
        }

        return "Auto";
    }

    private static string? FindPreviewInDir(string dir)
    {
        foreach (var f in new[] { "demo.png", "preview.png", "back.png", "background.png", "thumb.png", "default.gif" })
        {
            var p = Path.Combine(dir, f);
            if (File.Exists(p)) return p;
        }

        try
        {
            foreach (var ext in new[] { "*.png", "*.jpg", "*.jpeg", "*.gif" })
            {
                var first = Directory.EnumerateFiles(dir, ext, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (first != null) return first;
            }
        }
        catch { }

        return null;
    }

    private static bool TryReadImageDimensions(string imagePath, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var codec = SKCodec.Create(imagePath);
            if (codec != null && codec.Info.Width > 0 && codec.Info.Height > 0)
            {
                width = codec.Info.Width;
                height = codec.Info.Height;
                return true;
            }
        }
        catch { }
        return false;
    }
}
