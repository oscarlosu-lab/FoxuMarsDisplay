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
        var radialWidgets = new List<RadialWidgetSpec>();
        var graphWidgets = new List<GraphWidgetSpec>();

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
            ParseSettingTxt(settingPath, dir, ref name, ref canvasW, ref canvasH, ref direction, imageLayers, textWidgets, barWidgets);
        }
        else
        {
            string yamlPath = Path.Combine(dir, "theme.yaml");
            if (File.Exists(yamlPath))
                ParseThemeYaml(yamlPath, dir, ref canvasW, ref canvasH, ref direction, imageLayers, textWidgets, barWidgets, radialWidgets, graphWidgets);
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
        theme.RadialWidgets.AddRange(radialWidgets.OrderBy(r => r.ZIndex));
        theme.GraphWidgets.AddRange(graphWidgets.OrderBy(g => g.ZIndex));

        foreach (var fFolder in new[] { "font", "fonts" })
        {
            var fontDir = Path.Combine(dir, fFolder);
            if (!Directory.Exists(fontDir)) continue;
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
                float opacity = item.TryGetProperty("Opacity", out var opEl) && opEl.TryGetSingle(out var opv) ? Math.Clamp(opv, 0f, 1f) : 1f;
                var align = item.TryGetProperty("Align", out var alEl) && alEl.ValueKind == JsonValueKind.String
                    ? ParseAlign(alEl.GetString())
                    : TextAlignMode.Left;

                if (string.IsNullOrWhiteSpace(textType) && string.IsNullOrWhiteSpace(content))
                    continue;

                bool isStatic = string.IsNullOrWhiteSpace(textType) || textType.Equals("Static", StringComparison.OrdinalIgnoreCase);
                textWidgets.Add(new TextWidgetSpec
                {
                    X = x,
                    Y = y,
                    ZIndex = z,
                    FontSize = fs,
                    FontFamily = ff,
                    Color = ParseColor(colorHex, SKColors.White),
                    Opacity = opacity,
                    SensorKey = textType,
                    Unit = unit,
                    IsStaticText = isStatic && !string.IsNullOrWhiteSpace(content),
                    StaticContent = content,
                    Align = align
                });
            }
        }

        if (root.TryGetProperty("DisplayImages", out var imgsEl) &&
            imgsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in imgsEl.EnumerateArray())
            {
                string? rawImg = item.TryGetProperty("ImagePath", out var p1) && p1.ValueKind == JsonValueKind.String ? p1.GetString()
                    : item.TryGetProperty("Path", out var p2) && p2.ValueKind == JsonValueKind.String ? p2.GetString()
                    : item.TryGetProperty("FileName", out var p3) && p3.ValueKind == JsonValueKind.String ? p3.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(rawImg)) continue;

                string? resolved = ResolveAssetPath(dir, Path.GetFileName(rawImg));
                if (resolved == null) continue;

                float x = item.TryGetProperty("Left", out var lEl) && lEl.TryGetSingle(out var lv) ? lv : 0f;
                float y = item.TryGetProperty("Top", out var tEl) && tEl.TryGetSingle(out var tv) ? tv : 0f;
                int z = item.TryGetProperty("ZIndex", out var zEl) && zEl.TryGetInt32(out var zv) ? zv : 0;
                float? w = item.TryGetProperty("Width", out var wEl) && wEl.TryGetSingle(out var wv) && wv > 0 ? wv : null;
                float? h = item.TryGetProperty("Height", out var hEl) && hEl.TryGetSingle(out var hv) && hv > 0 ? hv : null;

                imageLayers.Add(new ImageLayerSpec
                {
                    FilePath = resolved,
                    X = x,
                    Y = y,
                    ZIndex = z,
                    Width = w,
                    Height = h
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
        ref int direction,
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
            if (key.Equals("DisplayDirection", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
                    direction = d & 3;
                continue;
            }

            var p = ParseAtParams(val);

            if (key.Equals("Text", StringComparison.OrdinalIgnoreCase))
            {
                string dataKey = GetStr(p, "data");
                string? content = p.TryGetValue("content", out var c) ? c : (p.TryGetValue("Text", out var t) ? t : null);
                bool isStatic = GetBool(p, "IsDefaultText") ||
                                dataKey.Equals("Static", StringComparison.OrdinalIgnoreCase) ||
                                (string.IsNullOrWhiteSpace(dataKey) && !string.IsNullOrWhiteSpace(content));
                texts.Add(new TextWidgetSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z"),
                    FontSize = GetFloat(p, "FontSize", 24f),
                    FontFamily = GetStr(p, "FontFamily").TrimStart('#'),
                    Color = ParseColor(GetStr(p, "Foreground", "#FFFFFF"), SKColors.White),
                    Opacity = Math.Clamp(GetFloat(p, "Opacity", 1f), 0f, 1f),
                    SensorKey = dataKey,
                    Unit = GetStr(p, "unit"),
                    IsStaticText = isStatic,
                    StaticContent = content,
                    Align = ParseAlign(GetStr(p, "Align"))
                });
            }
            else if (key.Equals("BorderLine", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("LineProgress", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("DefaultLine", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("GridLine", StringComparison.OrdinalIgnoreCase))
            {
                bars.Add(new BarWidgetSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z"),
                    MaxWidth = GetFloat(p, "maxwidth", 200f),
                    MaxHeight = GetFloat(p, "maxheight", 20f),
                    MinNum = GetFloat(p, "MinNum", 0f),
                    MaxNum = GetFloat(p, "MaxNum", 100f),
                    CornerRadius = GetFloat(p, "CornerRadius", 0f),
                    FillColor = ParseColor(GetStr(p, "Fill", "#00FFFF"), SKColors.Cyan),
                    BackColor = p.ContainsKey("BackColor") ? ParseColor(GetStr(p, "BackColor"), SKColor.Empty) : SKColor.Empty,
                    SensorKey = GetStr(p, "data"),
                    IsSegmented = key.Equals("GridLine", StringComparison.OrdinalIgnoreCase)
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
        List<TextWidgetSpec> texts,
        List<BarWidgetSpec> bars,
        List<RadialWidgetSpec> radials,
        List<GraphWidgetSpec> graphs)
    {
        var stack = new List<(int Indent, string Key)>();
        string? activeBlockPath = null;
        int activeBlockIndent = -1;
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void FlushBlock(ref float cw, ref float ch)
        {
            if (activeBlockPath is null || props.Count == 0)
            {
                activeBlockPath = null;
                props.Clear();
                return;
            }

            if (props.TryGetValue("SHOW", out var showVal) && showVal.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                activeBlockPath = null;
                props.Clear();
                return;
            }

            string pathUpper = activeBlockPath.ToUpperInvariant();
            if (pathUpper.StartsWith("STATIC_IMAGES", StringComparison.Ordinal) &&
                props.TryGetValue("PATH", out var imgRel))
            {
                var imgPath = ResolveAssetPath(dir, imgRel);
                if (imgPath != null)
                {
                    bool isBg = pathUpper.Contains("BACK") || !props.ContainsKey("X");
                    images.Add(new ImageLayerSpec
                    {
                        FilePath = imgPath,
                        X = GetFloat(props, "X"),
                        Y = GetFloat(props, "Y"),
                        ZIndex = isBg ? -5 : GetInt(props, "Z", 0),
                        Width = props.TryGetValue("WIDTH", out var ws) && TryFloat(ws, out var wv) ? wv : (isBg ? cw : null),
                        Height = props.TryGetValue("HEIGHT", out var hs) && TryFloat(hs, out var hv) ? hv : (isBg ? ch : null),
                        IsBackground = isBg
                    });
                }
            }
            else if (pathUpper.StartsWith("STATIC_TEXT", StringComparison.Ordinal) &&
                     props.TryGetValue("TEXT", out var staticText))
            {
                texts.Add(new TextWidgetSpec
                {
                    X = GetFloat(props, "X"),
                    Y = GetFloat(props, "Y"),
                    ZIndex = GetInt(props, "Z", 2),
                    FontSize = GetFloat(props, "FONT_SIZE", 24f),
                    FontFamily = GetStr(props, "FONT"),
                    Color = ParseColor(GetStr(props, "FONT_COLOR", "#FFFFFF"), SKColors.White),
                    Opacity = Math.Clamp(GetFloat(props, "ALPHA", 255f) > 1f ? GetFloat(props, "ALPHA", 255f) / 255f : GetFloat(props, "ALPHA", 1f), 0f, 1f),
                    IsStaticText = true,
                    StaticContent = staticText,
                    Align = ParseAlign(GetStr(props, "ALIGN")),
                    IsBold = GetBool(props, "BOLD")
                });
            }
            else if (pathUpper.StartsWith("STATS.", StringComparison.Ordinal))
            {
                var parts = pathUpper.Split('.');
                string widgetType = parts[^1];
                string sensor = MapYamlPathToSensor(parts);

                if (widgetType == "TEXT")
                {
                    texts.Add(new TextWidgetSpec
                    {
                        X = GetFloat(props, "X"),
                        Y = GetFloat(props, "Y"),
                        ZIndex = GetInt(props, "Z", 2),
                        FontSize = GetFloat(props, "FONT_SIZE", 24f),
                        FontFamily = GetStr(props, "FONT"),
                        Color = ParseColor(GetStr(props, "FONT_COLOR", "#FFFFFF"), SKColors.White),
                        Opacity = Math.Clamp(GetFloat(props, "ALPHA", 255f) > 1f ? GetFloat(props, "ALPHA", 255f) / 255f : GetFloat(props, "ALPHA", 1f), 0f, 1f),
                        SensorKey = sensor,
                        Unit = props.TryGetValue("UNIT", out var u) ? u : DefaultYamlUnit(sensor),
                        Align = ParseAlign(GetStr(props, "ALIGN")),
                        IsBold = GetBool(props, "BOLD")
                    });
                }
                else if (widgetType == "BAR" || widgetType == "GRAPH")
                {
                    bars.Add(new BarWidgetSpec
                    {
                        X = GetFloat(props, "X"),
                        Y = GetFloat(props, "Y"),
                        ZIndex = GetInt(props, "Z", 1),
                        MaxWidth = GetFloat(props, "WIDTH", 180f),
                        MaxHeight = GetFloat(props, "HEIGHT", 16f),
                        MinNum = GetFloat(props, "MIN_VALUE", 0f),
                        MaxNum = GetFloat(props, "MAX_VALUE", 100f),
                        CornerRadius = GetFloat(props, "RADIUS", 3f),
                        FillColor = ParseColor(GetStr(props, "BAR_COLOR", GetStr(props, "COLOR", "#FF6419")), SKColors.OrangeRed),
                        BackColor = props.ContainsKey("BACKGROUND_COLOR")
                            ? ParseColor(GetStr(props, "BACKGROUND_COLOR"), new SKColor(38, 31, 51, 200))
                            : new SKColor(38, 31, 51, 200),
                        SensorKey = sensor
                    });
                }
                else if (widgetType == "RADIAL")
                {
                    radials.Add(new RadialWidgetSpec
                    {
                        Cx = GetFloat(props, "CX", GetFloat(props, "X")),
                        Cy = GetFloat(props, "CY", GetFloat(props, "Y")),
                        Radius = GetFloat(props, "RADIUS", 48f),
                        Thickness = GetFloat(props, "BAR_WIDTH", GetFloat(props, "THICKNESS", 10f)),
                        StartAngle = GetFloat(props, "ANGLE_START", -225f),
                        EndAngle = GetFloat(props, "ANGLE_END", 45f),
                        MinValue = GetFloat(props, "MIN_VALUE", 0f),
                        MaxValue = GetFloat(props, "MAX_VALUE", 100f),
                        FillColor = ParseColor(GetStr(props, "BAR_COLOR", GetStr(props, "COLOR", "#FF6419")), SKColors.OrangeRed),
                        BackColor = ParseColor(GetStr(props, "BACKGROUND_COLOR", "#261F33"), new SKColor(38, 31, 51, 220)),
                        ShowText = !props.TryGetValue("SHOW_TEXT", out var st) || !st.Equals("false", StringComparison.OrdinalIgnoreCase),
                        FontSize = GetFloat(props, "FONT_SIZE", 18f),
                        FontFamily = GetStr(props, "FONT"),
                        TextColor = ParseColor(GetStr(props, "FONT_COLOR", "#FFFFFF"), SKColors.White),
                        Unit = props.TryGetValue("UNIT", out var ru) ? ru : DefaultYamlUnit(sensor),
                        SensorKey = sensor,
                        ZIndex = GetInt(props, "Z", 2)
                    });
                }
                else if (widgetType == "LINE_GRAPH")
                {
                    graphs.Add(new GraphWidgetSpec
                    {
                        X = GetFloat(props, "X"),
                        Y = GetFloat(props, "Y"),
                        Width = GetFloat(props, "WIDTH", 180f),
                        Height = GetFloat(props, "HEIGHT", 64f),
                        MinValue = GetFloat(props, "MIN_VALUE", 0f),
                        MaxValue = GetFloat(props, "MAX_VALUE", 100f),
                        LineColor = ParseColor(GetStr(props, "LINE_COLOR", GetStr(props, "COLOR", "#FF6419")), SKColors.OrangeRed),
                        FillColor = ParseColor(GetStr(props, "FILL_COLOR", "255, 100, 25, 70"), new SKColor(255, 100, 25, 70)),
                        BackColor = ParseColor(GetStr(props, "BACKGROUND_COLOR", "20, 17, 30, 180"), new SKColor(20, 17, 30, 180)),
                        LineThickness = GetFloat(props, "LINE_WIDTH", 2.5f),
                        MaxSamples = Math.Clamp(GetInt(props, "AXIS_X_RANGE", 60), 10, 180),
                        SensorKey = sensor,
                        ZIndex = GetInt(props, "Z", 1)
                    });
                }
            }

            activeBlockPath = null;
            props.Clear();
        }

        foreach (var raw in File.ReadAllLines(yamlPath, Encoding.UTF8))
        {
            int commentIdx = raw.IndexOf('#');
            string cleanLine = commentIdx >= 0 ? raw[..commentIdx] : raw;
            var trimmed = cleanLine.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            int indent = cleanLine.Length - cleanLine.TrimStart().Length;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                trimmed = trimmed[2..].Trim();

            int colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;

            string k = trimmed[..colon].Trim();
            string v = trimmed[(colon + 1)..].Trim().Trim('"', '\'');

            while (stack.Count > 0 && stack[^1].Indent >= indent)
                stack.RemoveAt(stack.Count - 1);

            if (activeBlockPath is not null && indent <= activeBlockIndent)
                FlushBlock(ref canvasW, ref canvasH);

            if (string.IsNullOrEmpty(v))
            {
                stack.Add((indent, k));
                continue;
            }

            // Key-value leaf
            string parentPath = string.Join(".", stack.Select(s => s.Key));
            string parentUpper = parentPath.ToUpperInvariant();

            if (parentUpper == "DISPLAY")
            {
                if (k.Equals("DISPLAY_WIDTH", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out var dw) && dw > 0)
                    canvasW = dw;
                else if (k.Equals("DISPLAY_HEIGHT", StringComparison.OrdinalIgnoreCase) && TryFloat(v, out var dh) && dh > 0)
                    canvasH = dh;
                else if (k.Equals("DISPLAY_ORIENTATION", StringComparison.OrdinalIgnoreCase))
                {
                    direction = v.ToLowerInvariant() switch
                    {
                        "landscape" => 1,
                        "reverse_portrait" => 2,
                        "reverse_landscape" => 3,
                        _ => 0
                    };
                }
                continue;
            }

            if ((k.Equals("BACKGROUND_IMAGE", StringComparison.OrdinalIgnoreCase) ||
                 k.Equals("PATH", StringComparison.OrdinalIgnoreCase) && parentUpper == "STATIC_IMAGES"))
            {
                var bgResolved = ResolveAssetPath(dir, v);
                if (bgResolved != null)
                {
                    images.Add(new ImageLayerSpec
                    {
                        FilePath = bgResolved,
                        X = 0,
                        Y = 0,
                        ZIndex = -10,
                        Width = canvasW,
                        Height = canvasH,
                        IsBackground = true
                    });
                }
                continue;
            }

            if (activeBlockPath is null || !parentPath.Equals(activeBlockPath, StringComparison.Ordinal))
            {
                FlushBlock(ref canvasW, ref canvasH);
                activeBlockPath = parentPath;
                activeBlockIndent = stack.Count > 0 ? stack[^1].Indent : indent - 1;
            }

            props[k.ToUpperInvariant()] = v;
        }

        FlushBlock(ref canvasW, ref canvasH);
    }

    private static string MapYamlPathToSensor(string[] parts)
    {
        string joined = string.Join(".", parts);
        if (joined.Contains("CPU.TEMPERATURE", StringComparison.OrdinalIgnoreCase)) return "CPUT";
        if (joined.Contains("CPU.PERCENTAGE", StringComparison.OrdinalIgnoreCase)) return "CpuUsage";
        if (joined.Contains("CPU.FREQUENCY", StringComparison.OrdinalIgnoreCase)) return "CpuFrequency";
        if (joined.Contains("CPU.POWER", StringComparison.OrdinalIgnoreCase)) return "CpuTEC";
        if (joined.Contains("GPU.TEMPERATURE", StringComparison.OrdinalIgnoreCase)) return "GPUT";
        if (joined.Contains("GPU.PERCENTAGE", StringComparison.OrdinalIgnoreCase)) return "GpuUsage";
        if (joined.Contains("GPU.MEMORY_PERCENT", StringComparison.OrdinalIgnoreCase)) return "GPUMemoryLoad";
        if (joined.Contains("GPU.MEMORY_USED", StringComparison.OrdinalIgnoreCase)) return "GPUMemoryUsed";
        if (joined.Contains("GPU.FREQUENCY", StringComparison.OrdinalIgnoreCase)) return "GpuFrequency";
        if (joined.Contains("GPU.POWER", StringComparison.OrdinalIgnoreCase)) return "GpuTEC";
        if (joined.Contains("GPU.FAN", StringComparison.OrdinalIgnoreCase)) return "GpuFan";
        if (joined.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) && joined.Contains("PERCENT", StringComparison.OrdinalIgnoreCase)) return "MemoryUsage";
        if (joined.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) && joined.Contains("USED", StringComparison.OrdinalIgnoreCase)) return "MemoryUseInt";
        if (joined.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) && joined.Contains("FREE", StringComparison.OrdinalIgnoreCase)) return "MemoryFree";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains("PERCENT", StringComparison.OrdinalIgnoreCase)) return "DiskUsage";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains("USED", StringComparison.OrdinalIgnoreCase)) return "DiskUsed";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains("FREE", StringComparison.OrdinalIgnoreCase)) return "DiskFree";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains("TEMP", StringComparison.OrdinalIgnoreCase)) return "DiskTemp";
        if (joined.Contains("NET", StringComparison.OrdinalIgnoreCase) && joined.Contains("UPLOAD", StringComparison.OrdinalIgnoreCase)) return "UpNetSpeed";
        if (joined.Contains("NET", StringComparison.OrdinalIgnoreCase) && joined.Contains("DOWNLOAD", StringComparison.OrdinalIgnoreCase)) return "DownNetSpeed";
        if (joined.Contains("DATE.DAY", StringComparison.OrdinalIgnoreCase)) return "CurrentDate";
        if (joined.Contains("DATE.HOUR", StringComparison.OrdinalIgnoreCase)) return "CurrentTime";
        return parts.Length >= 2 ? parts[^2] : parts[0];
    }

    private static string DefaultYamlUnit(string sensorKey) => sensorKey.ToUpperInvariant() switch
    {
        "CPUT" or "GPUT" or "DISKTEMP" => "°C",
        "CPUUSAGE" or "GPUUSAGE" or "GPUMEMORYLOAD" or "MEMORYUSAGE" or "DISKUSAGE" or "GPUFAN" => "%",
        "CPUFREQUENCY" or "GPUFREQUENCY" => "MHz",
        "CPUTEC" or "GPUTEC" => "W",
        "MEMORYUSEINT" or "MEMORYFREE" or "GPUMEMORYUSED" or "DISKUSED" or "DISKFREE" => "GB",
        "UPNETSPEED" or "DOWNNETSPEED" => "KB/s",
        _ => string.Empty
    };

    private static TextAlignMode ParseAlign(string? align)
    {
        if (string.IsNullOrWhiteSpace(align)) return TextAlignMode.Left;
        if (align.Contains("center", StringComparison.OrdinalIgnoreCase)) return TextAlignMode.Center;
        if (align.Contains("right", StringComparison.OrdinalIgnoreCase)) return TextAlignMode.Right;
        return TextAlignMode.Left;
    }

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
        string raw = hex.Trim().Trim('[', ']');
        if (raw.Contains(','))
        {
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is 3 or 4 &&
                byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
            {
                byte a = 255;
                if (parts.Length == 4)
                    byte.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out a);
                return new SKColor(r, g, b, a);
            }
        }
        if (!raw.StartsWith('#')) raw = "#" + raw;
        return SKColor.TryParse(raw, out var color) ? color : fallback;
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
