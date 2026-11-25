using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FoxuMarsDisplay.Core.Rendering;
using FoxuMarsDisplay.Core.Sensors;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Themes;

/// <summary>
/// Exports DisplayTheme definitions to Setting.txt, foxu-theme-v1 JSON, and self-contained .smtheme packages,
/// and provides WCAG relative-luminance auto-contrast color calculation.
/// </summary>
public static class ThemeExporter
{
    public static SKColor ComputeAutoContrastColor(SKBitmap? background, SKRect region, SKColor preferred)
    {
        if (background is null || background.Width <= 0 || background.Height <= 0)
            return preferred;

        int x0 = Math.Clamp((int)Math.Floor(region.Left), 0, background.Width - 1);
        int y0 = Math.Clamp((int)Math.Floor(region.Top), 0, background.Height - 1);
        int x1 = Math.Clamp((int)Math.Ceiling(region.Right), x0 + 1, background.Width);
        int y1 = Math.Clamp((int)Math.Ceiling(region.Bottom), y0 + 1, background.Height);

        int stepX = Math.Max(1, (x1 - x0) / 12);
        int stepY = Math.Max(1, (y1 - y0) / 12);

        double sumLum = 0;
        int samples = 0;
        for (int y = y0; y < y1; y += stepY)
        {
            for (int x = x0; x < x1; x += stepX)
            {
                var px = background.GetPixel(x, y);
                sumLum += (0.2126 * px.Red + 0.7152 * px.Green + 0.0722 * px.Blue) / 255.0;
                samples++;
            }
        }

        if (samples == 0) return preferred;
        double bgLum = sumLum / samples;
        double prefLum = (0.2126 * preferred.Red + 0.7152 * preferred.Green + 0.0722 * preferred.Blue) / 255.0;
        double ratio = (Math.Max(bgLum, prefLum) + 0.05) / (Math.Min(bgLum, prefLum) + 0.05);

        if (ratio >= 4.5)
            return preferred;

        return bgLum > 0.5 ? new SKColor(18, 18, 26, 255) : SKColors.White;
    }

    public static string ExportToSettingTxt(DisplayTheme theme)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"name:{theme.Name}");
        sb.AppendLine(FormattableString.Invariant($"width:{Math.Round(theme.CanvasWidth)}"));
        sb.AppendLine(FormattableString.Invariant($"height:{Math.Round(theme.CanvasHeight)}"));
        sb.AppendLine($"DisplayDirection:{theme.DisplayDirection & 3}");

        foreach (var img in theme.ImageLayers.OrderBy(l => l.ZIndex))
        {
            string file = Path.GetFileName(img.FilePath);
            if (string.IsNullOrWhiteSpace(file)) continue;
            string wStr = img.Width.HasValue ? FormattableString.Invariant($",width@{img.Width.Value:0.##}") : "";
            string hStr = img.Height.HasValue ? FormattableString.Invariant($",height@{img.Height.Value:0.##}") : "";
            sb.AppendLine(FormattableString.Invariant($"{file}:x@{img.X:0.##},y@{img.Y:0.##},z@{img.ZIndex}{wStr}{hStr},IsBack@{img.IsBackground}"));
        }

        foreach (var bar in theme.BarWidgets.OrderBy(b => b.ZIndex))
        {
            string tag = bar.IsSegmented ? "GridLine" : "BorderLine";
            string rad = FormattableString.Invariant($"{bar.CornerRadius:0.##} {bar.CornerRadius:0.##} {bar.CornerRadius:0.##} {bar.CornerRadius:0.##}");
            string extra = bar.IsSegmented
                ? FormattableString.Invariant($",maxcount@{bar.SegmentCount},Margin@{bar.SegmentGap:0.##},Orientation@{(bar.ForceHorizontal ? "Horizontal" : "Vertical")}")
                : "";
            string border = bar.BorderThickness > 0 && bar.BorderColor != SKColor.Empty
                ? FormattableString.Invariant($",BorderThicknes@{bar.BorderThickness:0.##},BorderFill@{FormatHex(bar.BorderColor)}")
                : "";
            string back = bar.BackColor != SKColor.Empty ? $",BackColor@{FormatHex(bar.BackColor)}" : "";
            string data = bar.IsStaticFill ? "" : $",data@{bar.SensorKey}";
            sb.AppendLine(FormattableString.Invariant(
                $"{tag}:x@{bar.X:0.##},y@{bar.Y:0.##},z@{bar.ZIndex},maxwidth@{bar.MaxWidth:0.##},maxheight@{bar.MaxHeight:0.##},MinNum@{bar.MinNum:0.##},MaxNum@{bar.MaxNum:0.##},CornerRadius@{rad},Fill@{FormatHex(bar.FillColor)}{back}{border}{extra}{data}"));
        }

        foreach (var rad in theme.RadialWidgets.OrderBy(r => r.ZIndex))
        {
            float perimeter = rad.Radius * 2f + rad.Thickness;
            float rx = rad.Cx - perimeter * 0.5f;
            float ry = rad.Cy - perimeter * 0.5f;
            sb.AppendLine(FormattableString.Invariant(
                $"RingProgressBar:x@{rx:0.##},y@{ry:0.##},z@{rad.ZIndex},RingWidth@{rad.Thickness:0.##},Perimeter@{perimeter:0.##},MinValue@{rad.MinValue:0.##},MaxValue@{rad.MaxValue:0.##},IndexColor@{FormatHex(rad.FillColor)},BackColor@{FormatHex(rad.BackColor)},data@{rad.SensorKey}"));
        }

        foreach (var av in theme.AudioVisualizers.OrderBy(a => a.ZIndex))
        {
            sb.AppendLine(FormattableString.Invariant(
                $"AudioVisualizerControl:x@{av.X:0.##},y@{av.Y:0.##},z@{av.ZIndex},Width@{av.Width:0.##},Height@{av.Height:0.##},BandWidth@{av.BandWidth:0.##},Color@{FormatHex(av.PrimaryColor)},SeColor@{FormatHex(av.SecondaryColor)}"));
        }

        foreach (var art in theme.AlbumArts.OrderBy(a => a.ZIndex))
        {
            sb.AppendLine(FormattableString.Invariant(
                $"SongIcon:x@{art.X:0.##},y@{art.Y:0.##},z@{art.ZIndex},width@{art.Width:0.##},height@{art.Height:0.##},CornerRadius@{art.CornerRadius:0.##}"));
        }

        foreach (var txt in theme.TextWidgets.OrderBy(t => t.ZIndex))
        {
            int alignCode = txt.Align switch { TextAlignMode.Center => 1, TextAlignMode.Right => 2, _ => 0 };
            string titlePart = !string.IsNullOrEmpty(txt.Prefix) ? $",Title@{txt.Prefix.TrimEnd()}" : "";
            string dataPart = txt.IsStaticText ? "" : $",data@{txt.SensorKey}";
            string contentPart = txt.IsStaticText ? $",IsDefaultText@True,content@{txt.StaticContent ?? ""}" : "";
            string widthPart = txt.BoxWidth > 0 ? FormattableString.Invariant($",IsShowWidth@True,width@{txt.BoxWidth:0.##}") : "";
            string unitPart = !string.IsNullOrEmpty(txt.Unit) ? $",unit@{txt.Unit}" : "";
            string fontPart = !string.IsNullOrEmpty(txt.FontFamily) ? $",FontFamily@{txt.FontFamily}" : "";

            sb.AppendLine(FormattableString.Invariant(
                $"Text:x@{txt.X:0.##},y@{txt.Y:0.##},z@{txt.ZIndex}{titlePart},FontSize@{txt.FontSize:0.##}{fontPart},Foreground@{FormatHex(txt.Color)},TextAlignment@{alignCode}{widthPart}{unitPart}{dataPart}{contentPart}"));
        }

        return sb.ToString();
    }

    public static string ExportToFoxuJson(DisplayTheme theme)
    {
        var widgets = new List<object>();

        foreach (var img in theme.ImageLayers.OrderBy(l => l.ZIndex))
        {
            widgets.Add(new
            {
                type = "image",
                x = img.X,
                y = img.Y,
                z = img.ZIndex,
                width = img.Width ?? theme.CanvasWidth,
                height = img.Height ?? theme.CanvasHeight,
                src = Path.GetFileName(img.FilePath),
                isBackground = img.IsBackground
            });
        }

        foreach (var bar in theme.BarWidgets.OrderBy(b => b.ZIndex))
        {
            widgets.Add(new
            {
                type = bar.IsStaticFill ? "card" : "bar",
                x = bar.X,
                y = bar.Y,
                z = bar.ZIndex,
                width = bar.MaxWidth,
                height = bar.MaxHeight,
                max = bar.MaxNum,
                radius = bar.CornerRadius,
                color = FormatHex(bar.FillColor),
                backColor = bar.BackColor == SKColor.Empty ? "" : FormatHex(bar.BackColor),
                sensor = bar.SensorKey
            });
        }

        foreach (var rad in theme.RadialWidgets.OrderBy(r => r.ZIndex))
        {
            float side = rad.Radius * 2.2f;
            widgets.Add(new
            {
                type = "radial",
                x = rad.Cx - side * 0.5f,
                y = rad.Cy - side * 0.5f,
                z = rad.ZIndex,
                width = side,
                height = side,
                thickness = rad.Thickness,
                color = FormatHex(rad.FillColor),
                backColor = FormatHex(rad.BackColor),
                sensor = rad.SensorKey,
                showText = rad.ShowText
            });
        }

        foreach (var g in theme.GraphWidgets.OrderBy(w => w.ZIndex))
        {
            widgets.Add(new
            {
                type = "graph",
                x = g.X,
                y = g.Y,
                z = g.ZIndex,
                width = g.Width,
                height = g.Height,
                color = FormatHex(g.LineColor),
                sensor = g.SensorKey
            });
        }

        foreach (var av in theme.AudioVisualizers.OrderBy(a => a.ZIndex))
        {
            widgets.Add(new
            {
                type = "audiovisualizer",
                x = av.X,
                y = av.Y,
                z = av.ZIndex,
                width = av.Width,
                height = av.Height,
                color = FormatHex(av.PrimaryColor),
                backColor = FormatHex(av.SecondaryColor)
            });
        }

        foreach (var art in theme.AlbumArts.OrderBy(a => a.ZIndex))
        {
            widgets.Add(new
            {
                type = "albumart",
                x = art.X,
                y = art.Y,
                z = art.ZIndex,
                width = art.Width,
                height = art.Height,
                vinyl = art.IsVinylSpin
            });
        }

        foreach (var txt in theme.TextWidgets.OrderBy(t => t.ZIndex))
        {
            widgets.Add(new
            {
                type = "text",
                x = txt.X,
                y = txt.Y,
                z = txt.ZIndex,
                fontSize = txt.FontSize,
                fontFamily = txt.FontFamily,
                color = FormatHex(txt.Color),
                sensor = txt.IsStaticText ? "" : txt.SensorKey,
                prefix = txt.Prefix,
                unit = txt.Unit,
                text = txt.IsStaticText ? txt.StaticContent : null,
                align = txt.Align.ToString().ToLowerInvariant()
            });
        }

        var payload = new
        {
            format = "foxu-theme-v1",
            Name = theme.Name,
            Width = theme.CanvasWidth,
            Height = theme.CanvasHeight,
            DisplayDirection = theme.DisplayDirection,
            widgets
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void ExportToSmthemePackage(DisplayTheme theme, string outputSmthemePath)
    {
        string outDir = Path.GetDirectoryName(outputSmthemePath) ?? ".";
        Directory.CreateDirectory(outDir);
        string tmpZip = outputSmthemePath + ".tmp";
        if (File.Exists(tmpZip)) File.Delete(tmpZip);

        int cw = Math.Max(64, (int)Math.Round(theme.CanvasWidth));
        int ch = Math.Max(64, (int)Math.Round(theme.CanvasHeight));
        string ori = cw > ch ? "Horizontal" : (ch > cw ? "Vertical" : "Square");

        var manifest = new
        {
            Name = theme.Name,
            Version = "1.0",
            Resolution = $"{cw}x{ch}",
            Orientation = ori,
            Animated = theme.ImageLayers.Any(l => l.IsGif || l.IsVideo) || theme.FrameSequenceFiles.Count > 1,
            HasLiveSensors = theme.HasLiveSensors,
            Source = "studio",
            Created = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        byte[]? previewPng = null;
        try
        {
            string cacheDir = Path.Combine(Path.GetTempPath(), "FoxuExportPrev_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var renderer = new SkiaThemeRenderer(theme, cw, ch, targetFps: 10, customCacheDir: cacheDir);
                var sampleSnap = new SensorSnapshot
                {
                    CpuTemp = 54, CpuUsage = 42, CpuFrequencyMhz = 4600, CpuPowerW = 68,
                    GpuTemp = 61, GpuUsage = 68, GpuFrequencyMhz = 2450, GpuPowerW = 195, GpuMemoryLoad = 52,
                    MemoryUsagePct = 58, MemoryUsedGb = 18.4, MemoryFreeGb = 13.6, MemoryTotalGb = 32.0,
                    DiskUsagePct = 47, DiskCUsagePct = 55, DiskDUsagePct = 38,
                    NetDownKbps = 1250, NetUpKbps = 280, PingMs = 14,
                    HasWeather = true, WeatherDescription = "Clear", WeatherTempC = 22,
                    AudioPeak = 0.65f, SongTitle = "Cyber Synth", Artist = "Foxu",
                    AiClaudePct = 85, AiCodexPct = 72, AiGeminiPct = 94,
                    Now = new DateTime(2026, 11, 25, 14, 30, 0)
                };
                if (renderer.RenderIfDirty(sampleSnap, 100))
                    previewPng = renderer.CopyPreviewPngBytes(Math.Min(480, Math.Max(cw, ch)));
            }
            finally
            {
                try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch { }
            }
        }
        catch { }

        using (var zip = ZipFile.Open(tmpZip, ZipArchiveMode.Create))
        {
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddTextEntry(string entryName, string content)
            {
                if (!added.Add(entryName)) return;
                var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                using var sw = new StreamWriter(e.Open(), new UTF8Encoding(false));
                sw.Write(content);
            }

            void AddFileEntry(string entryName, string srcPath)
            {
                if (!File.Exists(srcPath) || !added.Add(entryName)) return;
                zip.CreateEntryFromFile(srcPath, entryName, CompressionLevel.Optimal);
            }

            AddTextEntry("manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            AddTextEntry("Setting.txt", ExportToSettingTxt(theme));
            AddTextEntry("theme.json", ExportToFoxuJson(theme));

            if (previewPng is { Length: > 0 })
            {
                added.Add("preview.png");
                var pe = zip.CreateEntry("preview.png", CompressionLevel.Optimal);
                using var ps = pe.Open();
                ps.Write(previewPng, 0, previewPng.Length);
            }

            foreach (var img in theme.ImageLayers)
                AddFileEntry(Path.GetFileName(img.FilePath), img.FilePath);
            foreach (var icon in theme.StatusIcons)
                AddFileEntry("source/" + Path.GetFileName(icon.FilePath), icon.FilePath);
            foreach (var font in theme.FontFiles)
                AddFileEntry("fonts/" + Path.GetFileName(font), font);
        }

        File.Move(tmpZip, outputSmthemePath, overwrite: true);
    }

    private static string FormatHex(SKColor c) =>
        $"#{c.Alpha:X2}{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
}
