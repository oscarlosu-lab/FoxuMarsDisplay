using FoxuMarsDisplay.Core.Backends;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Core.Rendering;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;
using SkiaSharp;
using Xunit;

namespace FoxuMarsDisplay.Core.Tests;

public sealed class StudioAndWeatherV21Tests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FoxuStudioWeatherTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Config_WeatherLocation_RoundTrip_PreservesManualAndNullDefaults()
    {
        string dir = CreateTempDir();
        try
        {
            string cfgPath = Path.Combine(dir, "config.json");
            var store = new FoxuDisplayConfigStore(cfgPath);

            var cfg = new FoxuDisplayConfig
            {
                WeatherCity = "Gijón, Asturias, Spain",
                WeatherLatitude = 43.53573,
                WeatherLongitude = -5.66152
            };
            store.Save(cfg);

            var reloaded = store.LoadOrImportSoeyi();
            Assert.Equal("Gijón, Asturias, Spain", reloaded.WeatherCity);
            Assert.Equal(43.53573, reloaded.WeatherLatitude!.Value, precision: 5);
            Assert.Equal(-5.66152, reloaded.WeatherLongitude!.Value, precision: 5);

            reloaded.WeatherCity = string.Empty;
            reloaded.WeatherLatitude = null;
            reloaded.WeatherLongitude = null;
            store.Save(reloaded);

            var cleared = store.LoadOrImportSoeyi();
            Assert.Equal(string.Empty, cleared.WeatherCity);
            Assert.Null(cleared.WeatherLatitude);
            Assert.Null(cleared.WeatherLongitude);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void WeatherProvider_ConfigureLocation_And_SnapshotWeatherCity_Work()
    {
        using var provider = new WeatherAndNetProvider();
        provider.ConfigureLocation("Avilés, Asturias, Spain", 43.5547, -5.9248);

        Assert.True(provider.IsManualLocation);
        Assert.Equal("Avilés, Asturias, Spain", provider.WeatherCity);
        Assert.Equal(43.5547, provider.Latitude, precision: 4);
        Assert.Equal(-5.9248, provider.Longitude, precision: 4);

        var snap = new SensorSnapshot { WeatherCity = provider.WeatherCity };
        Assert.Equal("Avilés, Asturias, Spain", snap.FormatValue("WEATHER_CITY", ""));
        Assert.Equal("--", new SensorSnapshot().FormatValue("WEATHER_CITY", ""));

        provider.ConfigureLocation(null, null, null);
        Assert.False(provider.IsManualLocation);
        Assert.Equal(string.Empty, provider.WeatherCity);
        Assert.False(provider.HasWeather);
    }

    [Fact]
    public void DisplayHost_SetWeatherLocation_PersistsToConfig()
    {
        string dir = CreateTempDir();
        try
        {
            string cfgPath = Path.Combine(dir, "config.json");
            var store = new FoxuDisplayConfigStore(cfgPath);
            using var host = new DisplayHost(
                backend: new FakeBackend(),
                sensors: new LightweightSensorProvider(),
                configStore: store,
                config: new FoxuDisplayConfig(),
                customCacheDir: dir);

            host.SetWeatherLocation("Oviedo, Asturias, Spain", 43.36029, -5.84476);
            Assert.Equal("Oviedo, Asturias, Spain", host.Config.WeatherCity);
            Assert.Equal(43.36029, host.Config.WeatherLatitude!.Value, precision: 5);
            Assert.Equal(-5.84476, host.Config.WeatherLongitude!.Value, precision: 5);

            var persisted = store.LoadOrImportSoeyi();
            Assert.Equal("Oviedo, Asturias, Spain", persisted.WeatherCity);
            Assert.Equal(43.36029, persisted.WeatherLatitude!.Value, precision: 5);

            host.SetWeatherLocation(null, null, null);
            var cleared = store.LoadOrImportSoeyi();
            Assert.Equal(string.Empty, cleared.WeatherCity);
            Assert.Null(cleared.WeatherLatitude);
            Assert.Null(cleared.WeatherLongitude);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ThemeExporter_And_UnifiedThemeLoader_RoundTrip_RichTextAndAlignment()
    {
        string dir = CreateTempDir();
        try
        {
            var theme = new DisplayTheme
            {
                Name = "RichTextStudioTest",
                CanvasWidth = 360,
                CanvasHeight = 960
            };
            theme.TextWidgets.Add(new TextWidgetSpec
            {
                X = 0,
                Y = 120,
                ZIndex = 4,
                FontSize = 32,
                FontFamily = "Consolas",
                Color = SKColors.OrangeRed,
                IsStaticText = true,
                StaticContent = "FOXU LABS",
                Align = TextAlignMode.Center,
                IsBold = true,
                IsItalic = true,
                IsUnderline = true,
                IsStrikeout = true,
                HasShadow = true,
                BoxWidth = 360,
                BoxHeight = 42,
                Anchor = "ma"
            });

            string smthemePath = Path.Combine(dir, "RichTextStudioTest.smtheme");
            ThemeExporter.ExportToSmthemePackage(theme, smthemePath);
            Assert.True(File.Exists(smthemePath));

            var loadedSm = UnifiedThemeLoader.Load(smthemePath);
            Assert.NotNull(loadedSm);
            var t1 = Assert.Single(loadedSm!.TextWidgets);
            Assert.Equal("Consolas", t1.FontFamily);
            Assert.Equal(TextAlignMode.Center, t1.Align);
            Assert.True(t1.IsBold);
            Assert.True(t1.IsItalic);
            Assert.True(t1.IsUnderline);
            Assert.True(t1.IsStrikeout);
            Assert.True(t1.HasShadow);
            Assert.Equal(360f, t1.BoxWidth);
            Assert.Equal("FOXU LABS", t1.StaticContent);

            string jsonPath = Path.Combine(dir, "theme.json");
            File.WriteAllText(jsonPath, ThemeExporter.ExportToFoxuJson(theme));
            var loadedJson = UnifiedThemeLoader.Load(jsonPath);
            Assert.NotNull(loadedJson);
            var t2 = Assert.Single(loadedJson!.TextWidgets);
            Assert.Equal("Consolas", t2.FontFamily);
            Assert.Equal(TextAlignMode.Center, t2.Align);
            Assert.True(t2.IsBold);
            Assert.True(t2.IsItalic);
            Assert.True(t2.IsUnderline);
            Assert.True(t2.IsStrikeout);
            Assert.True(t2.HasShadow);
            Assert.Equal(360f, t2.BoxWidth);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void SkiaThemeRenderer_TextAlignment_ProducesDistinctHorizontalCentroids()
    {
        float RenderCentroid(TextAlignMode align, string anchor)
        {
            var theme = new DisplayTheme { Name = "AlignTest", CanvasWidth = 360, CanvasHeight = 120 };
            theme.TextWidgets.Add(new TextWidgetSpec
            {
                X = 0,
                Y = 30,
                ZIndex = 1,
                FontSize = 28,
                FontFamily = "Segoe UI",
                Color = SKColors.White,
                IsStaticText = true,
                StaticContent = "FOXU",
                Align = align,
                BoxWidth = 360,
                BoxHeight = 40,
                Anchor = anchor
            });

            using var renderer = new SkiaThemeRenderer(theme, 360, 120, customCacheDir: Path.GetTempPath());
            Assert.True(renderer.RenderIfDirty(new SensorSnapshot(), 100));

            double sumX = 0;
            long count = 0;
            var bmp = renderer.OutputBitmap;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y).Red > 30)
                    {
                        sumX += x;
                        count++;
                    }
                }
            }
            Assert.True(count > 20);
            return (float)(sumX / count);
        }

        float leftX = RenderCentroid(TextAlignMode.Left, "la");
        float centerX = RenderCentroid(TextAlignMode.Center, "ma");
        float rightX = RenderCentroid(TextAlignMode.Right, "ra");

        Assert.True(leftX < centerX - 40f, $"Expected leftX ({leftX}) < centerX - 40 ({centerX - 40f})");
        Assert.True(centerX < rightX - 40f, $"Expected centerX ({centerX}) < rightX - 40 ({rightX - 40f})");
    }

    [Fact]
    public void SkiaThemeRenderer_RichTextStyles_AlterRenderedPixels()
    {
        byte[] RenderHashBytes(bool bold, bool italic, bool underline, bool strikeout, bool shadow, string font = "Consolas")
        {
            var theme = new DisplayTheme { Name = "StyleTest", CanvasWidth = 240, CanvasHeight = 100 };
            theme.BarWidgets.Add(new BarWidgetSpec
            {
                X = 0,
                Y = 0,
                ZIndex = 0,
                MaxWidth = 240,
                MaxHeight = 100,
                FillColor = new SKColor(60, 40, 120, 255),
                IsStaticFill = true
            });
            theme.TextWidgets.Add(new TextWidgetSpec
            {
                X = 20,
                Y = 25,
                ZIndex = 1,
                FontSize = 28,
                FontFamily = font,
                Color = SKColors.White,
                IsStaticText = true,
                StaticContent = "ABCD",
                IsBold = bold,
                IsItalic = italic,
                IsUnderline = underline,
                IsStrikeout = strikeout,
                HasShadow = shadow,
                BoxWidth = 200,
                BoxHeight = 40,
                Anchor = "la"
            });

            using var renderer = new SkiaThemeRenderer(theme, 240, 100, customCacheDir: Path.GetTempPath());
            Assert.True(renderer.RenderIfDirty(new SensorSnapshot(), 100));
            return renderer.OutputBitmap.Bytes;
        }

        var plain = RenderHashBytes(false, false, false, false, false);
        Assert.False(plain.SequenceEqual(RenderHashBytes(true, false, false, false, false)));
        Assert.False(plain.SequenceEqual(RenderHashBytes(false, true, false, false, false)));
        Assert.False(plain.SequenceEqual(RenderHashBytes(false, false, true, false, false)));
        Assert.False(plain.SequenceEqual(RenderHashBytes(false, false, false, true, false)));
        Assert.False(plain.SequenceEqual(RenderHashBytes(false, false, false, false, true)));
        Assert.False(plain.SequenceEqual(RenderHashBytes(false, false, false, false, false, "Arial")));
    }

    private sealed class FakeBackend : IDisplayBackend
    {
        public IReadOnlyList<DisplayDeviceInfo> Devices => [];
        public event Action<DisplayDeviceInfo>? DeviceAttached { add { } remove { } }
        public event Action<uint>? DeviceDetached { add { } remove { } }
        public bool Start(int logLevel = 0) => true;
        public bool SetResolution(uint handle, int width, int height, int refresh = 60) => true;
        public int SendBgraFrame(uint handle, SKBitmap bgraBitmap) => 0;
        public void Stop() { }
        public void Dispose() { }
    }
}
