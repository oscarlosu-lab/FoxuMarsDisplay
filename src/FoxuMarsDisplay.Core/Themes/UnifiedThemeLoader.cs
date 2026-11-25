using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Themes;

/// <summary>
/// Unified loader for SOEYI folders (Setting.txt + Type 0/1 JSON), .photo (ZIP/7z),
/// TURZX .smtheme (ZIP + manifest.json / theme.yaml), PNG sequences, and raw GIF/PNG/JPG files.
/// </summary>
public static class UnifiedThemeLoader
{
    public const string PhotoPassword = "vmax2025";

    public static DisplayTheme Load(string path, string? schemeJsonPath = null)
    {
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".smtheme")
                return LoadSmthemeArchive(path);
            if (ext == ".photo")
                return LoadPhotoArchive(path);
            if (ext == ".txt")
                return LoadDirectory(Path.GetDirectoryName(path)!, schemeJsonPath);
            if (ext == ".json")
                return LoadFromSchemeJson(path);
            if (ext is ".gif" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
                return LoadSingleImageOrGif(path);
        }

        if (Directory.Exists(path))
            return LoadDirectory(path, schemeJsonPath);

        throw new FileNotFoundException($"Theme path not found: {path}");
    }

    private static DisplayTheme LoadSingleImageOrGif(string filePath)
    {
        int w = 360, h = 960;
        try
        {
            using var codec = SKCodec.Create(filePath);
            if (codec != null && codec.Info.Width > 0 && codec.Info.Height > 0)
            {
                w = codec.Info.Width;
                h = codec.Info.Height;
            }
        }
        catch { }

        var theme = new DisplayTheme
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            SourceDirectory = Path.GetDirectoryName(filePath) ?? string.Empty,
            CanvasWidth = w,
            CanvasHeight = h
        };
        theme.ImageLayers.Add(new ImageLayerSpec
        {
            FilePath = filePath,
            X = 0,
            Y = 0,
            ZIndex = -10,
            Width = w,
            Height = h,
            IsBackground = true
        });
        return theme;
    }

    private static DisplayTheme LoadFromSchemeJson(string jsonPath)
    {
        var themeName = Path.GetFileNameWithoutExtension(jsonPath);
        var dir = Path.GetDirectoryName(jsonPath)!;
        var cur = new DirectoryInfo(dir);
        for (int i = 0; i < 3 && cur != null; i++)
        {
            var prog = Path.Combine(cur.FullName, "Programme", themeName);
            if (Directory.Exists(prog))
                return LoadDirectory(prog, jsonPath);
            cur = cur.Parent;
        }
        return LoadDirectory(dir, jsonPath);
    }

    public static void ExtractPhotoToDirectory(string photoPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string fullDest = Path.GetFullPath(destDir);

        using var archive = ArchiveFactory.OpenArchive(photoPath, new ReaderOptions { Password = PhotoPassword });
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
                continue;

            var normalizedKey = entry.Key.Replace('/', Path.DirectorySeparatorChar);
            var outPath = Path.GetFullPath(Path.Combine(fullDest, normalizedKey));
            if (!outPath.StartsWith(fullDest, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            entry.WriteToFile(outPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        }
    }

    public static DisplayTheme LoadSmthemeArchive(string smthemePath)
    {
        string extractDir = GetCacheDirectory("smtheme", smthemePath);
        if (!Directory.Exists(extractDir) || Directory.GetFiles(extractDir).Length == 0)
        {
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(smthemePath, extractDir, overwriteFiles: true);
        }
        return LoadDirectory(extractDir);
    }

    public static DisplayTheme LoadPhotoArchive(string photoPath)
    {
        string extractDir = GetCacheDirectory("photo", photoPath);
        if (!Directory.Exists(extractDir) || Directory.GetFileSystemEntries(extractDir).Length == 0)
        {
            ExtractPhotoToDirectory(photoPath, extractDir);
        }

        if (FindSettingFile(extractDir) == null)
        {
            var nestedSetting = Directory.GetFiles(extractDir, "Setting.txt", SearchOption.AllDirectories).FirstOrDefault();
            if (nestedSetting != null)
                extractDir = Path.GetDirectoryName(nestedSetting)!;
            else
            {
                var nestedBg = Directory.GetFiles(extractDir, "*.png", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(extractDir, "*.gif", SearchOption.AllDirectories))
                    .FirstOrDefault();
                if (nestedBg != null)
                    extractDir = Path.GetDirectoryName(nestedBg)!;
            }
        }

        return LoadDirectory(extractDir);
    }

    public static DisplayTheme LoadDirectory(string dir, string? schemeJsonPath = null)
    {
        float canvasW = 360f, canvasH = 960f;
        int direction = 0;
        string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var imageLayers = new List<ImageLayerSpec>();
        var textWidgets = new List<TextWidgetSpec>();
        var barWidgets = new List<BarWidgetSpec>();

        string? jsonPath = schemeJsonPath;
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            jsonPath = Directory.GetFiles(dir, "*.json").FirstOrDefault(f => !f.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));

        JsonDocument? schemeDoc = null;
        if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
        {
            try
            {
                schemeDoc = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
                var root = schemeDoc.RootElement;
                if (root.TryGetProperty("Name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                    name = nEl.GetString() ?? name;
                if (root.TryGetProperty("Width", out var wEl) && wEl.TryGetSingle(out var jw) && jw > 0)
                    canvasW = jw;
                if (root.TryGetProperty("Height", out var hEl) && hEl.TryGetSingle(out var jh) && jh > 0)
                    canvasH = jh;
                if (root.TryGetProperty("DisplayDirection", out var dEl) && dEl.TryGetInt32(out var jd))
                    direction = jd;
            }
            catch
            {
                schemeDoc?.Dispose();
                schemeDoc = null;
            }
        }

        string manifestPath = Path.Combine(dir, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
                var root = doc.RootElement;
                if (root.TryGetProperty("Name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                    name = nEl.GetString() ?? name;
                if (root.TryGetProperty("Resolution", out var resEl) && resEl.ValueKind == JsonValueKind.String)
                {
                    var parts = (resEl.GetString() ?? "").Split('x', 'X');
                    if (parts.Length == 2 &&
                        float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var mw) &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mh))
                    {
                        canvasW = mw;
                        canvasH = mh;
                    }
                }
            }
            catch { }
        }

        string? settingPath = FindSettingFile(dir);
        if (settingPath != null)
        {
            ParseSettingTxt(settingPath, dir, ref name, ref canvasW, ref canvasH, imageLayers, textWidgets, barWidgets);
        }
        else
        {
            string yamlPath = Path.Combine(dir, "theme.yaml");
            if (File.Exists(yamlPath))
                ParseThemeYaml(yamlPath, dir, ref canvasW, ref canvasH, ref direction, imageLayers, textWidgets);
        }

        if (schemeDoc != null)
        {
            using (schemeDoc)
            {
                ParseSchemeJsonElements(schemeDoc.RootElement, dir, canvasW, canvasH, imageLayers, textWidgets);
            }
        }

        bool hasBackground = imageLayers.Any(l => l.IsBackground ||
            Path.GetFileName(l.FilePath).Equals("back.png", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(l.FilePath).Equals("background.png", StringComparison.OrdinalIgnoreCase));

        if (!hasBackground)
        {
            string? bgFile = new[] { "background.png", "back.png", "default.gif" }
                .Select(f => Path.Combine(dir, f))
                .FirstOrDefault(File.Exists);

            if (bgFile != null)
            {
                imageLayers.Add(new ImageLayerSpec
                {
                    FilePath = bgFile,
                    X = 0,
                    Y = 0,
                    ZIndex = -10,
                    Width = canvasW,
                    Height = canvasH,
                    IsBackground = true
                });
            }
        }

        var theme = new DisplayTheme
        {
            Name = name,
            SourceDirectory = dir,
            CanvasWidth = canvasW,
            CanvasHeight = canvasH,
            DisplayDirection = direction
        };

        theme.ImageLayers.AddRange(imageLayers.OrderBy(l => l.ZIndex));
        theme.TextWidgets.AddRange(textWidgets.OrderBy(w => w.ZIndex));
        theme.BarWidgets.AddRange(barWidgets.OrderBy(b => b.ZIndex));

        var fontDir = Path.Combine(dir, "font");
        if (Directory.Exists(fontDir))
        {
            theme.FontFiles.AddRange(Directory.GetFiles(fontDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)));
        }
        theme.FontFiles.AddRange(Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)));

        var framesDir = Path.Combine(dir, "frames");
        if (Directory.Exists(framesDir))
        {
            theme.FrameSequenceFiles.AddRange(
                Directory.GetFiles(framesDir, "*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        else if (imageLayers.Count == 0)
        {
            var numbered = Directory.GetFiles(dir, "*.png")
                .Where(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out _))
                .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f)))
                .ToList();
            if (numbered.Count > 1)
                theme.FrameSequenceFiles.AddRange(numbered);
        }

        return theme;
    }

    private static void ParseSchemeJsonElements(
        JsonElement root,
        string dir,
        float canvasW,
        float canvasH,
        List<ImageLayerSpec> imageLayers,
        List<TextWidgetSpec> textWidgets)
    {
        if (imageLayers.Count == 0)
        {
            foreach (var propName in new[] { "BackgroundVideoFile", "BackgroundImage" })
            {
                if (root.TryGetProperty(propName, out var bgEl) && bgEl.ValueKind == JsonValueKind.String)
                {
                    var bgStr = bgEl.GetString();
                    if (!string.IsNullOrWhiteSpace(bgStr))
                    {
                        var resolved = ResolveAssetPath(dir, Path.GetFileName(bgStr));
                        if (resolved != null)
                        {
                            imageLayers.Add(new ImageLayerSpec
                            {
                                FilePath = resolved,
                                X = 0,
                                Y = 0,
                                ZIndex = -10,
                                Width = canvasW,
                                Height = canvasH,
                                IsBackground = true
                            });
                            break;
                        }
                    }
                }
            }
        }

        if (textWidgets.Count == 0 &&
            root.TryGetProperty("DisplayTexts", out var textsEl) &&
            textsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in textsEl.EnumerateArray())
            {
                float x = item.TryGetProperty("Left", out var lEl) && lEl.TryGetSingle(out var lv) ? lv : 0f;
                float y = item.TryGetProperty("Top", out var tEl) && tEl.TryGetSingle(out var tv) ? tv : 0f;
                int z = item.TryGetProperty("ZIndex", out var zEl) && zEl.TryGetInt32(out var zv) ? zv : 1;
                float fs = item.TryGetProperty("FontSize", out var fsEl) && fsEl.TryGetSingle(out var fsv) ? fsv : 24f;
                string ff = item.TryGetProperty("FontFamily", out var ffEl) ? (ffEl.GetString() ?? "").TrimStart('#') : "";
                string colorHex = item.TryGetProperty("Color", out var cEl) ? (cEl.GetString() ?? "#FFFFFF")
                    : item.TryGetProperty("Foreground", out var fgEl) ? (fgEl.GetString() ?? "#FFFFFF") : "#FFFFFF";
                string textType = item.TryGetProperty("TextType", out var ttEl) ? (ttEl.GetString() ?? "")
                    : item.TryGetProperty("Data", out var dEl) ? (dEl.GetString() ?? "") : "";
                string unit = item.TryGetProperty("Unit", out var uEl) ? (uEl.GetString() ?? "") : "";
                string? content = item.TryGetProperty("Content", out var ctEl) && ctEl.ValueKind == JsonValueKind.String
                    ? ctEl.GetString() : null;

                if (string.IsNullOrWhiteSpace(textType) && string.IsNullOrWhiteSpace(content))
                    continue;

                textWidgets.Add(new TextWidgetSpec
                {
                    X = x,
                    Y = y,
                    ZIndex = z,
                    FontSize = fs,
                    FontFamily = ff,
                    Color = ParseColor(colorHex, SKColors.White),
                    SensorKey = textType,
                    Unit = unit,
                    IsStaticText = string.IsNullOrWhiteSpace(textType) && !string.IsNullOrWhiteSpace(content),
                    StaticContent = content
                });
            }
        }
    }

    private static string? FindSettingFile(string dir)
    {
        var s1 = Path.Combine(dir, "Setting.txt");
        if (File.Exists(s1)) return s1;
        var s2 = Path.Combine(dir, "settings.txt");
        if (File.Exists(s2)) return s2;
        return null;
    }

    private static void ParseSettingTxt(
        string settingPath,
        string themeDir,
        ref string name,
        ref float canvasW,
        ref float canvasH,
        List<ImageLayerSpec> images,
        List<TextWidgetSpec> texts,
        List<BarWidgetSpec> bars)
    {
        foreach (var rawLine in File.ReadAllLines(settingPath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                continue;

            int colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            string key = line[..colonIdx].Trim();
            string val = line[(colonIdx + 1)..].Trim();

            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(val)) name = val;
                continue;
            }
            if (key.Equals("width", StringComparison.OrdinalIgnoreCase))
            {
                if (TryFloat(val, out var w) && w > 0) canvasW = w;
                continue;
            }
            if (key.Equals("height", StringComparison.OrdinalIgnoreCase))
            {
                if (TryFloat(val, out var h) && h > 0) canvasH = h;
                continue;
            }

            var p = ParseAtParams(val);

            if (key.Equals("Text", StringComparison.OrdinalIgnoreCase))
            {
                texts.Add(new TextWidgetSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z"),
                    FontSize = GetFloat(p, "FontSize", 24f),
                    FontFamily = GetStr(p, "FontFamily").TrimStart('#'),
                    Color = ParseColor(GetStr(p, "Foreground", "#FFFFFF"), SKColors.White),
                    Opacity = Math.Clamp(GetFloat(p, "Opacity", 1f), 0f, 1f),
                    SensorKey = GetStr(p, "data"),
                    Unit = GetStr(p, "unit"),
                    IsStaticText = GetBool(p, "IsDefaultText"),
                    StaticContent = p.TryGetValue("content", out var c) ? c : null
                });
            }
            else if (key.Equals("BorderLine", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("LineProgress", StringComparison.OrdinalIgnoreCase))
            {
                bars.Add(new BarWidgetSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z"),
                    MaxWidth = GetFloat(p, "maxwidth", 200f),
                    MaxHeight = GetFloat(p, "maxheight", 20f),
                    MaxNum = GetFloat(p, "MaxNum", 100f),
                    CornerRadius = GetFloat(p, "CornerRadius", 0f),
                    FillColor = ParseColor(GetStr(p, "Fill", "#00FFFF"), SKColors.Cyan),
                    BackColor = p.ContainsKey("BackColor") ? ParseColor(GetStr(p, "BackColor"), SKColor.Empty) : SKColor.Empty,
                    SensorKey = GetStr(p, "data")
                });
            }
            else if (key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                string? resolved = ResolveAssetPath(themeDir, key);
                if (resolved != null)
                {
                    images.Add(new ImageLayerSpec
                    {
                        FilePath = resolved,
                        X = GetFloat(p, "x"),
                        Y = GetFloat(p, "y"),
                        ZIndex = GetInt(p, "z"),
                        Width = p.TryGetValue("width", out var ws) && TryFloat(ws, out var wv) ? wv : null,
                        Height = p.TryGetValue("height", out var hs) && TryFloat(hs, out var hv) ? hv : null,
                        IsBackground = GetBool(p, "IsBack")
                    });
                }
            }
        }
    }

    private static void ParseThemeYaml(
        string yamlPath,
        string dir,
        ref float canvasW,
        ref float canvasH,
        ref int direction,
        List<ImageLayerSpec> images,
        List<TextWidgetSpec> texts)
    {
        string? currentSection = null;
        string? currentSensor = null;
        float curX = 0, curY = 0, curFontSize = 24f;
        SKColor curColor = SKColors.White;

        void FlushYamlText()
        {
            if (!string.IsNullOrEmpty(currentSensor))
            {
                texts.Add(new TextWidgetSpec
                {
                    X = curX,
                    Y = curY,
                    ZIndex = 2,
                    FontSize = curFontSize,
                    Color = curColor,
                    SensorKey = currentSensor
                });
                currentSensor = null;
            }
        }

        foreach (var raw in File.ReadAllLines(yamlPath, Encoding.UTF8))
        {
            var trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            int indent = raw.Length - raw.TrimStart().Length;
            int colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;

            string k = trimmed[..colon].Trim();
            string v = trimmed[(colon + 1)..].Trim().Trim('"', '\'');

            if (indent == 0)
            {
                FlushYamlText();
                currentSection = k.ToUpperInvariant();
                continue;
            }

            if (currentSection == "DISPLAY")
            {
                if (k.Equals("DISPLAY_ORIENTATION", StringComparison.OrdinalIgnoreCase) &&
                    v.Contains("landscape", StringComparison.OrdinalIgnoreCase))
                    direction = 1;
            }
            else if (currentSection == "STATIC_IMAGES" && k.Equals("PATH", StringComparison.OrdinalIgnoreCase))
            {
                var imgPath = ResolveAssetPath(dir, v);
                if (imgPath != null)
                {
                    images.Add(new ImageLayerSpec
                    {
                        FilePath = imgPath,
                        X = 0,
                        Y = 0,
                        ZIndex = -5,
                        Width = canvasW,
                        Height = canvasH,
                        IsBackground = true
                    });
                }
            }
            else if (currentSection == "STATS")
            {
                if (indent <= 4 && string.IsNullOrEmpty(v))
                {
                    FlushYamlText();
                    currentSensor = MapYamlSectionToSensor(k);
                    curX = 0; curY = 0; curFontSize = 24f; curColor = SKColors.White;
                }
                else if (currentSensor != null)
                {
                    if (k.Equals("X", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out var xv)) curX = xv;
                    else if (k.Equals("Y", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out var yv)) curY = yv;
                    else if (k.Equals("FONT_SIZE", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out var fs)) curFontSize = fs;
                    else if (k.Equals("FONT_COLOR", StringComparison.OrdinalIgnoreCase)) curColor = ParseColor(v, SKColors.White);
                }
            }
        }
        FlushYamlText();
    }

    private static string MapYamlSectionToSensor(string section) => section.ToUpperInvariant() switch
    {
        "CPU" or "CPU_PERCENTAGE" => "CpuUsage",
        "CPU_TEMPERATURE" or "CPU_TEMP" => "CPUT",
        "GPU" or "GPU_PERCENTAGE" => "GpuUsage",
        "GPU_TEMPERATURE" or "GPU_TEMP" => "GPUT",
        "MEMORY" or "RAM" => "MemoryUsage",
        "DATE" => "CurrentDate",
        "TIME" => "CurrentTime",
        _ => section
    };

    private static string? ResolveAssetPath(string themeDir, string fileName)
    {
        var p1 = Path.Combine(themeDir, fileName);
        if (File.Exists(p1)) return p1;

        var p2 = Path.Combine(themeDir, "source", fileName);
        if (File.Exists(p2)) return p2;

        return Directory.GetFiles(themeDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
    }

    private static Dictionary<string, string> ParseAtParams(string spec)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = token.IndexOf('@');
            if (at <= 0) continue;
            dict[token[..at].Trim()] = token[(at + 1)..].Trim();
        }
        return dict;
    }

    private static string GetStr(Dictionary<string, string> p, string k, string def = "") =>
        p.TryGetValue(k, out var v) ? v : def;

    private static float GetFloat(Dictionary<string, string> p, string k, float def = 0f) =>
        p.TryGetValue(k, out var v) && TryFloat(v, out var f) ? f : def;

    private static int GetInt(Dictionary<string, string> p, string k, int def = 0) =>
        p.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : def;

    private static bool GetBool(Dictionary<string, string> p, string k) =>
        p.TryGetValue(k, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool TryFloat(string s, out float f) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

    public static SKColor ParseColor(string hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        hex = hex.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        return SKColor.TryParse(hex, out var color) ? color : fallback;
    }

    private static string GetCacheDirectory(string kind, string filePath)
    {
        var info = new FileInfo(filePath);
        var key = $"{filePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "FoxuMarsDisplay", "cache", kind, hash);
    }

    public static DisplayTheme CreateBuiltInDefaultTheme(int width, int height)
    {
        float w = Math.Max(160, width);
        float h = Math.Max(160, height);
        float scale = Math.Min(w / 240f, h / 320f);
        scale = Math.Clamp(scale, 0.85f, 1.8f);
        float pad = 14f * scale;
        float barW = w - (pad * 2f);
        float barH = Math.Max(6f, 8f * scale);

        var theme = new DisplayTheme
        {
            Name = "FoxuCyberHUD",
            CanvasWidth = w,
            CanvasHeight = h,
            DisplayDirection = 0
        };

        // Header
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = pad, Y = pad, ZIndex = 1, FontSize = 14f * scale,
            Color = SKColor.Parse("#FF6419"), IsStaticText = true, StaticContent = "FOXU // MARS"
        });
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = pad, Y = pad + 22f * scale, ZIndex = 1, FontSize = 24f * scale,
            Color = SKColors.White, SensorKey = "Time"
        });

        // CPU
        float cpuY = h * 0.28f;
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = pad, Y = cpuY, ZIndex = 1, FontSize = 12f * scale,
            Color = SKColor.Parse("#FF1F4B"), IsStaticText = true, StaticContent = "CPU"
        });
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = pad, Y = cpuY + 16f * scale, ZIndex = 1, FontSize = 26f * scale,
            Color = SKColors.White, SensorKey = "CpuTemp", Unit = "°C"
        });
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = w * 0.56f, Y = cpuY + 20f * scale, ZIndex = 1, FontSize = 18f * scale,
            Color = SKColor.Parse("#988FAD"), SensorKey = "CpuUsage", Unit = "%"
        });
        theme.BarWidgets.Add(new BarWidgetSpec
        {
            X = pad, Y = cpuY + 50f * scale, ZIndex = 1, MaxWidth = barW, MaxHeight = barH,
            MaxNum = 100f, CornerRadius = 3f, FillColor = SKColor.Parse("#FF1F4B"),
            BackColor = SKColor.Parse("#261F33"), SensorKey = "CpuUsage"
        });

        // GPU
        float gpuY = h * 0.53f;
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = pad, Y = gpuY, ZIndex = 1, FontSize = 12f * scale,
            Color = SKColor.Parse("#FFB820"), IsStaticText = true, StaticContent = "GPU"
        });
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = pad, Y = gpuY + 16f * scale, ZIndex = 1, FontSize = 26f * scale,
            Color = SKColors.White, SensorKey = "GpuTemp", Unit = "°C"
        });
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = w * 0.56f, Y = gpuY + 20f * scale, ZIndex = 1, FontSize = 18f * scale,
            Color = SKColor.Parse("#988FAD"), SensorKey = "GpuUsage", Unit = "%"
        });
        theme.BarWidgets.Add(new BarWidgetSpec
        {
            X = pad, Y = gpuY + 50f * scale, ZIndex = 1, MaxWidth = barW, MaxHeight = barH,
            MaxNum = 100f, CornerRadius = 3f, FillColor = SKColor.Parse("#FFB820"),
            BackColor = SKColor.Parse("#261F33"), SensorKey = "GpuUsage"
        });

        // RAM
        float ramY = h * 0.78f;
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = pad, Y = ramY, ZIndex = 1, FontSize = 12f * scale,
            Color = SKColor.Parse("#B57BFF"), IsStaticText = true, StaticContent = "RAM"
        });
        theme.TextWidgets.Add(new TextWidgetSpec
        {
            X = w * 0.56f, Y = ramY - 2f * scale, ZIndex = 1, FontSize = 16f * scale,
            Color = SKColors.White, SensorKey = "MemoryUsage", Unit = "%"
        });
        theme.BarWidgets.Add(new BarWidgetSpec
        {
            X = pad, Y = ramY + 20f * scale, ZIndex = 1, MaxWidth = barW, MaxHeight = barH,
            MaxNum = 100f, CornerRadius = 3f, FillColor = SKColor.Parse("#B57BFF"),
            BackColor = SKColor.Parse("#261F33"), SensorKey = "MemoryUsage"
        });

        return theme;
    }
}
