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
            File.WriteAllText(Path.Combine(dir, "theme.yaml"), """
                display:
                  DISPLAY_WIDTH: 360
                  DISPLAY_HEIGHT: 960
                STATS:
                  CPU:
                    TEMPERATURE:
                      TEXT:
                        SHOW: true
                        X: 50
                        Y: 100
                        FONT_SIZE: 28
                        FONT_COLOR: 255, 255, 255
                """);

            var theme = UnifiedThemeLoader.Load(dir);
            Assert.NotNull(theme);
            Assert.Equal(360f, theme!.CanvasWidth);
            Assert.Equal(960f, theme.CanvasHeight);
            Assert.True(theme.TextWidgets.Count >= 1);
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

        // Compatibility checks
        Assert.Equal(ThemeCompatibility.Exact, ThemeLibraryScanner.EvaluateCompatibility(360, 960, 360, 960));
        Assert.Equal(ThemeCompatibility.Proportional, ThemeLibraryScanner.EvaluateCompatibility(320, 960, 360, 960));
        Assert.Equal(ThemeCompatibility.Incompatible, ThemeLibraryScanner.EvaluateCompatibility(240, 320, 360, 960));
        Assert.Equal(ThemeCompatibility.Incompatible, ThemeLibraryScanner.EvaluateCompatibility(0, 0, 360, 960));

        // Concatenated filename resolution extraction
        Assert.True(ThemeLibraryScanner.TryExtractResolution(null, "TURZX_1_7201280.smtheme", null, out int w1, out int h1));
        Assert.Equal((720, 1280), (w1, h1));
        Assert.True(ThemeLibraryScanner.TryExtractResolution(null, "TURZX_24_480480r.smtheme", null, out int w2, out int h2));
        Assert.Equal((480, 480), (w2, h2));
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
}
