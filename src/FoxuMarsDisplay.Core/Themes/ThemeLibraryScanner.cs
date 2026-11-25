using System.IO.Compression;
using System.Text.Json;

namespace FoxuMarsDisplay.Core.Themes;

public sealed record ThemeLibraryEntry(
    string Name,
    string Format,
    string Resolution,
    string ThemePath,
    string? SchemeJsonPath,
    string? ThumbnailPath);

public static class ThemeLibraryScanner
{
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
                    results.Add(new ThemeLibraryEntry(name, "SOEYI", res, dir, scheme, thumb));
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
                results.Add(new ThemeLibraryEntry(name, ".photo", "Auto", photo, null, null));
        }

        foreach (var sm in Directory.GetFiles(folder, "*.smtheme"))
        {
            var name = Path.GetFileNameWithoutExtension(sm);
            var (res, thumb) = InspectSmtheme(sm);
            if (seen.Add($"TURZX|{name}"))
                results.Add(new ThemeLibraryEntry(name, "TURZX", res, sm, null, thumb));
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
                    results.Add(new ThemeLibraryEntry(name, "Carpeta", DetectFolderResolution(dir, null), dir, null, FindPreviewInDir(dir)));
            }
        }
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

            var prev = z.GetEntry("preview.png");
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
        var setting = Path.Combine(dir, "Setting.txt");
        if (File.Exists(setting))
        {
            string w = "", h = "";
            foreach (var line in File.ReadLines(setting).Take(10))
            {
                if (line.StartsWith("width:", StringComparison.OrdinalIgnoreCase)) w = line[6..].Trim();
                else if (line.StartsWith("height:", StringComparison.OrdinalIgnoreCase)) h = line[7..].Trim();
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
        foreach (var f in new[] { "demo.png", "preview.png", "back.png", "background.png" })
        {
            var p = Path.Combine(dir, f);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
