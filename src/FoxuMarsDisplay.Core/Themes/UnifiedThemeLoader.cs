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
            if (ext is ".gif" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".mp4" or ".avi" or ".mov" or ".wmv" or ".webm")
                return LoadSingleImageOrGif(path);
        }

        if (Directory.Exists(path))
            return LoadDirectory(path, schemeJsonPath);

        throw new FileNotFoundException($"Theme path not found: {path}");
    }

    private static bool IsVideoExtension(string path) =>
        path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);

    private static DisplayTheme LoadSingleImageOrGif(string filePath)
    {
        int w = 360, h = 960;
        if (!IsVideoExtension(filePath))
        {
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
        }

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
                var nestedBg = Directory.GetFiles(extractDir, "*.mp4", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(extractDir, "*.png", SearchOption.AllDirectories))
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
        var statusIcons = new List<StatusIconSpec>();
        var audioVisualizers = new List<AudioVisualizerSpec>();
        var albumArts = new List<AlbumArtSpec>();

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

                if (FindSettingFile(dir) == null &&
                    !File.Exists(Path.Combine(dir, "theme.yaml")) &&
                    (root.TryGetProperty("widgets", out _) || root.TryGetProperty("elements", out _)))
                {
                    ParseFoxuJsonTheme(root, dir, canvasW, canvasH, imageLayers, textWidgets, barWidgets, radialWidgets, graphWidgets, audioVisualizers, albumArts);
                }
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
            ParseSettingTxt(
                settingPath, dir, ref name, ref canvasW, ref canvasH, ref direction,
                imageLayers, textWidgets, barWidgets, radialWidgets, statusIcons, audioVisualizers, albumArts);
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

        bool hasBackground = imageLayers.Any(l => l.IsBackground || l.IsVideo ||
            Path.GetFileName(l.FilePath).Equals("back.png", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(l.FilePath).Equals("background.png", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(l.FilePath).Equals("back.mp4", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(l.FilePath).Equals("video.mp4", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(l.FilePath).Equals("default.mp4", StringComparison.OrdinalIgnoreCase));

        if (!hasBackground)
        {
            string? bgFile = new[] { "back.mp4", "video.mp4", "default.mp4" }
                .Select(f => Path.Combine(dir, f))
                .FirstOrDefault(File.Exists)
                ?? Directory.GetFiles(dir, "*.mp4", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? new[] { "background.png", "back.png", "default.gif" }
                    .Select(f => Path.Combine(dir, f))
                    .FirstOrDefault(File.Exists);

            if (bgFile != null)
            {
                if (!IsVideoExtension(bgFile) && Math.Abs(canvasW - 360f) < 1f && Math.Abs(canvasH - 960f) < 1f)
                {
                    try
                    {
                        using var codec = SKCodec.Create(bgFile);
                        if (codec != null && codec.Info.Width > 0 && codec.Info.Height > 0)
                        {
                            canvasW = codec.Info.Width;
                            canvasH = codec.Info.Height;
                        }
                    }
                    catch { }
                }

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
        theme.StatusIcons.AddRange(statusIcons.OrderBy(i => i.ZIndex));
        theme.AudioVisualizers.AddRange(audioVisualizers.OrderBy(a => a.ZIndex));
        theme.AlbumArts.AddRange(albumArts.OrderBy(a => a.ZIndex));

        foreach (var fFolder in new[] { "font", "fonts" })
        {
            var fontDir = Path.Combine(dir, fFolder);
            if (!Directory.Exists(fontDir)) continue;
            theme.FontFiles.AddRange(Directory.GetFiles(fontDir, "*.*", SearchOption.AllDirectories)
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
                        if (resolved == null && propName == "BackgroundVideoFile")
                        {
                            resolved = ResolveAssetPath(dir, "back.mp4")
                                ?? ResolveAssetPath(dir, "video.mp4")
                                ?? Directory.GetFiles(dir, "*.mp4", SearchOption.TopDirectoryOnly).FirstOrDefault();
                        }
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

    private static void ParseFoxuJsonTheme(
        JsonElement root,
        string dir,
        float canvasW,
        float canvasH,
        List<ImageLayerSpec> images,
        List<TextWidgetSpec> texts,
        List<BarWidgetSpec> bars,
        List<RadialWidgetSpec> radials,
        List<GraphWidgetSpec> graphs,
        List<AudioVisualizerSpec> audioVisualizers,
        List<AlbumArtSpec> albumArts)
    {
        if (!root.TryGetProperty("widgets", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            if (!root.TryGetProperty("elements", out arr) || arr.ValueKind != JsonValueKind.Array)
                return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            string kind = item.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String
                ? (tEl.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                : string.Empty;
            float x = item.TryGetProperty("x", out var xEl) && xEl.TryGetSingle(out var xv) ? xv : 0f;
            float y = item.TryGetProperty("y", out var yEl) && yEl.TryGetSingle(out var yv) ? yv : 0f;
            int z = item.TryGetProperty("z", out var zEl) && zEl.TryGetInt32(out var zv) ? zv : 1;
            float w = item.TryGetProperty("width", out var wEl) && wEl.TryGetSingle(out var wv) ? wv : 160f;
            float h = item.TryGetProperty("height", out var hEl) && hEl.TryGetSingle(out var hv) ? hv : 24f;
            string sensor = item.TryGetProperty("sensor", out var sEl) && sEl.ValueKind == JsonValueKind.String ? (sEl.GetString() ?? "") : "";
            string colorHex = item.TryGetProperty("color", out var cEl) && cEl.ValueKind == JsonValueKind.String ? (cEl.GetString() ?? "#FFFFFF") : "#FFFFFF";
            string backHex = item.TryGetProperty("backColor", out var bcEl) && bcEl.ValueKind == JsonValueKind.String ? (bcEl.GetString() ?? "") : "";

            switch (kind)
            {
                case "text":
                    string? content = item.TryGetProperty("text", out var txtEl) && txtEl.ValueKind == JsonValueKind.String ? txtEl.GetString() : null;
                    bool isStatic = string.IsNullOrWhiteSpace(sensor) && !string.IsNullOrWhiteSpace(content);
                    var alignMode = item.TryGetProperty("align", out var alEl) && alEl.ValueKind == JsonValueKind.String ? ParseAlign(alEl.GetString()) : TextAlignMode.Left;
                    float boxWJson = item.TryGetProperty("boxWidth", out var bwEl) && bwEl.TryGetSingle(out var bwv)
                        ? bwv
                        : (alignMode != TextAlignMode.Left && item.TryGetProperty("width", out var jwEl) && jwEl.TryGetSingle(out var jwv) ? jwv : 0f);
                    float fsJson = item.TryGetProperty("fontSize", out var fsEl) && fsEl.TryGetSingle(out var fsv) ? fsv : 22f;
                    bool jBold = (item.TryGetProperty("isBold", out var jbEl) && jbEl.ValueKind == JsonValueKind.True) ||
                                 (item.TryGetProperty("bold", out var jb2El) && jb2El.ValueKind == JsonValueKind.True);
                    bool jItalic = (item.TryGetProperty("isItalic", out var jiEl) && jiEl.ValueKind == JsonValueKind.True) ||
                                   (item.TryGetProperty("italic", out var ji2El) && ji2El.ValueKind == JsonValueKind.True);
                    bool jUnderline = (item.TryGetProperty("isUnderline", out var juEl) && juEl.ValueKind == JsonValueKind.True) ||
                                      (item.TryGetProperty("underline", out var ju2El) && ju2El.ValueKind == JsonValueKind.True);
                    bool jStrikeout = (item.TryGetProperty("isStrikeout", out var jsEl) && jsEl.ValueKind == JsonValueKind.True) ||
                                      (item.TryGetProperty("strikeout", out var js2El) && js2El.ValueKind == JsonValueKind.True);
                    bool jShadow = (item.TryGetProperty("hasShadow", out var jshEl) && jshEl.ValueKind == JsonValueKind.True) ||
                                   (item.TryGetProperty("shadow", out var jsh2El) && jsh2El.ValueKind == JsonValueKind.True);
                    texts.Add(new TextWidgetSpec
                    {
                        X = x,
                        Y = y,
                        ZIndex = z,
                        FontSize = fsJson,
                        FontFamily = item.TryGetProperty("fontFamily", out var ffEl) && ffEl.ValueKind == JsonValueKind.String ? (ffEl.GetString() ?? "") : "",
                        Color = ParseColor(colorHex, SKColors.White),
                        SensorKey = sensor,
                        Prefix = item.TryGetProperty("prefix", out var pEl) && pEl.ValueKind == JsonValueKind.String ? (pEl.GetString() ?? "") : "",
                        Unit = item.TryGetProperty("unit", out var uEl) && uEl.ValueKind == JsonValueKind.String ? (uEl.GetString() ?? "") : "",
                        IsStaticText = isStatic,
                        StaticContent = content,
                        Align = alignMode,
                        IsBold = jBold,
                        IsItalic = jItalic,
                        IsUnderline = jUnderline,
                        IsStrikeout = jStrikeout,
                        HasShadow = jShadow,
                        BoxWidth = boxWJson,
                        BoxHeight = boxWJson > 0f ? fsJson * 1.3f : 0f,
                        Anchor = alignMode switch
                        {
                            TextAlignMode.Center => "ma",
                            TextAlignMode.Right => "ra",
                            _ => "la"
                        }
                    });
                    break;

                case "bar":
                case "card":
                    bars.Add(new BarWidgetSpec
                    {
                        X = x,
                        Y = y,
                        ZIndex = z,
                        MaxWidth = w,
                        MaxHeight = h,
                        MaxNum = item.TryGetProperty("max", out var mxEl) && mxEl.TryGetSingle(out var mxv) ? mxv : 100f,
                        CornerRadius = item.TryGetProperty("radius", out var rEl) && rEl.TryGetSingle(out var rv) ? rv : 4f,
                        FillColor = ParseColor(colorHex, SKColors.Cyan),
                        BackColor = string.IsNullOrEmpty(backHex) ? SKColor.Empty : ParseColor(backHex, SKColor.Empty),
                        SensorKey = sensor,
                        IsStaticFill = kind == "card" || string.IsNullOrWhiteSpace(sensor)
                    });
                    break;

                case "radial":
                case "ring":
                    radials.Add(new RadialWidgetSpec
                    {
                        Cx = x + w * 0.5f,
                        Cy = y + h * 0.5f,
                        Radius = Math.Max(6f, Math.Min(w, h) * 0.45f),
                        Thickness = item.TryGetProperty("thickness", out var thEl) && thEl.TryGetSingle(out var thv) ? thv : 10f,
                        FillColor = ParseColor(colorHex, SKColors.OrangeRed),
                        BackColor = string.IsNullOrEmpty(backHex) ? new SKColor(38, 31, 51, 220) : ParseColor(backHex, SKColor.Empty),
                        SensorKey = sensor,
                        ZIndex = z,
                        ShowText = !item.TryGetProperty("showText", out var stEl) || stEl.ValueKind != JsonValueKind.False
                    });
                    break;

                case "graph":
                    graphs.Add(new GraphWidgetSpec
                    {
                        X = x, Y = y, Width = w, Height = h,
                        LineColor = ParseColor(colorHex, SKColors.OrangeRed),
                        SensorKey = sensor,
                        ZIndex = z
                    });
                    break;

                case "audiovisualizer":
                    audioVisualizers.Add(new AudioVisualizerSpec
                    {
                        X = x, Y = y, Width = w, Height = h, ZIndex = z,
                        PrimaryColor = ParseColor(colorHex, SKColors.Cyan),
                        SecondaryColor = string.IsNullOrEmpty(backHex) ? SKColors.Magenta : ParseColor(backHex, SKColors.Magenta)
                    });
                    break;

                case "albumart":
                    albumArts.Add(new AlbumArtSpec
                    {
                        X = x, Y = y, Width = w, Height = h, ZIndex = z,
                        IsVinylSpin = item.TryGetProperty("vinyl", out var vEl) && vEl.ValueKind == JsonValueKind.True
                    });
                    break;

                case "image":
                    string? src = item.TryGetProperty("src", out var srcEl) && srcEl.ValueKind == JsonValueKind.String ? srcEl.GetString() : null;
                    string? resolved = !string.IsNullOrWhiteSpace(src) ? ResolveAssetPath(dir, src) : null;
                    if (resolved != null)
                    {
                        images.Add(new ImageLayerSpec
                        {
                            FilePath = resolved,
                            X = x, Y = y, ZIndex = z,
                            Width = w > 0 ? w : canvasW,
                            Height = h > 0 ? h : canvasH,
                            IsBackground = item.TryGetProperty("isBackground", out var bgEl) && bgEl.ValueKind == JsonValueKind.True
                        });
                    }
                    break;
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
        List<BarWidgetSpec> bars,
        List<RadialWidgetSpec> radials,
        List<StatusIconSpec> statusIcons,
        List<AudioVisualizerSpec> audioVisualizers,
        List<AlbumArtSpec> albumArts)
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
                string title = GetStr(p, "Title");
                string? content = p.TryGetValue("content", out var c) ? c : (p.TryGetValue("Text", out var t) ? t : null);
                if (string.IsNullOrWhiteSpace(dataKey) && string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(title))
                    content = title;

                string prefix = string.Empty;
                if (!string.IsNullOrWhiteSpace(dataKey) && !string.IsNullOrWhiteSpace(title))
                    prefix = title.EndsWith(' ') ? title : title + " ";

                bool isStatic = GetBool(p, "IsDefaultText") ||
                                dataKey.Equals("Static", StringComparison.OrdinalIgnoreCase) ||
                                (string.IsNullOrWhiteSpace(dataKey) && !string.IsNullOrWhiteSpace(content));

                var align = ParseSettingTextAlign(GetStr(p, "TextAlignment"), GetStr(p, "Align"));
                float fontSize = GetFloat(p, "FontSize", 24f);
                float boxW = GetBool(p, "IsShowWidth") ? GetFloat(p, "width") : 0f;
                float boxH = boxW > 0f ? (GetFloat(p, "height") > 0f ? GetFloat(p, "height") : fontSize * 1.3f) : 0f;
                string anchor = align switch
                {
                    TextAlignMode.Center => "ma",
                    TextAlignMode.Right => "ra",
                    _ => "la"
                };

                bool isBold = GetBool(p, "IsBold") || GetBool(p, "Bold") ||
                              GetStr(p, "FontWeight").Equals("Bold", StringComparison.OrdinalIgnoreCase) ||
                              GetStr(p, "FontWeight").Equals("700", StringComparison.OrdinalIgnoreCase);
                bool isItalic = GetBool(p, "IsItalic") || GetBool(p, "Italic") ||
                                GetStr(p, "FontStyle").Equals("Italic", StringComparison.OrdinalIgnoreCase) ||
                                GetStr(p, "FontStyle").Equals("Oblique", StringComparison.OrdinalIgnoreCase);
                string deco = GetStr(p, "TextDecorations");
                bool isUnderline = GetBool(p, "IsUnderline") || GetBool(p, "Underline") ||
                                   deco.Contains("Underline", StringComparison.OrdinalIgnoreCase);
                bool isStrikeout = GetBool(p, "IsStrikeout") || GetBool(p, "Strikeout") ||
                                   deco.Contains("Strikethrough", StringComparison.OrdinalIgnoreCase);
                bool hasShadow = GetBool(p, "HasShadow") || GetBool(p, "Shadow");

                texts.Add(new TextWidgetSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z"),
                    FontSize = fontSize,
                    FontFamily = GetStr(p, "FontFamily").TrimStart('#'),
                    Color = ParseColor(GetStr(p, "Foreground", "#FFFFFF"), SKColors.White),
                    Opacity = Math.Clamp(GetFloat(p, "Opacity", 1f), 0f, 1f),
                    SensorKey = dataKey,
                    Prefix = prefix,
                    Unit = GetStr(p, "unit"),
                    IsStaticText = isStatic,
                    StaticContent = content,
                    Align = align,
                    IsBold = isBold,
                    IsItalic = isItalic,
                    IsUnderline = isUnderline,
                    IsStrikeout = isStrikeout,
                    HasShadow = hasShadow,
                    BoxWidth = boxW,
                    BoxHeight = boxH,
                    Anchor = anchor
                });
            }
            else if (key.Equals("BorderLine", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("LineProgress", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("DefaultLine", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("GridLine", StringComparison.OrdinalIgnoreCase))
            {
                string dataKey = GetStr(p, "data");
                bool isGrid = key.Equals("GridLine", StringComparison.OrdinalIgnoreCase);
                bars.Add(new BarWidgetSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z"),
                    MaxWidth = GetFloat(p, "maxwidth", 200f),
                    MaxHeight = GetFloat(p, "maxheight", 20f),
                    MinNum = GetFloat(p, "MinNum", 0f),
                    MaxNum = GetFloat(p, "MaxNum", 100f),
                    CornerRadius = ParseFirstFloat(GetStr(p, "CornerRadius"), 0f),
                    FillColor = ParseColor(GetStr(p, "Fill", "#00FFFF"), SKColors.Cyan),
                    BackColor = p.ContainsKey("BackColor") ? ParseColor(GetStr(p, "BackColor"), SKColor.Empty) : SKColor.Empty,
                    SensorKey = dataKey,
                    IsSegmented = isGrid,
                    IsStaticFill = string.IsNullOrWhiteSpace(dataKey),
                    BorderThickness = GetFloat(p, "BorderThicknes", GetFloat(p, "BorderThickness", 0f)),
                    BorderColor = p.ContainsKey("BorderFill") ? ParseColor(GetStr(p, "BorderFill"), SKColor.Empty) : SKColor.Empty,
                    SegmentCount = isGrid ? Math.Clamp(GetInt(p, "maxcount", 10), 2, 50) : 10,
                    SegmentGap = isGrid ? Math.Max(0f, GetFloat(p, "Margin", 2f)) : 2f,
                    ForceHorizontal = isGrid && GetStr(p, "Orientation").Equals("Horizontal", StringComparison.OrdinalIgnoreCase)
                });
            }
            else if (key.Equals("RingProgressBar", StringComparison.OrdinalIgnoreCase))
            {
                float x = GetFloat(p, "x");
                float y = GetFloat(p, "y");
                float ringWidth = Math.Max(2f, GetFloat(p, "RingWidth", 10f));
                float perimeter = Math.Max(16f, GetFloat(p, "Perimeter", 100f));
                radials.Add(new RadialWidgetSpec
                {
                    Cx = x + perimeter * 0.5f,
                    Cy = y + perimeter * 0.5f,
                    Radius = Math.Max(4f, (perimeter - ringWidth) * 0.5f),
                    Thickness = ringWidth,
                    StartAngle = -90f,
                    EndAngle = 270f,
                    MinValue = GetFloat(p, "MinValue", 0f),
                    MaxValue = GetFloat(p, "MaxValue", 100f),
                    FillColor = ParseColor(GetStr(p, "IndexColor", GetStr(p, "Fill", "#FF00FFFF")), SKColors.Cyan),
                    BackColor = p.ContainsKey("BackColor") ? ParseColor(GetStr(p, "BackColor"), SKColor.Empty) : new SKColor(255, 255, 255, 70),
                    ShowText = false,
                    SensorKey = GetStr(p, "data"),
                    ZIndex = GetInt(p, "z", 2),
                    Clockwise = true
                });
            }
            else if (key.Equals("IsEnableItem", StringComparison.OrdinalIgnoreCase))
            {
                string src = GetStr(p, "Source");
                string? resolved = !string.IsNullOrWhiteSpace(src) ? ResolveAssetPath(themeDir, src) : null;
                if (resolved != null)
                {
                    statusIcons.Add(new StatusIconSpec
                    {
                        FilePath = resolved,
                        X = GetFloat(p, "x"),
                        Y = GetFloat(p, "y"),
                        ZIndex = GetInt(p, "z", 3),
                        Width = p.TryGetValue("width", out var ws) && TryFloat(ws, out var wv) ? wv : null,
                        Height = p.TryGetValue("height", out var hs) && TryFloat(hs, out var hv) ? hv : null,
                        SensorKey = GetStr(p, "Endata", GetStr(p, "data"))
                    });
                }
            }
            else if (key.Equals("AudioVisualizerControl", StringComparison.OrdinalIgnoreCase))
            {
                audioVisualizers.Add(new AudioVisualizerSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z", 2),
                    Width = GetFloat(p, "Width", GetFloat(p, "width", 200f)),
                    Height = GetFloat(p, "Height", GetFloat(p, "height", 60f)),
                    BandWidth = Math.Max(2f, GetFloat(p, "BandWidth", 8f)),
                    PrimaryColor = ParseColor(GetStr(p, "Color", "#FF00FFFF"), SKColors.Cyan),
                    SecondaryColor = ParseColor(GetStr(p, "SeColor", "#FFFF00FF"), SKColors.Magenta)
                });
            }
            else if (key.Equals("SonglyricsControl", StringComparison.OrdinalIgnoreCase))
            {
                texts.Add(new TextWidgetSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z", 4),
                    FontSize = GetFloat(p, "FontSize", 18f),
                    FontFamily = GetStr(p, "FontFamily").TrimStart('#'),
                    Color = ParseColor(GetStr(p, "FontColor", GetStr(p, "Foreground", "#FFFFFF")), SKColors.White),
                    SensorKey = "SongLyric",
                    Align = TextAlignMode.Center,
                    BoxWidth = GetFloat(p, "width", 300f),
                    BoxHeight = GetFloat(p, "height", 40f),
                    Anchor = "mm"
                });
            }
            else if (key.Equals("SongIcon", StringComparison.OrdinalIgnoreCase))
            {
                albumArts.Add(new AlbumArtSpec
                {
                    X = GetFloat(p, "x"),
                    Y = GetFloat(p, "y"),
                    ZIndex = GetInt(p, "z", 3),
                    Width = GetFloat(p, "width", 80f),
                    Height = GetFloat(p, "height", 80f),
                    CornerRadius = ParseFirstFloat(GetStr(p, "CornerRadius"), 8f)
                });
            }
            else if (key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                     IsVideoExtension(key))
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
                        IsBackground = GetBool(p, "IsBack") || IsVideoExtension(key)
                    });
                }
            }
        }
    }

    private static TextAlignMode ParseSettingTextAlign(string textAlignment, string align) => textAlignment.Trim() switch
    {
        "1" => TextAlignMode.Center,
        "2" => TextAlignMode.Right,
        "0" => TextAlignMode.Left,
        _ => ParseAlign(align)
    };

    private static float ParseFirstFloat(string raw, float def)
    {
        if (string.IsNullOrWhiteSpace(raw)) return def;
        var first = raw.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return first != null && TryFloat(first, out var f) ? f : def;
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
                    float? wOpt = props.TryGetValue("WIDTH", out var ws) && TryFloat(ws, out var wv) ? wv : (isBg ? cw : null);
                    float? hOpt = props.TryGetValue("HEIGHT", out var hs) && TryFloat(hs, out var hv) ? hv : (isBg ? ch : null);
                    if (isBg && wOpt is > 0 && hOpt is > 0 && Math.Abs(cw - 360f) < 1f && Math.Abs(ch - 960f) < 1f)
                    {
                        cw = wOpt.Value;
                        ch = hOpt.Value;
                    }

                    if (!images.Any(existing => existing.FilePath.Equals(imgPath, StringComparison.OrdinalIgnoreCase) && existing.IsBackground == isBg))
                    {
                        images.Add(new ImageLayerSpec
                        {
                            FilePath = imgPath,
                            X = GetFloat(props, "X"),
                            Y = GetFloat(props, "Y"),
                            ZIndex = isBg ? -5 : GetInt(props, "Z", 0),
                            Width = wOpt,
                            Height = hOpt,
                            IsBackground = isBg
                        });
                    }
                }
            }
            else if (pathUpper.StartsWith("STATIC_TEXT", StringComparison.Ordinal) &&
                     props.TryGetValue("TEXT", out var staticText))
            {
                string anchor = GetStr(props, "ANCHOR", "la");
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
                    Align = ResolveAnchorAlign(anchor, GetStr(props, "ALIGN")),
                    IsBold = GetBool(props, "BOLD"),
                    BoxWidth = GetFloat(props, "WIDTH"),
                    BoxHeight = GetFloat(props, "HEIGHT"),
                    Anchor = anchor
                });
            }
            else if (pathUpper.StartsWith("STATS.", StringComparison.Ordinal))
            {
                var parts = pathUpper.Split('.');
                string widgetType = parts[^1];
                string sensor = MapYamlPathToSensor(parts);

                if (widgetType == "BAR" || widgetType == "GRAPH" || widgetType.EndsWith("_GRAPH", StringComparison.Ordinal) && widgetType != "LINE_GRAPH")
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
                        SensorKey = sensor,
                        ReverseDirection = GetBool(props, "REVERSE_DIRECTION")
                    });
                }
                else if (widgetType == "RADIAL" || widgetType.EndsWith("_RADIAL", StringComparison.Ordinal))
                {
                    bool drawBarBg = GetBool(props, "DRAW_BAR_BACKGROUND") || props.ContainsKey("BAR_BACKGROUND_COLOR") || props.ContainsKey("BACKGROUND_COLOR");
                    SKColor bgCol = drawBarBg
                        ? ParseColor(GetStr(props, "BAR_BACKGROUND_COLOR", GetStr(props, "BACKGROUND_COLOR", "#261F33")), new SKColor(38, 31, 51, 220))
                        : SKColor.Empty;
                    bool clockwise = !props.TryGetValue("CLOCKWISE", out var cwStr) || !cwStr.Equals("false", StringComparison.OrdinalIgnoreCase);
                    bool showUnit = !props.TryGetValue("SHOW_UNIT", out var rsu) || !rsu.Equals("false", StringComparison.OrdinalIgnoreCase);

                    float offX = 0f, offY = 0f;
                    if (props.TryGetValue("TEXT_OFFSET", out var toStr))
                    {
                        var tp = toStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (tp.Length >= 1) TryFloat(tp[0], out offX);
                        if (tp.Length >= 2) TryFloat(tp[1], out offY);
                    }

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
                        BackColor = bgCol,
                        ShowText = !props.TryGetValue("SHOW_TEXT", out var st) || !st.Equals("false", StringComparison.OrdinalIgnoreCase),
                        FontSize = GetFloat(props, "FONT_SIZE", 18f),
                        FontFamily = GetStr(props, "FONT"),
                        TextColor = ParseColor(GetStr(props, "FONT_COLOR", "#FFFFFF"), SKColors.White),
                        Unit = !showUnit ? string.Empty : (props.TryGetValue("UNIT", out var ru) ? ru : DefaultYamlUnit(sensor)),
                        SensorKey = sensor,
                        ZIndex = GetInt(props, "Z", 2),
                        Clockwise = clockwise,
                        TextOffsetX = offX,
                        TextOffsetY = offY
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
                        MaxSamples = Math.Clamp(GetInt(props, "AXIS_X_RANGE", GetInt(props, "HISTORY_SIZE", 60)), 10, 180),
                        SensorKey = sensor,
                        ZIndex = GetInt(props, "Z", 1)
                    });
                }
                else if (props.ContainsKey("X") && props.ContainsKey("Y"))
                {
                    bool showUnit = !props.TryGetValue("SHOW_UNIT", out var su) || !su.Equals("false", StringComparison.OrdinalIgnoreCase);
                    string anchor = GetStr(props, "ANCHOR", "la");
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
                        Unit = !showUnit ? string.Empty : (props.TryGetValue("UNIT", out var u) ? u : DefaultYamlUnit(sensor)),
                        Align = ResolveAnchorAlign(anchor, GetStr(props, "ALIGN")),
                        IsBold = GetBool(props, "BOLD"),
                        BoxWidth = GetFloat(props, "WIDTH"),
                        BoxHeight = GetFloat(props, "HEIGHT"),
                        Anchor = anchor
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
                continue;
            }

            bool isWidgetBlock = parentUpper.StartsWith("STATS.", StringComparison.Ordinal) ||
                                 parentUpper.StartsWith("STATIC_TEXT", StringComparison.Ordinal);
            if (!isWidgetBlock &&
                (k.Equals("BACKGROUND_IMAGE", StringComparison.OrdinalIgnoreCase) ||
                 (k.Equals("PATH", StringComparison.OrdinalIgnoreCase) && parentUpper == "STATIC_IMAGES")))
            {
                var bgResolved = ResolveAssetPath(dir, v);
                if (bgResolved != null &&
                    !images.Any(existing => existing.FilePath.Equals(bgResolved, StringComparison.OrdinalIgnoreCase) && existing.IsBackground))
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
        if (joined.Contains("CPU.PERCENTAGE", StringComparison.OrdinalIgnoreCase) || joined.Contains("CPU.LOAD", StringComparison.OrdinalIgnoreCase)) return "CpuUsage";
        if (joined.Contains("CPU.FREQUENCY", StringComparison.OrdinalIgnoreCase)) return "CpuFrequency";
        if (joined.Contains("CPU.POWER", StringComparison.OrdinalIgnoreCase)) return "CpuTEC";
        if (joined.Contains("CPU.FAN", StringComparison.OrdinalIgnoreCase)) return "GpuFan";
        if (joined.Contains("GPU.TEMPERATURE", StringComparison.OrdinalIgnoreCase)) return "GPUT";
        if (joined.Contains("GPU.PERCENTAGE", StringComparison.OrdinalIgnoreCase)) return "GpuUsage";
        if (joined.Contains("GPU.MEMORY_PERCENT", StringComparison.OrdinalIgnoreCase)) return "GPUMemoryLoad";
        if (joined.Contains("GPU.MEMORY_USED", StringComparison.OrdinalIgnoreCase)) return "GPUMemoryUsed";
        if (joined.Contains("GPU.MEMORY_TOTAL", StringComparison.OrdinalIgnoreCase)) return "GPUMemoryTotal";
        if (joined.Contains("GPU.MEMORY", StringComparison.OrdinalIgnoreCase)) return "GPUMemoryLoad";
        if (joined.Contains("GPU.FREQUENCY", StringComparison.OrdinalIgnoreCase)) return "GpuFrequency";
        if (joined.Contains("GPU.POWER", StringComparison.OrdinalIgnoreCase)) return "GpuTEC";
        if (joined.Contains("GPU.FAN", StringComparison.OrdinalIgnoreCase)) return "GpuFan";
        if (joined.Contains("GPU.FPS", StringComparison.OrdinalIgnoreCase)) return "FPS";
        if (joined.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) && joined.Contains("PERCENT", StringComparison.OrdinalIgnoreCase)) return "MemoryUsage";
        if (joined.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) && joined.Contains("USED", StringComparison.OrdinalIgnoreCase)) return "MemoryUsed";
        if (joined.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) && joined.Contains("FREE", StringComparison.OrdinalIgnoreCase)) return "MemoryFree";
        if (joined.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) && joined.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)) return "MemoryTotal";
        if (joined.Contains("MEMORY.VIRTUAL", StringComparison.OrdinalIgnoreCase)) return "MemoryUsage";
        if (joined.Contains("MEMORY.SWAP", StringComparison.OrdinalIgnoreCase)) return "Swap";
        if (joined.Contains(".LOAD.", StringComparison.OrdinalIgnoreCase)) return "SystemLoad";
        if (joined.Contains("WEATHER.WEATHER_DESCRIPTION", StringComparison.OrdinalIgnoreCase)) return "WEATHER_DESCRIPTION";
        if (joined.Contains("WEATHER.TEMPERATURE_FELT", StringComparison.OrdinalIgnoreCase)) return "TEMPERATURE_FELT";
        if (joined.Contains("WEATHER.HUMIDITY", StringComparison.OrdinalIgnoreCase)) return "HUMIDITY";
        if (joined.Contains("WEATHER.TEMPERATURE", StringComparison.OrdinalIgnoreCase)) return "WEATHER_TEMP";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains("PERCENT", StringComparison.OrdinalIgnoreCase)) return "DiskUsage";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains(".USED", StringComparison.OrdinalIgnoreCase) && !joined.Contains("PERCENT", StringComparison.OrdinalIgnoreCase)) return "DiskUsed";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains("FREE", StringComparison.OrdinalIgnoreCase)) return "DiskFree";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)) return "DiskTotal";
        if (joined.Contains("DISK", StringComparison.OrdinalIgnoreCase) && joined.Contains("TEMP", StringComparison.OrdinalIgnoreCase)) return "DiskTemp";
        if (joined.Contains("NET", StringComparison.OrdinalIgnoreCase) && joined.Contains("UPLOAD", StringComparison.OrdinalIgnoreCase)) return "UpNetSpeed";
        if (joined.Contains("NET", StringComparison.OrdinalIgnoreCase) && joined.Contains("DOWNLOAD", StringComparison.OrdinalIgnoreCase)) return "DownNetSpeed";
        if (joined.Contains("UPTIME", StringComparison.OrdinalIgnoreCase)) return "Uptime";
        if (joined.Contains("PING", StringComparison.OrdinalIgnoreCase)) return "Ping";
        if (joined.Contains("DATE.DAY", StringComparison.OrdinalIgnoreCase)) return "CurrentDate";
        if (joined.Contains("DATE.HOUR", StringComparison.OrdinalIgnoreCase)) return "CurrentTime";
        return parts.Length >= 2 ? parts[^2] : parts[0];
    }

    private static string DefaultYamlUnit(string sensorKey) => sensorKey.ToUpperInvariant() switch
    {
        "CPUT" or "GPUT" or "DISKTEMP" or "TEMPERATURE_FELT" or "WEATHER_TEMP" => "°C",
        "CPUUSAGE" or "GPUUSAGE" or "GPUMEMORYLOAD" or "MEMORYUSAGE" or "DISKUSAGE" or "GPUFAN" or "SWAP" or "SYSTEMLOAD" or "HUMIDITY" => "%",
        "CPUFREQUENCY" or "GPUFREQUENCY" => "MHz",
        "CPUTEC" or "GPUTEC" => "W",
        "MEMORYUSED" or "MEMORYFREE" or "MEMORYTOTAL" or "GPUMEMORYUSED" or "GPUMEMORYTOTAL" or "DISKUSED" or "DISKFREE" or "DISKTOTAL" => "GB",
        "UPNETSPEED" or "DOWNNETSPEED" => "KB/s",
        "PING" => " ms",
        _ => string.Empty
    };

    private static TextAlignMode ResolveAnchorAlign(string? anchor, string? align)
    {
        if (!string.IsNullOrWhiteSpace(anchor))
        {
            char h = char.ToLowerInvariant(anchor[0]);
            if (h == 'm') return TextAlignMode.Center;
            if (h == 'r') return TextAlignMode.Right;
        }
        return ParseAlign(align);
    }

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
