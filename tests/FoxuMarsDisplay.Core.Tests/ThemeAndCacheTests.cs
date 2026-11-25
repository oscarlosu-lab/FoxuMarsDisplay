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
                File.WriteAllBytes(Path.Combine(dir, "bg.png"), data.ToArray());
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
}
