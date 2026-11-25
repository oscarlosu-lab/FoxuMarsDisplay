using System.Text;
using FoxuMarsDisplay.Core.Backends;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;
using SkiaSharp;
using Xunit;

namespace FoxuMarsDisplay.Core.Tests;

public sealed class MultiHardwareAndMediaTests
{
    [Fact]
    public void UsbDisplayCatalog_CoversAllSixHardwareFamilies()
    {
        Assert.Equal(DisplayProtocolFamily.MacroSiliconMs9132, UsbDisplayCatalog.FindByVidPid(0x345F, 0x9132)!.Family);
        Assert.Equal(DisplayProtocolFamily.HlVmaxCdcDls, UsbDisplayCatalog.FindByVidPid(0x33C3, 0xF101)!.Family);
        Assert.Equal(DisplayProtocolFamily.HlVmaxCdcDls, UsbDisplayCatalog.FindByVidPid(0x33C3, 0x7788)!.Family);
        Assert.Equal(DisplayProtocolFamily.ArtInChipAic, UsbDisplayCatalog.FindByVidPid(0x33C3, 0x0E02)!.Family);
        Assert.Equal(DisplayProtocolFamily.TurzxWinUsb, UsbDisplayCatalog.FindByVidPid(0x1CBE, 0x0092)!.Family);
        Assert.Equal(DisplayProtocolFamily.TuringSerial, UsbDisplayCatalog.FindByVidPid(0x1A86, 0x5722)!.Family);
        Assert.Equal(DisplayProtocolFamily.ThermalrightLy, UsbDisplayCatalog.FindByVidPid(0x0416, 0x5408)!.Family);
        Assert.Equal(DisplayProtocolFamily.ChiZhuSomore, UsbDisplayCatalog.FindByVidPid(0x87AD, 0x70DB)!.Family);
        Assert.Equal(DisplayProtocolFamily.MiraBox, UsbDisplayCatalog.FindByVidPid(0x5548, 0x1011)!.Family);
    }

    [Fact]
    public void SerialCdcVmaxBackend_ParsesGeometry_BuildsBrightness_AndConstrainsJpeg()
    {
        Assert.Equal((320, 1480), SerialCdcVmaxBackend.ParseVmaxSerialGeometry("VMAXA170320*1480S000123"));
        Assert.Equal((480, 960), SerialCdcVmaxBackend.ParseVmaxSerialGeometry("VMAXC_480x960_ABC"));
        Assert.Equal([0xAA, 0xBB, 75, 0xCC, 0xDD], SerialCdcVmaxBackend.BuildBrightnessCommand(75));

        using var bmp = new SKBitmap(320, 1480, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.DarkSlateBlue);
            using var p = new SKPaint { Color = SKColors.OrangeRed };
            c.DrawCircle(160, 740, 120, p);
        }

        byte[] jpeg = SerialCdcVmaxBackend.EncodeConstrainedJpeg(bmp, maxBytes: 98_000);
        Assert.True(jpeg.Length > 100 && jpeg.Length <= 98_000);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        Assert.Equal(0xFF, jpeg[^2]);
        Assert.Equal(0xD9, jpeg[^1]);
    }

    [Fact]
    public void TurzxSerialBackend_BuildsRevACommand_AndRgb565()
    {
        byte[] cmd = TurzxSerialBackend.BuildRevACommand(197, 0, 0, 319, 479);
        Assert.Equal(6, cmd.Length);
        Assert.Equal(197, cmd[5]);

        using var bmp = new SKBitmap(4, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(255, 0, 0, 255)); // Pure Red -> 0xF800 LE = [0x00, 0xF8]
        byte[] rgb565 = TurzxSerialBackend.ConvertBgraToRgb565Le(bmp);
        Assert.Equal(16, rgb565.Length);
        Assert.Equal(0x00, rgb565[0]);
        Assert.Equal(0xF8, rgb565[1]);
    }

    [Fact]
    public void MultiProtocolEncoders_ProduceValidHeadersAndPixelBuffers()
    {
        using var bmp = new SKBitmap(16, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.White); // White has 0xFF bytes to test BGR888 sanitization

        byte[] msHdr = Ms9132Protocol.BuildFrameHeader(0, 0, 360, 960);
        Assert.Equal(8, msHdr.Length);
        Assert.Equal(0xFF, msHdr[0]);

        byte[] uyvy = Ms9132Protocol.ConvertBgraToUyvy(bmp);
        Assert.Equal(16 * 4 * 2, uyvy.Length);

        byte[] bgr = Ms9132Protocol.ConvertBgraToSanitizedBgr888(bmp);
        Assert.Equal(16 * 4 * 3, bgr.Length);
        Assert.DoesNotContain((byte)0xFF, bgr);

        byte[] vmaxBulk = HlVmaxBulkProtocol.Build12ByteHeader(640, 1920);
        Assert.Equal([0x0A, 0x10, 0x08, 0x00, 0x80, 0x02, 0x80, 0x07, 0x00, 0x00, 0x01, 0x00], vmaxBulk);

        var lyChunks = ThermalrightProtocol.ChunkLyJpeg(new byte[1000]);
        Assert.Equal(3, lyChunks.Count);
        Assert.Equal(512, lyChunks[0].Length);

        byte[] czHdr = ThermalrightProtocol.BuildChiZhuImageHeader(1600, 720, 42000);
        Assert.Equal(64, czHdr.Length);
        Assert.Equal(42000, BitConverter.ToInt32(czHdr, 60));

        byte[] aicRep = ArtInChipHidProtocol.BuildEscapedJsonReport("POST brightness 1", "{\"value\":80}");
        Assert.Equal(1025, aicRep.Length);
        Assert.Equal(0x5A, aicRep[1]);
    }

    [Fact]
    public void WasapiLoopbackFftProvider_ComputesFftBands_FromSineWave()
    {
        float[] silence = new float[256];
        float[] silentBands = WasapiLoopbackFftProvider.ComputeFftBands(silence, 16);
        Assert.All(silentBands, b => Assert.Equal(0f, b));

        float[] tone = new float[256];
        for (int i = 0; i < tone.Length; i++)
            tone[i] = MathF.Sin(2f * MathF.PI * 16f * i / 256f);

        float[] toneBands = WasapiLoopbackFftProvider.ComputeFftBands(tone, 16);
        Assert.Contains(toneBands, b => b > 0.2f);
    }

    [Fact]
    public void MediaArtAndLyricsProvider_ParsesSyncedLrc_AndSelectsLineByTimestamp()
    {
        string lrc = """
            [00:05.00]First line
            [00:12.50]Second line
            [00:25.10]Third line
            """;
        var parsed = MediaArtAndLyricsProvider.ParseLrc(lrc);
        Assert.Equal(3, parsed.Count);
        Assert.Equal("First line", MediaArtAndLyricsProvider.GetLineAt(parsed, 7000));
        Assert.Equal("Second line", MediaArtAndLyricsProvider.GetLineAt(parsed, 15000));
        Assert.Equal("Third line", MediaArtAndLyricsProvider.GetLineAt(parsed, 30000));
    }

    [Fact]
    public void UnifiedThemeLoader_ParsesFoxuJsonTheme_AndAiQuotaSensors()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FoxuJsonTheme_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "theme.json"), """
                {
                  "format": "foxu-theme-v1",
                  "Name": "UnicornJsonHud",
                  "Width": 480,
                  "Height": 320,
                  "widgets": [
                    { "type": "card", "x": 10, "y": 10, "width": 200, "height": 80, "color": "#80161616" },
                    { "type": "text", "x": 20, "y": 20, "sensor": "AI_CLAUDE", "prefix": "Claude ", "unit": "%", "fontSize": 20 },
                    { "type": "bar", "x": 20, "y": 55, "width": 160, "height": 12, "sensor": "AI_CLAUDE", "color": "#FF6419" },
                    { "type": "audiovisualizer", "x": 20, "y": 120, "width": 240, "height": 50, "color": "#00FFFF", "backColor": "#FF00FF" },
                    { "type": "albumart", "x": 300, "y": 20, "width": 96, "height": 96, "vinyl": true }
                  ]
                }
                """, Encoding.UTF8);

            var theme = UnifiedThemeLoader.Load(dir);
            Assert.Equal("UnicornJsonHud", theme.Name);
            Assert.Equal(480f, theme.CanvasWidth);
            Assert.Equal(2, theme.BarWidgets.Count);
            Assert.True(theme.BarWidgets[0].IsStaticFill);
            Assert.Single(theme.TextWidgets);
            Assert.Single(theme.AudioVisualizers);
            Assert.Single(theme.AlbumArts);
            Assert.True(theme.AlbumArts[0].IsVinylSpin);

            var snap = new SensorSnapshot { AiClaudePct = 82, AiCodexPct = 64, AiGeminiPct = 95, AiActiveTasks = 3 };
            Assert.Equal(82, snap.GetNumericValue("AI_CLAUDE"));
            Assert.Equal("82%", snap.FormatValue("AI_CLAUDE", "%"));
            Assert.Equal(3, snap.GetNumericValue("AI_TASKS"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ThemeExporter_AutoContrast_SettingTxt_Json_AndSmthemeRoundTrip()
    {
        using var darkBg = new SKBitmap(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul);
        darkBg.Erase(new SKColor(15, 15, 20, 255));
        Assert.Equal(SKColors.White, ThemeExporter.ComputeAutoContrastColor(darkBg, new SKRect(10, 10, 80, 40), new SKColor(20, 20, 25, 255)));

        using var lightBg = new SKBitmap(100, 100, SKColorType.Bgra8888, SKAlphaType.Premul);
        lightBg.Erase(new SKColor(240, 240, 245, 255));
        var flipped = ThemeExporter.ComputeAutoContrastColor(lightBg, new SKRect(10, 10, 80, 40), new SKColor(235, 235, 240, 255));
        Assert.True(flipped.Red < 40 && flipped.Green < 40);

        var theme = new DisplayTheme { Name = "StudioRoundTrip", CanvasWidth = 360, CanvasHeight = 960 };
        theme.BarWidgets.Add(new BarWidgetSpec { X = 16, Y = 20, MaxWidth = 320, MaxHeight = 60, FillColor = new SKColor(22, 22, 32, 220), IsStaticFill = true });
        theme.BarWidgets.Add(new BarWidgetSpec { X = 24, Y = 100, MaxWidth = 300, MaxHeight = 16, FillColor = SKColors.Cyan, SensorKey = "CpuUsage" });
        theme.RadialWidgets.Add(new RadialWidgetSpec { Cx = 180, Cy = 240, Radius = 44, Thickness = 10, FillColor = SKColors.OrangeRed, SensorKey = "GpuUsage", ShowText = false });
        theme.TextWidgets.Add(new TextWidgetSpec { X = 24, Y = 36, FontSize = 24, Prefix = "CPU ", SensorKey = "CPUTemp", Unit = "°C", Color = SKColors.White });
        theme.AudioVisualizers.Add(new AudioVisualizerSpec { X = 24, Y = 340, Width = 300, Height = 50 });
        theme.AlbumArts.Add(new AlbumArtSpec { X = 24, Y = 420, Width = 80, Height = 80 });

        string outZip = Path.Combine(Path.GetTempPath(), $"StudioRoundTrip_{Guid.NewGuid():N}.smtheme");
        try
        {
            ThemeExporter.ExportToSmthemePackage(theme, outZip);
            Assert.True(File.Exists(outZip));

            using (var zip = System.IO.Compression.ZipFile.OpenRead(outZip))
            {
                Assert.NotNull(zip.GetEntry("manifest.json"));
                Assert.NotNull(zip.GetEntry("Setting.txt"));
                Assert.NotNull(zip.GetEntry("theme.json"));
                var prev = zip.GetEntry("preview.png");
                Assert.NotNull(prev);
                Assert.True(prev!.Length > 100);
            }

            var reloaded = UnifiedThemeLoader.Load(outZip);
            Assert.Equal("StudioRoundTrip", reloaded.Name);
            Assert.Equal(2, reloaded.BarWidgets.Count);
            Assert.Single(reloaded.RadialWidgets);
            Assert.Single(reloaded.TextWidgets);
            Assert.Single(reloaded.AudioVisualizers);
            Assert.Single(reloaded.AlbumArts);
        }
        finally
        {
            if (File.Exists(outZip)) File.Delete(outZip);
        }
    }

    [Fact]
    public void UpdateChecker_ParsesGitHubRelease_ComparesSemver_AndVerifiesSha256()
    {
        Assert.True(FoxuMarsDisplay.Core.Host.UpdateChecker.IsNewerVersion("1.1.0", "v2.0.0"));
        Assert.False(FoxuMarsDisplay.Core.Host.UpdateChecker.IsNewerVersion("2.0.0", "v1.2.0"));

        string sha = new string('a', 64);
        string json = $$"""
            {
              "tag_name": "v2.0.0",
              "html_url": "https://github.com/oscarlosu-lab/FoxuMarsDisplay/releases/tag/v2.0.0",
              "body": "SHA-256: {{sha}}",
              "assets": [
                { "name": "FoxuMarsDisplay_Setup.exe", "browser_download_url": "https://example.com/Setup.exe" }
              ]
            }
            """;

        var res = FoxuMarsDisplay.Core.Host.UpdateChecker.ParseReleaseJson(json, "1.1.0");
        Assert.True(res.UpdateAvailable);
        Assert.Equal("v2.0.0", res.LatestTag);
        Assert.Equal("https://example.com/Setup.exe", res.InstallerDownloadUrl);
        Assert.Equal(sha.ToUpperInvariant(), res.ExpectedSha256);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "foxu-verify");
            using var fs = File.OpenRead(tmp);
            string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
            Assert.True(FoxuMarsDisplay.Core.Host.UpdateChecker.VerifyFileSha256(tmp, actual));
            Assert.False(FoxuMarsDisplay.Core.Host.UpdateChecker.VerifyFileSha256(tmp, sha));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
