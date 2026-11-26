using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FoxuMarsDisplay.Core.Backends;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Core.Rendering;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;
using SkiaSharp;
using Xunit;

namespace FoxuMarsDisplay.Core.Tests;

public sealed class V21StressAndE2ETests
{
    private sealed class FakeDualDisplayBackend : IDisplayBackend
    {
        private readonly List<DisplayDeviceInfo> _devices =
        [
            new(0x1001, "VMAXC230360*0960S_SIM1", 360, 960, 60, 2),
            new(0x1002, "VMAXA180240*0320S_SIM2", 240, 320, 60, 0)
        ];

        public readonly ConcurrentDictionary<uint, int> LastBrightnessByHandle = new();
        public readonly ConcurrentDictionary<uint, long> FrameCountByHandle = new();

        public event Action<DisplayDeviceInfo>? DeviceAttached;
        public event Action<uint>? DeviceDetached;

        public IReadOnlyList<DisplayDeviceInfo> Devices => _devices;

        public bool Start(int logLevel = 0)
        {
            foreach (var d in _devices)
                DeviceAttached?.Invoke(d);
            return true;
        }

        public bool SetResolution(uint handle, int width, int height, int refresh = 60) => true;

        public int SendBgraFrame(uint handle, SKBitmap bgraBitmap)
        {
            FrameCountByHandle.AddOrUpdate(handle, 1, (_, prev) => prev + 1);
            return 0;
        }

        public bool TrySetHardwareBrightness(uint handle, int brightness)
        {
            LastBrightnessByHandle[handle] = brightness;
            return true;
        }

        public void Stop()
        {
            foreach (var d in _devices)
                DeviceDetached?.Invoke(d.Handle);
        }

        public void Dispose() => Stop();
    }

    [Fact]
    public void Level1_SerialTileDiff_100Frames_PixelExactReconstruction_AndBandwidthSavings()
    {
        const int w = 480;
        const int h = 320;
        const int totalFrames = 100;
        var engine = new SerialTileDiffEngine(w, h, tileWidth: 16, tileHeight: 16);
        ushort[] remotePanel = new ushort[w * h];
        long totalDiffBytes = 0;
        long totalFullBytes = (long)w * h * 2 * totalFrames;

        using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        using var bgPaint = new SKPaint { Color = new SKColor(14, 12, 22, 255) };
        using var cardPaint = new SKPaint { Color = new SKColor(28, 24, 42, 255) };
        using var barPaint = new SKPaint { Color = new SKColor(255, 100, 25, 255) };
        using var textPaint = new SKPaint { Color = SKColors.White, TextSize = 22f, IsAntialias = false };

        for (int f = 0; f < totalFrames; f++)
        {
            canvas.DrawRect(0, 0, w, h, bgPaint);
            canvas.DrawRect(16, 16, 448, 288, cardPaint);
            canvas.DrawText($"12:34:{f % 60:00}", 32, 58, textPaint);
            float barW = 40 + (f % 50) * 3f;
            canvas.DrawRect(32, 84, barW, 14, barPaint);

            ushort[] frame = SerialTileDiffEngine.ConvertBgraToRgb565Array(bmp);

            int safety = 0;
            bool hasPending;
            do
            {
                var rects = engine.ComputeDirtyRects(frame, maxBytesBudget: 32_768, out hasPending);
                foreach (var r in rects)
                {
                    byte[] payload = SerialTileDiffEngine.ExtractRectRgb565Le(frame, w, r);
                    totalDiffBytes += 6 + payload.Length;
                    int pIdx = 0;
                    for (int y = r.Y; y < r.Y + r.Height; y++)
                    {
                        int rowBase = y * w + r.X;
                        for (int x = 0; x < r.Width; x++)
                        {
                            ushort px = (ushort)(payload[pIdx] | (payload[pIdx + 1] << 8));
                            pIdx += 2;
                            remotePanel[rowBase + x] = px;
                        }
                    }
                }
                safety++;
                Assert.True(safety < 40, "Diff drain exceeded expected passes.");
            }
            while (hasPending);

            Assert.Equal(frame, remotePanel);
        }

        double savingsPct = (1.0 - (double)totalDiffBytes / totalFullBytes) * 100.0;
        Assert.True(savingsPct >= 80.0, $"Expected >=80% bandwidth savings, got {savingsPct:F2}%");
    }

    [Fact]
    public void Level2_WinUsbSlv3_Fuzz50Headers_24Pids_AndHighEntropyJpegClamping()
    {
        var winUsbSpecs = UsbDisplayCatalog.KnownDisplays
            .Where(d => d.Family == DisplayProtocolFamily.TurzxWinUsb)
            .ToList();
        Assert.True(winUsbSpecs.Count >= 24, $"Expected >=24 WinUSB PIDs, got {winUsbSpecs.Count}");

        byte[] key = Encoding.ASCII.GetBytes("slv3tuzx");
        var rng = new Random(20261125);

#pragma warning disable SYSLIB0021
        using var des = DES.Create();
#pragma warning restore SYSLIB0021
        des.Key = key;
        des.IV = key;
        des.Mode = CipherMode.CBC;
        des.Padding = PaddingMode.PKCS7;

        for (int i = 0; i < 50; i++)
        {
            bool legacy = (i % 2) == 1;
            byte cmd = (byte)(10 + (i % 120));
            uint seq = (uint)(1000 + i * 17);
            byte[] payloadArgs = new byte[1 + (i % 16)];
            rng.NextBytes(payloadArgs);

            byte[] pkt = TurzxWinUsbBackend.BuildSlv3Header(cmd, payloadArgs, seq, legacy504: legacy);
            Assert.Equal(512, pkt.Length);
            if (!legacy)
            {
                Assert.Equal(0xA1, pkt[510]);
                Assert.Equal(0x1A, pkt[511]);
            }

            int cipherLen = legacy ? 512 : 504;
            byte[] plain = des.DecryptCbc(pkt.AsSpan(0, cipherLen).ToArray(), key);
            Assert.Equal(cmd, plain[0]);
            Assert.Equal(0x1A, plain[2]);
            Assert.Equal(0x6D, plain[3]);
            Assert.Equal(seq, BitConverter.ToUInt32(plain, 4));
            Assert.Equal(payloadArgs, plain.AsSpan(8, payloadArgs.Length).ToArray());
        }

        (int W, int H, int MaxBytes)[] stressResolutions =
        [
            (480, 1920, 480_000),
            (720, 1472, 480_000),
            (1080, 2320, 500_000)
        ];

        foreach (var (bw, bh, maxBytes) in stressResolutions)
        {
            using var noisy = new SKBitmap(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var c = new SKCanvas(noisy))
            {
                c.Clear(SKColors.Black);
                using var p = new SKPaint { IsAntialias = true };
                for (int k = 0; k < 200; k++)
                {
                    p.Color = new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 255);
                    c.DrawCircle(rng.Next(bw), rng.Next(bh), 12 + rng.Next(80), p);
                }
            }

            byte[] jpg = TurzxWinUsbBackend.EncodeJpegUnderLimit(noisy, maxBytes);
            Assert.True(jpg.Length > 500 && jpg.Length <= maxBytes, $"{bw}x{bh} JPEG size {jpg.Length} > {maxBytes}");
            Assert.Equal(0xFF, jpg[0]);
            Assert.Equal(0xD8, jpg[1]);
            Assert.Equal(0xFF, jpg[^2]);
            Assert.Equal(0xD9, jpg[^1]);
        }
    }

    [Fact]
    public async Task Level3_DisplayHost_HttpRemote_AndNightSchedule_DualScreenEndToEnd()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuV21E2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string cfgPath = Path.Combine(tempDir, "config.json");
        int port = GetFreeLoopbackPort();
        const string token = "foxu-e2e-token-210";

        try
        {
            var store = new FoxuDisplayConfigStore(cfgPath);
            var cfg = new FoxuDisplayConfig
            {
                TargetFps = 15,
                PauseOnFullscreen = false,
                NightBrightnessEnabled = true,
                NightStartHour = 22,
                NightEndHour = 7,
                NightBrightnessPct = 30,
                HttpRemoteEnabled = true,
                HttpRemotePort = port,
                HttpRemoteToken = token
            };

            using var fakeBackend = new FakeDualDisplayBackend();
            using var host = new DisplayHost(
                backend: fakeBackend,
                configStore: store,
                config: cfg,
                customCacheDir: Path.Combine(tempDir, "anim"));

            host.Start(dryRun: false, enableIpc: false);
            await Task.Delay(250);

            Assert.Equal(2, host.GetStatuses().Count);

            using var http = new HttpClient();

            var rootResp = await http.GetAsync(host.HttpRemoteUrl);
            Assert.Equal(HttpStatusCode.OK, rootResp.StatusCode);
            string html = await rootResp.Content.ReadAsStringAsync();
            Assert.Contains("FOXU // REMOTE CONTROL", html);

            var unauth = await http.GetAsync($"{host.HttpRemoteUrl}api/status");
            Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

            var qsResp = await http.GetAsync($"{host.HttpRemoteUrl}api/status?token={token}");
            Assert.Equal(HttpStatusCode.OK, qsResp.StatusCode);
            string statusJson = await qsResp.Content.ReadAsStringAsync();
            Assert.Contains("VMAXC230360*0960S_SIM1", statusJson);
            Assert.Contains("VMAXA180240*0320S_SIM2", statusJson);

            async Task<HttpResponseMessage> PostApiAsync(string ep, string json)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{host.HttpRemoteUrl}{ep}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return await http.SendAsync(req);
            }

            Assert.Equal(HttpStatusCode.OK, (await PostApiAsync("api/screen/brightness", "{\"serial\":\"VMAXC230360*0960S_SIM1\",\"value\":85}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostApiAsync("api/screen/rotation", "{\"serial\":\"VMAXC230360*0960S_SIM1\",\"value\":1}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostApiAsync("api/screen/enabled", "{\"serial\":\"VMAXA180240*0320S_SIM2\",\"flag\":false}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostApiAsync("api/fps", "{\"value\":20}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PostApiAsync("api/unknown", "{}")).StatusCode);

            var statuses = host.GetStatuses();
            var s1 = statuses.Single(x => x.SerialNumber == "VMAXC230360*0960S_SIM1");
            var s2 = statuses.Single(x => x.SerialNumber == "VMAXA180240*0320S_SIM2");
            Assert.Equal(85, s1.Brightness);
            Assert.Equal(1, s1.Rotation);
            Assert.False(s2.IsEnabled);
            Assert.Equal(20, host.Config.TargetFps);

            host.ApplyNightBrightnessScheduleNow(new DateTime(2026, 11, 25, 23, 0, 0));
            Assert.Equal(30, fakeBackend.LastBrightnessByHandle[0x1001]);
            Assert.Equal(85, host.GetStatuses().Single(x => x.SerialNumber == "VMAXC230360*0960S_SIM1").Brightness);

            host.ApplyNightBrightnessScheduleNow(new DateTime(2026, 11, 25, 14, 0, 0));
            Assert.Equal(85, fakeBackend.LastBrightnessByHandle[0x1001]);

            host.SetLyricsOffsetMs(1200);
            Assert.Equal(1200, host.Config.LyricsOffsetMs);

            host.Stop();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Level4_AllFiveLanguageDictionaries_CoverEveryDynamicResourceKeyInMainWindow()
    {
        string repoRoot = LocateRepoRoot();
        string appDir = Path.Combine(repoRoot, "src", "FoxuMarsDisplay");
        string mainXaml = File.ReadAllText(Path.Combine(appDir, "MainWindow.xaml"), Encoding.UTF8);

        var referencedKeys = Regex.Matches(mainXaml, @"\{DynamicResource\s+(Str_[A-Za-z0-9_]+)\}")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(referencedKeys);

        string[] langs = ["es", "en", "fr", "de", "pt"];
        XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (string lang in langs)
        {
            string path = Path.Combine(appDir, "Resources", $"Strings.{lang}.xaml");
            Assert.True(File.Exists(path), $"Missing dictionary {path}");
            var doc = XDocument.Load(path);
            var keys = doc.Root!
                .Elements()
                .Select(el => (Key: el.Attribute(xNs + "Key")?.Value, Value: el.Value))
                .Where(pair => !string.IsNullOrEmpty(pair.Key))
                .ToDictionary(pair => pair.Key!, pair => pair.Value, StringComparer.Ordinal);

            foreach (string key in referencedKeys)
            {
                Assert.True(keys.ContainsKey(key), $"Missing key '{key}' in Strings.{lang}.xaml");
                Assert.False(string.IsNullOrWhiteSpace(keys[key]), $"Empty value for '{key}' in Strings.{lang}.xaml");
            }

            foreach (string extraKey in new[] { "Str_NightLevelFmt", "Str_LyricsOffsetFmt", "Str_FpsLabelFmt" })
                Assert.True(keys.ContainsKey(extraKey), $"Missing code-behind key '{extraKey}' in Strings.{lang}.xaml");
        }
    }

    [Fact]
    public void Level5_SkiaThemeRenderer_DrawsAmbientCoverGlow_WhenNoBackgroundLayers()
    {
        var theme = new DisplayTheme
        {
            Name = "AmbientVinylOnly",
            CanvasWidth = 360,
            CanvasHeight = 960
        };
        theme.AlbumArts.Add(new AlbumArtSpec
        {
            X = 110,
            Y = 300,
            Width = 140,
            Height = 140,
            IsVinylSpin = true,
            ZIndex = 2
        });

        using var artBmp = new SKBitmap(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul);
        artBmp.Erase(new SKColor(235, 35, 45, 255));
        using var artImg = SKImage.FromBitmap(artBmp);
        using var artPng = artImg.Encode(SKEncodedImageFormat.Png, 100);

        using var renderer = new SkiaThemeRenderer(theme, 360, 960, targetFps: 12, brightness: 100, rotationOverride: 0);
        var snap = new SensorSnapshot { AlbumArtBytes = artPng.ToArray(), SongTitle = "Test Track", Artist = "Test Artist" };

        bool dirty = renderer.RenderIfDirty(snap, 100);
        Assert.True(dirty);

        SKColor bgSample = renderer.OutputBitmap.GetPixel(40, 380);
        Assert.True(bgSample.Red > 30, $"Expected ambient red tint > 30, got {bgSample}");
        Assert.True(bgSample.Red > bgSample.Blue + 15, $"Expected red-dominant ambient tint, got {bgSample}");
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "THIRD_PARTY_NOTICES.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
