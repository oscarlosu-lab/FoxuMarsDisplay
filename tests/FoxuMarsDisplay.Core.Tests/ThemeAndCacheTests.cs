using FoxuMarsDisplay.Core.Backends;
using FoxuMarsDisplay.Core.Rendering;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;
using SkiaSharp;
using Xunit;

namespace FoxuMarsDisplay.Core.Tests;

public sealed class ThemeAndCacheTests
{
    [Fact]
    public void UnifiedThemeLoader_LoadsSoeyiSettingTxt_AndRendersBlackAndPreview()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FoxuMarsThemeTest_" + Guid.NewGuid().ToString("N"));
        string cacheDir = Path.Combine(dir, "cache");
        Directory.CreateDirectory(dir);
        try
        {
            using (var bmp = new SKBitmap(240, 320, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                bmp.Erase(new SKColor(16, 24, 48, 255));
                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(dir, "back.png"), data.ToArray());
            }

            File.WriteAllText(Path.Combine(dir, "Setting.txt"), """
                name:TestCyber
                width:240
                height:320
                Text:x@20,y@40,FontSize@24,Foreground@#00E5FF,data@CpuTemp
                """);

            var theme = UnifiedThemeLoader.Load(dir);
            Assert.NotNull(theme);
            Assert.Equal(240f, theme!.CanvasWidth);
            Assert.Equal(320f, theme.CanvasHeight);
            Assert.Single(theme.TextWidgets);

            using var renderer = new SkiaThemeRenderer(theme, 240, 320, targetFps: 12, brightness: 100, rotationOverride: -1, customCacheDir: cacheDir);
            var snap = new SensorSnapshot { CpuTemp = 54.2f, Now = DateTime.Now };
            Assert.True(renderer.RenderIfDirty(snap, 100));
            Assert.False(renderer.RenderIfDirty(snap, 150)); // static dirty skip

            byte[]? png = renderer.CopyPreviewPngBytes(120);
            Assert.NotNull(png);
            Assert.True(png!.Length > 100);

            renderer.RenderBlackFrame();
            var px = renderer.OutputBitmap.GetPixel(10, 10);
            Assert.Equal(0, px.Red);
            Assert.Equal(0, px.Green);
            Assert.Equal(0, px.Blue);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UnifiedThemeLoader_LoadsTurzxThemeYaml()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FoxuMarsYamlTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var bmp = new SKBitmap(480, 1920, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                bmp.Erase(new SKColor(18, 22, 34, 255));
                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(Path.Combine(dir, "background.png"), data.ToArray());
            }

            File.WriteAllText(Path.Combine(dir, "theme.yaml"), """
                display:
                  DISPLAY_SIZE: 8.8"
                  DISPLAY_ORIENTATION: landscape
                static_images:
                  BACKGROUND:
                    PATH: background.png
                    X: 0
                    Y: 0
                    WIDTH: 480
                    HEIGHT: 1920
                static_text:
                  CPU:
                    TEXT: CPU
                    X: 344
                    Y: 1273
                    FONT_SIZE: 24
                    FONT_COLOR: 0, 0, 0
                    BACKGROUND_IMAGE: background.png
                STATS:
                  CPU:
                    PERCENTAGE:
                      TEXT:
                        SHOW: True
                        SHOW_UNIT: False
                        X: 326
                        Y: 1655
                        WIDTH: 100
                        HEIGHT: 44
                        ANCHOR: mm
                        FONT_SIZE: 40
                        FONT_COLOR: 255, 255, 255
                        BACKGROUND_IMAGE: background.png
                      GRAPH:
                        SHOW: True
                        X: 160
                        Y: 1270
                        WIDTH: 174
                        HEIGHT: 24
                        MIN_VALUE: 0
                        MAX_VALUE: 100
                        BAR_COLOR: 0, 0, 0
                        BACKGROUND_COLOR: 255, 255, 255
                    TEMPERATURE:
                      TEXT:
                        SHOW: True
                        SHOW_UNIT: False
                        X: 326
                        Y: 1597
                        WIDTH: 100
                        HEIGHT: 44
                        ANCHOR: mm
                        FONT_SIZE: 40
                        FONT_COLOR: 255, 255, 255
                        BACKGROUND_IMAGE: background.png
                  MEMORY:
                    VIRTUAL:
                      USED:
                        SHOW: True
                        SHOW_UNIT: True
                        X: 163
                        Y: 1435
                        FONT_SIZE: 30
                        FONT_COLOR: 255, 255, 255
                        BACKGROUND_IMAGE: background.png
                      PERCENT_TEXT:
                        SHOW: True
                        SHOW_UNIT: True
                        X: 145
                        Y: 1499
                        FONT_SIZE: 30
                        FONT_COLOR: 255, 255, 255
                        BACKGROUND_IMAGE: background.png
                """);

            var theme = UnifiedThemeLoader.Load(dir);
            Assert.NotNull(theme);
            Assert.Equal(480f, theme!.CanvasWidth);
            Assert.Equal(1920f, theme.CanvasHeight);
            Assert.Equal(0, theme.DisplayDirection);
            Assert.Single(theme.ImageLayers);
            Assert.Equal(5, theme.TextWidgets.Count);
            Assert.Single(theme.BarWidgets);
            Assert.True(theme.HasLiveSensors);
            Assert.Equal(string.Empty, theme.TextWidgets.First(t => t.SensorKey == "CpuUsage").Unit);

            using var renderer = new SkiaThemeRenderer(theme, 360, 960, scaleModeOverride: ThemeScaleMode.Fit);
            var snap1 = new SensorSnapshot { CpuTemp = 50f, CpuUsage = 20f, MemoryUsedGb = 8.5, MemoryUsagePct = 26f, Now = DateTime.Now };
            Assert.True(renderer.RenderIfDirty(snap1, 100));
            var snap2 = new SensorSnapshot { CpuTemp = 79f, CpuUsage = 92f, MemoryUsedGb = 24.0, MemoryUsagePct = 75f, Now = DateTime.Now };
            Assert.True(renderer.RenderIfDirty(snap2, 200));

            renderer.SetScaleMode(ThemeScaleMode.Stretch);
            Assert.Equal(ThemeScaleMode.Stretch, renderer.ScaleMode);
            Assert.True(renderer.RenderIfDirty(snap2, 300));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ThemeLibraryScanner_ClassifiesOrientations_AndDetectsThumbAndSize()
    {
        Assert.Equal(ThemeOrientation.Vertical, ThemeLibraryScanner.ClassifyOrientation(240, 320));
        Assert.Equal(ThemeOrientation.Vertical, ThemeLibraryScanner.ClassifyOrientation(360, 960));
        Assert.Equal(ThemeOrientation.Vertical, ThemeLibraryScanner.ClassifyOrientation(379, 960));
        Assert.Equal(ThemeOrientation.Horizontal, ThemeLibraryScanner.ClassifyOrientation(960, 360));
        Assert.Equal(ThemeOrientation.Horizontal, ThemeLibraryScanner.ClassifyOrientation(1024, 600));
        Assert.Equal(ThemeOrientation.Square, ThemeLibraryScanner.ClassifyOrientation(480, 480));
        Assert.Equal(ThemeOrientation.Unknown, ThemeLibraryScanner.ClassifyOrientation(0, 0));

        // Form-factor classification
        Assert.Equal(ThemeFormFactor.VerticalCompact, ThemeLibraryScanner.ClassifyFormFactor(240, 320));
        Assert.Equal(ThemeFormFactor.VerticalStrip, ThemeLibraryScanner.ClassifyFormFactor(360, 960));
        Assert.Equal(ThemeFormFactor.VerticalStrip, ThemeLibraryScanner.ClassifyFormFactor(320, 1480));
        Assert.Equal(ThemeFormFactor.HorizontalCompact, ThemeLibraryScanner.ClassifyFormFactor(320, 240));
        Assert.Equal(ThemeFormFactor.HorizontalCompact, ThemeLibraryScanner.ClassifyFormFactor(1024, 600));
        Assert.Equal(ThemeFormFactor.HorizontalStrip, ThemeLibraryScanner.ClassifyFormFactor(960, 360));
        Assert.Equal(ThemeFormFactor.HorizontalStrip, ThemeLibraryScanner.ClassifyFormFactor(1920, 462));
        Assert.Equal(ThemeFormFactor.Square, ThemeLibraryScanner.ClassifyFormFactor(480, 480));

        // Compatibility checks — universal same-orientation + near-square AIO compatibility
        Assert.Equal(ThemeCompatibility.Exact, ThemeLibraryScanner.EvaluateCompatibility(360, 960, 360, 960));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(320, 960, 360, 960));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(480, 1920, 360, 960)); // Cyberpunk 2077 Vertical
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(320, 1480, 360, 960));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(240, 320, 360, 960));
        Assert.Equal(ThemeCompatibility.Incompatible, ThemeLibraryScanner.EvaluateCompatibility(1920, 480, 360, 960));
        Assert.Equal(ThemeCompatibility.Incompatible, ThemeLibraryScanner.EvaluateCompatibility(0, 0, 360, 960));

        // 320x240 AIO accepts Horizontal & Square (e.g. ROGCode 480x480), rejects Vertical
        Assert.Equal(ThemeCompatibility.Exact, ThemeLibraryScanner.EvaluateCompatibility(320, 240, 320, 240));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(480, 320, 320, 240));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(480, 480, 320, 240));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(1280, 720, 320, 240));
        foreach (var (tw, th) in new[] { (240, 320), (360, 960), (480, 1920) })
            Assert.Equal(ThemeCompatibility.Incompatible, ThemeLibraryScanner.EvaluateCompatibility(tw, th, 320, 240));

        // 240x320 AIO accepts Vertical & Square (e.g. ROGCode 480x480), rejects Horizontal
        Assert.Equal(ThemeCompatibility.Exact, ThemeLibraryScanner.EvaluateCompatibility(240, 320, 240, 320));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(320, 480, 240, 320));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(480, 480, 240, 320));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(720, 1280, 240, 320));
        foreach (var (tw, th) in new[] { (320, 240), (800, 480) })
            Assert.Equal(ThemeCompatibility.Incompatible, ThemeLibraryScanner.EvaluateCompatibility(tw, th, 240, 320));

        // 480x480 Square AIO accepts Square & compact near-square AIO (240x320, 320x240)
        Assert.Equal(ThemeCompatibility.Exact, ThemeLibraryScanner.EvaluateCompatibility(480, 480, 480, 480));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(240, 320, 480, 480));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(320, 240, 480, 480));
        foreach (var (tw, th) in new[] { (360, 960), (1280, 720) })
            Assert.Equal(ThemeCompatibility.Incompatible, ThemeLibraryScanner.EvaluateCompatibility(tw, th, 480, 480));

        // Friendly TURZX name formatting
        Assert.Equal("TURZX #1", ThemeLibraryScanner.FormatFriendlyThemeName("TURZX_1_7201280", "TURZX"));
        Assert.Equal("Dragon Ball vertical", ThemeLibraryScanner.FormatFriendlyThemeName("TURZX_Dragon Ball_vertical_800480", "TURZX"));
        Assert.Equal("AMD", ThemeLibraryScanner.FormatFriendlyThemeName("TURZX_AMD_320960", "TURZX"));
        Assert.Equal("TURZX #24", ThemeLibraryScanner.FormatFriendlyThemeName("TURZX_24_480480r", "TURZX"));

        // Concatenated filename resolution extraction
        Assert.True(ThemeLibraryScanner.TryExtractResolution(null, "TURZX_1_7201280.smtheme", null, out int w1, out int h1));
        Assert.Equal((720, 1280), (w1, h1));
        Assert.True(ThemeLibraryScanner.TryExtractResolution(null, "TURZX_24_480480r.smtheme", null, out int w2, out int h2));
        Assert.Equal((480, 480), (w2, h2));
        Assert.True(ThemeLibraryScanner.TryExtractResolution(null, "TURZX_AMD_320960.smtheme", null, out int w4, out int h4));
        Assert.Equal((960, 320), (w4, h4));
        Assert.True(ThemeLibraryScanner.TryExtractResolution(null, "Example_0.96Inch_1.smtheme", null, out int w3, out int h3));
        Assert.Equal((160, 80), (w3, h3));

        string root = Path.Combine(Path.GetTempPath(), "FoxuMarsScanTest_" + Guid.NewGuid().ToString("N"));
        string themeDir = Path.Combine(root, "CyberSquare");
        Directory.CreateDirectory(themeDir);
        try
        {
            using (var bmp = new SKBitmap(480, 480, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                bmp.Erase(SKColors.DarkSlateBlue);
                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(Path.Combine(themeDir, "back.png"), data.ToArray());
            }

            var list = ThemeLibraryScanner.ScanAll([root]);
            var found = list.FirstOrDefault(x => x.Name == "CyberSquare");
            Assert.NotNull(found);
            Assert.Equal(480, found!.Width);
            Assert.Equal(480, found.Height);
            Assert.Equal(ThemeOrientation.Square, found.Orientation);
            Assert.False(string.IsNullOrEmpty(found.ThumbnailPath));

            // InspectThemePath on synthetic .smtheme (both manifest & background fallback)
            string smPath = Path.Combine(root, "TURZX_Demo_320960.smtheme");
            using (var zip = System.IO.Compression.ZipFile.Open(smPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var mf = zip.CreateEntry("manifest.json");
                using (var sw = new StreamWriter(mf.Open()))
                    sw.Write("{\"Resolution\":\"960x320\"}");
                var prev = zip.CreateEntry("preview.png");
                using var ps = prev.Open();
                byte[] pngBytes = File.ReadAllBytes(Path.Combine(themeDir, "back.png"));
                ps.Write(pngBytes, 0, pngBytes.Length);
            }
            var (swW, swH, swOri) = ThemeLibraryScanner.InspectThemePath(smPath);
            Assert.Equal((960, 320, ThemeOrientation.Horizontal), (swW, swH, swOri));

            string smNoManifest = Path.Combine(root, "CustomNoManifest.smtheme");
            using (var zip = System.IO.Compression.ZipFile.Open(smNoManifest, System.IO.Compression.ZipArchiveMode.Create))
            {
                var bg = zip.CreateEntry("background.png");
                using var bs = bg.Open();
                byte[] pngBytes = File.ReadAllBytes(Path.Combine(themeDir, "back.png"));
                bs.Write(pngBytes, 0, pngBytes.Length);
            }
            var (nmW, nmH, nmOri) = ThemeLibraryScanner.InspectThemePath(smNoManifest);
            Assert.Equal((480, 480, ThemeOrientation.Square), (nmW, nmH, nmOri));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuiltInDefaultTheme_RendersCleanlyForBothResolutions()
    {
        foreach (var (w, h) in new[] { (240, 320), (360, 960) })
        {
            var theme = UnifiedThemeLoader.CreateBuiltInDefaultTheme(w, h);
            Assert.Equal("FoxuCyberHUD", theme.Name);
            using var renderer = new SkiaThemeRenderer(theme, w, h);
            var snap = new SensorSnapshot { CpuTemp = 41.5f, CpuUsage = 18f, GpuTemp = 32f, GpuUsage = 10f, MemoryUsagePct = 40f, Now = DateTime.Now };
            Assert.True(renderer.RenderIfDirty(snap, 100));
            byte[]? png = renderer.CopyPreviewPngBytes(120);
            Assert.NotNull(png);
            Assert.True(png!.Length > 100);
        }
    }

    [Fact]
    public void MsDisplayBackend_ResolveHardwareRotationOffset_ClassifiesPanels()
    {
        Assert.Equal(2, MsDisplayBackend.ResolveHardwareRotationOffset("VMAXC230360*0960S00000001", 360, 960));
        Assert.Equal(2, MsDisplayBackend.ResolveHardwareRotationOffset("VMAXC230462*1920S00000002", 462, 1920));
        Assert.Equal(0, MsDisplayBackend.ResolveHardwareRotationOffset("VMAXA180240*0320S00000003", 240, 320));
        Assert.Equal(0, MsDisplayBackend.ResolveHardwareRotationOffset("VMAXC230960*0360S00000004", 960, 360));
        Assert.Equal(0, MsDisplayBackend.ResolveHardwareRotationOffset("AIC_0001", 480, 480));
    }

    [Fact]
    public void SkiaThemeRenderer_HardwareRotationOffset180_FlipsOutputAndKeepsPreviewUpright()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FoxuMarsRotTest_" + Guid.NewGuid().ToString("N"));
        string cacheDir = Path.Combine(dir, "cache");
        Directory.CreateDirectory(dir);
        try
        {
            using (var bmp = new SKBitmap(60, 120, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                using var c = new SKCanvas(bmp);
                using var redPaint = new SKPaint { Color = SKColors.Red };
                using var bluePaint = new SKPaint { Color = SKColors.Blue };
                c.DrawRect(0, 0, 60, 60, redPaint);
                c.DrawRect(0, 60, 60, 60, bluePaint);

                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(dir, "back.png"), data.ToArray());
            }

            File.WriteAllText(Path.Combine(dir, "Setting.txt"), """
                name:SplitOrient
                width:60
                height:120
                """);

            var theme = UnifiedThemeLoader.Load(dir);
            var snap = new SensorSnapshot { Now = DateTime.Now };

            // 1. Offset 0, user rotation 0 => Output top RED, bottom BLUE
            using (var r0 = new SkiaThemeRenderer(theme, 60, 120, rotationOverride: 0, customCacheDir: cacheDir, hardwareRotationOffset: 0))
            {
                Assert.True(r0.RenderIfDirty(snap, 100));
                Assert.Equal(SKColors.Red, r0.OutputBitmap.GetPixel(30, 15));
                Assert.Equal(SKColors.Blue, r0.OutputBitmap.GetPixel(30, 105));
            }

            // 2. Offset 2 (180° VMAXC), user rotation 0 => Output top BLUE, bottom RED; Preview stays upright (top RED, bottom BLUE)
            using (var r180 = new SkiaThemeRenderer(theme, 60, 120, rotationOverride: 0, customCacheDir: cacheDir, hardwareRotationOffset: 2))
            {
                Assert.Equal(0, r180.EffectiveRotation);
                Assert.Equal(2, r180.PhysicalRotation);
                Assert.True(r180.RenderIfDirty(snap, 100));
                Assert.Equal(SKColors.Blue, r180.OutputBitmap.GetPixel(30, 15));
                Assert.Equal(SKColors.Red, r180.OutputBitmap.GetPixel(30, 105));

                byte[]? previewBytes = r180.CopyPreviewPngBytes(120);
                Assert.NotNull(previewBytes);
                using var previewBmp = SKBitmap.Decode(previewBytes!);
                Assert.Equal(60, previewBmp.Width);
                Assert.Equal(120, previewBmp.Height);
                Assert.Equal(SKColors.Red, previewBmp.GetPixel(30, 15));
                Assert.Equal(SKColors.Blue, previewBmp.GetPixel(30, 105));
            }

            // 3. Offset 2, user rotation 2 => PhysicalRotation 0 => Output top RED
            using (var rDouble = new SkiaThemeRenderer(theme, 60, 120, rotationOverride: 2, customCacheDir: cacheDir, hardwareRotationOffset: 2))
            {
                Assert.Equal(0, rDouble.PhysicalRotation);
                Assert.True(rDouble.RenderIfDirty(snap, 100));
                Assert.Equal(SKColors.Red, rDouble.OutputBitmap.GetPixel(30, 15));
            }

            // 4. AIO 270° (offset 0, user rotation 3) => Output left RED, right BLUE; Preview horizontal (120x60) with top RED
            using (var r270 = new SkiaThemeRenderer(theme, 60, 120, rotationOverride: 3, customCacheDir: cacheDir, hardwareRotationOffset: 0))
            {
                Assert.Equal(3, r270.PhysicalRotation);
                Assert.True(r270.RenderIfDirty(snap, 100));
                Assert.Equal(SKColors.Red, r270.OutputBitmap.GetPixel(15, 60));
                Assert.Equal(SKColors.Blue, r270.OutputBitmap.GetPixel(45, 60));

                byte[]? previewBytes = r270.CopyPreviewPngBytes(120);
                Assert.NotNull(previewBytes);
                using var previewBmp = SKBitmap.Decode(previewBytes!);
                Assert.Equal(120, previewBmp.Width);
                Assert.Equal(60, previewBmp.Height);
                Assert.Equal(SKColors.Red, previewBmp.GetPixel(60, 10));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UnifiedThemeLoader_ParsesTurzxThemeYamlV2V3_AllWidgetsAndRgbColors()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FoxuTurzxV3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "theme.yaml"), """
                display:
                  DISPLAY_WIDTH: 480
                  DISPLAY_HEIGHT: 1920
                  DISPLAY_ORIENTATION: portrait
                static_text:
                  TITLE:
                    SHOW: true
                    X: 240
                    Y: 20
                    FONT_SIZE: 30
                    FONT_COLOR: 255, 100, 25
                    ALIGN: center
                  HIDDEN_LABEL:
                    SHOW: false
                    X: 10
                    Y: 10
                    TEXT: Hidden
                STATS:
                  CPU:
                    TEMPERATURE:
                      TEXT:
                        SHOW: true
                        X: 40
                        Y: 120
                        FONT_SIZE: 36
                        FONT_COLOR: 255, 255, 255
                        UNIT: "°C"
                    PERCENTAGE:
                      RADIAL:
                        SHOW: true
                        CX: 120
                        CY: 300
                        RADIUS: 60
                        BAR_WIDTH: 14
                        MIN_VALUE: 0
                        MAX_VALUE: 100
                        BAR_COLOR: 255, 31, 75
                        BACKGROUND_COLOR: 38, 31, 51
                      BAR:
                        SHOW: true
                        X: 40
                        Y: 420
                        WIDTH: 300
                        HEIGHT: 18
                        BAR_COLOR: 255, 184, 32
                      LINE_GRAPH:
                        SHOW: true
                        X: 40
                        Y: 500
                        WIDTH: 400
                        HEIGHT: 90
                        LINE_COLOR: 255, 100, 25
                        AXIS_X_RANGE: 30
                """);

            // Add static text content on TITLE
            string yaml = File.ReadAllText(Path.Combine(dir, "theme.yaml"))
                .Replace("ALIGN: center", "ALIGN: center\n    TEXT: FOXI TURZX");
            File.WriteAllText(Path.Combine(dir, "theme.yaml"), yaml);

            var theme = UnifiedThemeLoader.Load(dir);
            Assert.Equal(480f, theme.CanvasWidth);
            Assert.Equal(1920f, theme.CanvasHeight);
            Assert.Equal(2, theme.TextWidgets.Count); // TITLE + CPU.TEMP (HIDDEN_LABEL skipped)
            Assert.Single(theme.RadialWidgets);
            Assert.Single(theme.BarWidgets);
            Assert.Single(theme.GraphWidgets);
            Assert.Equal(TextAlignMode.Center, theme.TextWidgets[0].Align);
            Assert.Equal(new SKColor(255, 100, 25, 255), theme.TextWidgets[0].Color);
            Assert.Equal("CpuUsage", theme.RadialWidgets[0].SensorKey);
            Assert.Equal(30, theme.GraphWidgets[0].MaxSamples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UnifiedThemeLoader_ParsesSnakeMarsTheme_GridLineDefaultLineAndType0Json()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FoxuSmtTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var bmp = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                bmp.Erase(SKColors.Orange);
                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(Path.Combine(dir, "badge.png"), data.ToArray());
            }

            File.WriteAllText(Path.Combine(dir, "Setting.txt"), """
                name:ThemeMakerPro
                width:240
                height:320
                DisplayDirection:0
                Text:x@10,y@20,z@1,FontSize@18,Foreground@#FFB820,data@CPUTempPU,unit@°C
                DefaultLine:x@10,y@60,z@1,maxwidth@180,maxheight@10,Fill@#FF6419,data@GpuUsagePU
                GridLine:x@10,y@90,z@1,maxwidth@180,maxheight@12,Fill@#FF1F4B,data@MemoryUsagePU
                """);

            File.WriteAllText(Path.Combine(dir, "ThemeMakerPro.json"), """
                {
                  "Type": 0,
                  "Name": "ThemeMakerPro",
                  "Width": 240,
                  "Height": 320,
                  "DisplayDirection": 0,
                  "DisplayTexts": [],
                  "DisplayImages": [
                    { "ImagePath": "badge.png", "Left": 190, "Top": 10, "Width": 32, "Height": 32, "ZIndex": 2 }
                  ]
                }
                """);

            var theme = UnifiedThemeLoader.Load(dir);
            Assert.Equal(2, theme.BarWidgets.Count);
            Assert.False(theme.BarWidgets[0].IsSegmented);
            Assert.True(theme.BarWidgets[1].IsSegmented);
            Assert.Contains(theme.ImageLayers, l => Path.GetFileName(l.FilePath) == "badge.png");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SkiaThemeRenderer_RendersRadialGraphGridLineAndAspectFit_Cleanly()
    {
        var theme = new DisplayTheme
        {
            Name = "TurzxAspect",
            CanvasWidth = 480f,
            CanvasHeight = 1920f,
            ScaleMode = ThemeScaleMode.Auto
        };
        theme.RadialWidgets.Add(new RadialWidgetSpec
        {
            Cx = 240, Cy = 400, Radius = 140, Thickness = 24,
            FillColor = new SKColor(255, 100, 25), BackColor = new SKColor(40, 35, 55),
            SensorKey = "CpuUsage", ShowText = false
        });
        theme.BarWidgets.Add(new BarWidgetSpec
        {
            X = 60, Y = 800, MaxWidth = 360, MaxHeight = 40,
            FillColor = new SKColor(255, 31, 75), SensorKey = "GpuUsage", IsSegmented = true
        });
        theme.GraphWidgets.Add(new GraphWidgetSpec
        {
            X = 60, Y = 1100, Width = 360, Height = 200,
            LineColor = new SKColor(255, 184, 32), FillColor = new SKColor(255, 184, 32, 90),
            SensorKey = "MemoryUsage"
        });

        // Target is 240x320 (480x1920 aspect = 0.25 vs 0.75 => ratio 3.0 > 1.22 => Auto AspectFit letterbox!)
        using var renderer = new SkiaThemeRenderer(theme, 240, 320);
        var snap = new SensorSnapshot { CpuUsage = 75, GpuUsage = 80, MemoryUsagePct = 55, Now = DateTime.Now };
        Assert.True(renderer.RenderIfDirty(snap, 100));
        // Second sample for line graph
        snap = new SensorSnapshot { CpuUsage = 76, GpuUsage = 80, MemoryUsagePct = 65, Now = DateTime.Now };
        Assert.True(renderer.RenderIfDirty(snap, 1200));

        // Left letterbox edge at x=5, y=160 must remain pure black
        Assert.Equal(new SKColor(0, 0, 0, 255), renderer.OutputBitmap.GetPixel(5, 160));

        // Center area must contain non-black rendered pixels from widgets
        bool hasColored = false;
        for (int y = 20; y < 300 && !hasColored; y += 4)
        {
            for (int x = 85; x < 155; x += 4)
            {
                var px = renderer.OutputBitmap.GetPixel(x, y);
                if (px.Red > 30 || px.Green > 30 || px.Blue > 30)
                {
                    hasColored = true;
                    break;
                }
            }
        }
        Assert.True(hasColored);
    }

    [Fact]
    public void SensorSnapshot_SupportsAllSnakeMarsThemeAndTurzxTokens()
    {
        var snap = new SensorSnapshot
        {
            CpuTemp = 54,
            GpuUsage = 82,
            GpuMemoryFreqMhz = 7000,
            GpuMemoryUsedGb = 4.5,
            MemoryFreeGb = 18.2,
            DiskFreeGb = 210.4,
            DiskUsagePct = 62,
            NetUpKbps = 120,
            NetDownKbps = 840
        };

        Assert.Equal("54°C", snap.FormatValue("CPUTempPU", "°C"));
        Assert.Equal("82%", snap.FormatValue("GpuUsagePU", "%"));
        Assert.Equal("7000MHz", snap.FormatValue("GPUMemoryFrequencyPU", "MHz"));
        Assert.Equal("4.5GB", snap.FormatValue("GPUMemoryUsed", "GB"));
        Assert.Equal("18.2GB", snap.FormatValue("MemoryFree", "GB"));
        Assert.Equal("210.4GB", snap.FormatValue("DiskFree", "GB"));
        Assert.Equal("62%", snap.FormatValue("DiskUsage", "%"));
        Assert.Equal("120KB/s", snap.FormatValue("UpNetSpeed", "KB/s"));
        Assert.Equal("840KB/s", snap.FormatValue("DownNetSpeed", "KB/s"));
    }

    [Fact]
    public void UnifiedThemeLoader_ParsesAllSoeyiWidgetsAndYamlMemoryFixes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FoxuParity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string srcDir = Path.Combine(dir, "source");
        Directory.CreateDirectory(srcDir);

        try
        {
            using (var ico = new SKBitmap(24, 24, SKColorType.Bgra8888, SKAlphaType.Premul))
            {
                ico.Erase(SKColors.Lime);
                using var img = SKImage.FromBitmap(ico);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(srcDir, "NumLockBool.png"), data.ToArray());
            }

            File.WriteAllText(Path.Combine(dir, "Setting.txt"), """
                name:ParityTest
                width:360
                height:960
                BorderLine:x@20,y@40,z@2,maxheight@50,maxwidth@320,CornerRadius@10 10 10 10,Fill@#80161616,BorderThicknes@2,BorderFill@#FFFFFFFF
                GridLine:x@20,y@110,z@3,maxheight@20,maxwidth@300,maxcount@6,Margin@7,Orientation@Horizontal,Fill@#FF00FFAA,data@CpuUsage
                RingProgressBar:x@30,y@160,z@4,RingWidth@12,Perimeter@100,MaxValue@100,IndexColor@#FFFF0036,BackColor@#80FFFFFF,data@GpuUsage
                Text:x@30,y@300,z@5,Title@CPU TEMP,FontSize@22,Foreground@#FFFFFF,data@CPUTemp,unit@°C,TextAlignment@1,IsShowWidth@True,width@260
                IsEnableItem:x@30,y@360,z@6,width@24,height@24,Source@NumLockBool.png,Endata@NumLockBool
                AudioVisualizerControl:x@30,y@420,z@7,Width@280,Height@60,BandWidth@8,Color@#FF00FFFF,SeColor@#FFFF00FF
                SonglyricsControl:x@30,y@500,z@8,width@300,height@40,FontSize@16,FontColor@#FFFFFF
                SongIcon:x@30,y@560,z@8,width@64,height@64,CornerRadius@12 12 12 12
                """);

            var theme = UnifiedThemeLoader.Load(dir);
            Assert.Equal(2, theme.BarWidgets.Count);
            Assert.True(theme.BarWidgets[0].IsStaticFill);
            Assert.Equal(10f, theme.BarWidgets[0].CornerRadius);
            Assert.Equal(2f, theme.BarWidgets[0].BorderThickness);
            Assert.True(theme.BarWidgets[1].IsSegmented);
            Assert.Equal(6, theme.BarWidgets[1].SegmentCount);
            Assert.Equal(7f, theme.BarWidgets[1].SegmentGap);
            Assert.True(theme.BarWidgets[1].ForceHorizontal);

            Assert.Single(theme.RadialWidgets);
            Assert.Equal(80f, theme.RadialWidgets[0].Cx);
            Assert.Equal(210f, theme.RadialWidgets[0].Cy);
            Assert.Equal(44f, theme.RadialWidgets[0].Radius);

            Assert.Equal(2, theme.TextWidgets.Count);
            Assert.Equal("CPU TEMP ", theme.TextWidgets[0].Prefix);
            Assert.Equal(TextAlignMode.Center, theme.TextWidgets[0].Align);
            Assert.Equal(260f, theme.TextWidgets[0].BoxWidth);
            Assert.Equal("SongLyric", theme.TextWidgets[1].SensorKey);

            Assert.Single(theme.StatusIcons);
            Assert.Single(theme.AudioVisualizers);
            Assert.Single(theme.AlbumArts);
            Assert.True(theme.HasLiveSensors);

            using var renderer = new SkiaThemeRenderer(theme, 360, 960, customCacheDir: Path.Combine(dir, "cache"));
            var snapA = new SensorSnapshot
            {
                CpuTemp = 45, CpuUsage = 20, GpuUsage = 35, MemoryUsagePct = 52, MemoryUsedGb = 16.4,
                DiskCUsagePct = 61, DiskDUsagePct = 44, Fan2Rpm = 1200, VolumePct = 75, PingMs = 14,
                HasWeather = true, WeatherDescription = "Clear", WeatherTempC = 22, WeatherFeelsLikeC = 21,
                WeatherHumidityPct = 55, WeatherHighC = 24, WeatherLowC = 14,
                NumLockOn = true, AudioPeak = 0.0f, SongTitle = "Track A", Artist = "Artist A"
            };
            var snapB = new SensorSnapshot
            {
                CpuTemp = 78, CpuUsage = 85, GpuUsage = 92, MemoryUsagePct = 77, MemoryUsedGb = 24.5,
                DiskCUsagePct = 61, DiskDUsagePct = 44, Fan2Rpm = 1800, VolumePct = 80, PingMs = 19,
                HasWeather = true, WeatherDescription = "Rain", WeatherTempC = 18, WeatherFeelsLikeC = 17,
                WeatherHumidityPct = 80, WeatherHighC = 20, WeatherLowC = 12,
                NumLockOn = false, AudioPeak = 0.82f, SongTitle = "Track B", Artist = "Artist B"
            };

            Assert.Equal("52%", snapA.FormatValue("MemoryUseInt", "%"));
            Assert.Equal("16.4GB", snapA.FormatValue("MemoryUsed", "GB"));
            Assert.Equal("61%", snapA.FormatValue("DiskCUsed", "%"));
            Assert.Equal("44%", snapA.FormatValue("DiskDUsed", "%"));
            Assert.Equal("Clear", snapA.FormatValue("WEATHER_DESCRIPTION", ""));
            Assert.Equal("21°C", snapA.FormatValue("TEMPERATURE_FELT", "°C"));
            Assert.True(snapA.GetBoolState("NumLockBool"));
            Assert.False(snapB.GetBoolState("NumLockBool"));

            Assert.True(renderer.RenderIfDirty(snapA, 100));
            byte[]? png1 = renderer.CopyPreviewPngBytes(360);
            Assert.True(renderer.RenderIfDirty(snapB, 500));
            byte[]? png2 = renderer.CopyPreviewPngBytes(360);
            Assert.NotNull(png1);
            Assert.NotNull(png2);
            Assert.False(png1!.SequenceEqual(png2!));

            // Also verify YAML MEMORY.VIRTUAL / MEMORY.VIRTUAL.USED / GPU.MEMORY / WEATHER mappings
            string yamlDir = Path.Combine(dir, "yaml");
            Directory.CreateDirectory(yamlDir);
            File.WriteAllText(Path.Combine(yamlDir, "theme.yaml"), """
                display:
                  DISPLAY_WIDTH: 480
                  DISPLAY_HEIGHT: 320
                STATS:
                  MEMORY:
                    VIRTUAL:
                      GRAPH:
                        SHOW: True
                        X: 10
                        Y: 10
                        WIDTH: 100
                        HEIGHT: 12
                      USED:
                        TEXT:
                          SHOW: True
                          X: 10
                          Y: 30
                  GPU:
                    MEMORY:
                      TEXT:
                        SHOW: True
                        X: 10
                        Y: 60
                  WEATHER:
                    WEATHER_DESCRIPTION:
                      TEXT:
                        SHOW: True
                        X: 10
                        Y: 90
                """);
            var yTheme = UnifiedThemeLoader.Load(yamlDir);
            Assert.Single(yTheme.BarWidgets);
            Assert.Equal("MemoryUsage", yTheme.BarWidgets[0].SensorKey);
            Assert.Equal("MemoryUsed", yTheme.TextWidgets[0].SensorKey);
            Assert.Equal("GB", yTheme.TextWidgets[0].Unit);
            Assert.Equal("GPUMemoryLoad", yTheme.TextWidgets[1].SensorKey);
            Assert.Equal("WEATHER_DESCRIPTION", yTheme.TextWidgets[2].SensorKey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MediaFoundationVideoDecoder_DecodesRealMp4_WhenEnvVarProvided()
    {
        string? mp4Path = Environment.GetEnvironmentVariable("FOXU_TEST_MP4");
        if (string.IsNullOrWhiteSpace(mp4Path) || !File.Exists(mp4Path))
            return;

        var frames = new List<(byte[] Bytes, int DurationMs)>();
        bool ok = MediaFoundationVideoDecoder.TryDecodeToFrames(
            mp4Path, 180, 480, 0, 0, 180, 480, 10, 15,
            (bytes, dur) => frames.Add((bytes, dur)));

        Assert.True(ok, MediaFoundationVideoDecoder.LastError ?? "TryDecodeToFrames returned false");
        Assert.True(frames.Count > 1);
        using var bmp = SKBitmap.Decode(frames[0].Bytes);
        Assert.NotNull(bmp);
        Assert.Equal(180, bmp!.Width);
        Assert.Equal(480, bmp.Height);

        bool hasNonBlack = false;
        for (int y = 0; y < bmp.Height && !hasNonBlack; y += 12)
        {
            for (int x = 0; x < bmp.Width; x += 12)
            {
                var px = bmp.GetPixel(x, y);
                if (px.Red > 5 || px.Green > 5 || px.Blue > 5)
                {
                    hasNonBlack = true;
                    break;
                }
            }
        }
        Assert.True(hasNonBlack);
    }
}
