using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FoxuMarsDisplay.Core.Backends;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;
using SkiaSharp;
using Xunit;

namespace FoxuMarsDisplay.Core.Tests;

public sealed class CleanRoomV21Tests
{
    [Fact]
    public void SerialTileDiffEngine_EmitsFullSync_ZeroOnIdentical_MinimalDirty_AndBudgeted()
    {
        var engine = new SerialTileDiffEngine(64, 32, tileWidth: 16, tileHeight: 16);
        ushort[] frameA = new ushort[64 * 32];
        Array.Fill(frameA, (ushort)0x001F);

        var initial = engine.ComputeDirtyRects(frameA, 100_000, out bool pending1);
        Assert.False(pending1);
        Assert.NotEmpty(initial);
        int initialPixels = initial.Sum(r => r.Width * r.Height);
        Assert.Equal(64 * 32, initialPixels);

        var identical = engine.ComputeDirtyRects(frameA, 100_000, out bool pending2);
        Assert.False(pending2);
        Assert.Empty(identical);

        // Change a single 4x4 block inside tile (1, 0)
        ushort[] frameB = (ushort[])frameA.Clone();
        for (int y = 2; y < 6; y++)
            for (int x = 20; x < 24; x++)
                frameB[y * 64 + x] = 0xF800;

        var dirty = engine.ComputeDirtyRects(frameB, 100_000, out bool pending3);
        Assert.False(pending3);
        Assert.Single(dirty);
        Assert.Equal(new DirtyTileRect(20, 2, 4, 4), dirty[0]);

        byte[] le = SerialTileDiffEngine.ExtractRectRgb565Le(frameB, 64, dirty[0]);
        Assert.Equal(4 * 4 * 2, le.Length);
        Assert.Equal(0x00, le[0]);
        Assert.Equal(0xF8, le[1]);

        // Invalidate + tight budget must report pending=true then finish on next call
        engine.Invalidate();
        var part1 = engine.ComputeDirtyRects(frameB, 1024, out bool pending4);
        Assert.True(pending4);
        Assert.NotEmpty(part1);

        var part2 = engine.ComputeDirtyRects(frameB, 100_000, out bool pending5);
        Assert.False(pending5);
        Assert.NotEmpty(part2);
    }

    [Fact]
    public void TurzxWinUsbBackend_BuildsDecryptableSlv3Header_AndConstrainsJpeg()
    {
        byte[] args = [0x00, 0x01, 0x2C, 0x00];
        byte[] pkt = TurzxWinUsbBackend.BuildSlv3Header(101, args, sequenceId: 42);
        Assert.Equal(512, pkt.Length);
        Assert.Equal(0xA1, pkt[510]);
        Assert.Equal(0x1A, pkt[511]);

        byte[] key = Encoding.ASCII.GetBytes("slv3tuzx");
#pragma warning disable SYSLIB0021
        using var des = DES.Create();
#pragma warning restore SYSLIB0021
        des.Key = key;
        des.IV = key;
        des.Mode = CipherMode.CBC;
        des.Padding = PaddingMode.PKCS7;
        byte[] plain = des.DecryptCbc(pkt.AsSpan(0, 504).ToArray(), key);

        Assert.Equal(101, plain[0]);
        Assert.Equal(0x1A, plain[2]);
        Assert.Equal(0x6D, plain[3]);
        Assert.Equal(42u, BitConverter.ToUInt32(plain, 4));
        Assert.Equal(args, plain[8..12]);

        using var bmp = new SKBitmap(240, 240, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp))
            c.Clear(SKColors.MediumPurple);
        byte[] jpg = TurzxWinUsbBackend.EncodeJpegUnderLimit(bmp, maxBytes: 40_000);
        Assert.True(jpg.Length > 100 && jpg.Length <= 40_000);
        Assert.Equal(0xFF, jpg[0]);
        Assert.Equal(0xD8, jpg[1]);
    }

    [Fact]
    public void UsbDisplayCatalog_AndEncoders_CoverExpandedEcosystem()
    {
        Assert.Equal(DisplayProtocolFamily.TurzxWinUsb, UsbDisplayCatalog.FindByVidPid(0x1CBE, 0xA088)!.Family);
        Assert.Equal(DisplayProtocolFamily.TurzxWinUsb, UsbDisplayCatalog.FindByVidPid(0x1CBE, 0xA018)!.Family);
        Assert.Equal(DisplayProtocolFamily.TurzxWinUsb, UsbDisplayCatalog.FindByVidPid(0x1CBE, 0x0028)!.Family);
        Assert.Equal(DisplayProtocolFamily.WeActSerial, UsbDisplayCatalog.FindByVidPid(0x1A86, 0xFE0C)!.Family);
        Assert.Equal(DisplayProtocolFamily.ThermaltakeHid, UsbDisplayCatalog.FindByVidPid(0x264A, 0x2347)!.Family);
        Assert.Equal(DisplayProtocolFamily.ThermaltakeHid, UsbDisplayCatalog.FindByVidPid(0x26CE, 0x0A10)!.Family);
        Assert.Equal(DisplayProtocolFamily.NzxtKraken, UsbDisplayCatalog.FindByVidPid(0x1E71, 0x3008)!.Family);
        Assert.Equal(DisplayProtocolFamily.CorsairHid, UsbDisplayCatalog.FindByVidPid(0x1B1C, 0x0C39)!.Family);
        Assert.Equal(DisplayProtocolFamily.GigabyteAorus, UsbDisplayCatalog.FindByVidPid(0x048D, 0x5702)!.Family);

        var (chunks, needsZlp) = Ms9132Protocol.ChunkBulkPayload(new byte[65536 * 2]);
        Assert.Equal(2, chunks.Count);
        Assert.True(needsZlp);

        var (chunks2, needsZlp2) = Ms9132Protocol.ChunkBulkPayload(new byte[70000]);
        Assert.Equal(2, chunks2.Count);
        Assert.False(needsZlp2);

        byte[] hdr60 = VmaxCdc60Protocol.Build60ByteFrameHeader(320, 1480, 12345);
        Assert.Equal(60, hdr60.Length);
        Assert.Equal(320, BitConverter.ToUInt16(hdr60, 4));
        Assert.Equal(1480, BitConverter.ToUInt16(hdr60, 6));
        Assert.Equal(12345, BitConverter.ToInt32(hdr60, 8));
    }

    [Fact]
    public void MediaArtAndLyrics_CleansQueries_AppliesOffset_AndExtractsAccent()
    {
        Assert.Equal("Daft Punk - Harder Better", MediaArtAndLyricsProvider.CleanTrackQuery("Daft Punk - Harder Better (Official Music Video) - YouTube"));
        Assert.Equal("Synthwave Mix", MediaArtAndLyricsProvider.CleanTrackQuery("Synthwave Mix [Official Audio]"));

        using var prov = new MediaArtAndLyricsProvider { LyricsOffsetMs = 1500 };
        Assert.Equal(1500, prov.LyricsOffsetMs);
        prov.LyricsOffsetMs = -2500;
        Assert.Equal(-2500, prov.LyricsOffsetMs);

        using var redBmp = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul);
        redBmp.Erase(new SKColor(220, 40, 30, 255));
        using var img = SKImage.FromBitmap(redBmp);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        SKColor? accent = MediaArtAndLyricsProvider.ExtractDominantAccentColor(png.ToArray());
        Assert.NotNull(accent);
        Assert.True(accent!.Value.Red > 180 && accent.Value.Green < 80);

        using var blackBmp = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        blackBmp.Erase(SKColors.Black);
        using var bImg = SKImage.FromBitmap(blackBmp);
        using var bPng = bImg.Encode(SKEncodedImageFormat.Png, 100);
        Assert.Null(MediaArtAndLyricsProvider.ExtractDominantAccentColor(bPng.ToArray()));
    }

    [Fact]
    public void BrightnessSchedule_And_ThemeCanvasScaler_WorkAcrossAllCases()
    {
        Assert.Equal(90, BrightnessSchedule.ComputeEffectiveBrightness(90, false, 22, 7, 25, 23));
        Assert.Equal(25, BrightnessSchedule.ComputeEffectiveBrightness(90, true, 22, 7, 25, 23));
        Assert.Equal(25, BrightnessSchedule.ComputeEffectiveBrightness(90, true, 22, 7, 25, 3));
        Assert.Equal(90, BrightnessSchedule.ComputeEffectiveBrightness(90, true, 22, 7, 25, 14));
        Assert.Equal(35, BrightnessSchedule.ComputeEffectiveBrightness(90, true, 13, 17, 35, 15));
        Assert.Equal(90, BrightnessSchedule.ComputeEffectiveBrightness(90, true, 10, 10, 25, 10));

        var scaledSquare = ThemeCanvasScaler.ScaleRect(100, 200, 100, 100, 20, 400, 800, 800, 800, preserveSquareAspect: true);
        Assert.Equal(200f, scaledSquare.X);
        Assert.Equal(200f, scaledSquare.Y);
        Assert.Equal(100f, scaledSquare.Width);
        Assert.Equal(100f, scaledSquare.Height);

        var scaledBar = ThemeCanvasScaler.ScaleRect(20, 40, 200, 20, 16, 400, 800, 800, 400, preserveSquareAspect: false);
        Assert.Equal(40f, scaledBar.X);
        Assert.Equal(20f, scaledBar.Y);
        Assert.Equal(400f, scaledBar.Width);
        Assert.Equal(10f, scaledBar.Height);
    }

    [Fact]
    public async Task LocalHttpRemoteServer_ServesHtml_EnforcesToken_AndInvokesCallbacks()
    {
        int port = GetFreeLoopbackPort();
        int? receivedBrightness = null;

        using var server = new LocalHttpRemoteServer(
            port,
            "secret-foxu-token",
            "2.1.0",
            () => [new ScreenRuntimeStatus(1, "SN_TEST", 360, 960, "Cyber", "", 80, 0, true, false, 10, 0, 0, 0, 0)],
            () => new SensorSnapshot { CpuTemp = 55, CpuUsage = 30 },
            cmd =>
            {
                if (cmd.Action == "set_brightness" && cmd.IntValue.HasValue)
                {
                    receivedBrightness = cmd.IntValue.Value;
                    return true;
                }
                return false;
            });

        Assert.True(server.Start());
        using var http = new HttpClient();

        var homeResp = await http.GetAsync(server.BaseUrl);
        Assert.Equal(HttpStatusCode.OK, homeResp.StatusCode);
        string html = await homeResp.Content.ReadAsStringAsync();
        Assert.Contains("FOXU // REMOTE CONTROL", html);

        var unauthResp = await http.GetAsync($"{server.BaseUrl}api/status");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

        using var authReq = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUrl}api/status");
        authReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret-foxu-token");
        var authResp = await http.SendAsync(authReq);
        Assert.Equal(HttpStatusCode.OK, authResp.StatusCode);
        string statusJson = await authResp.Content.ReadAsStringAsync();
        Assert.Contains("SN_TEST", statusJson);

        using var postReq = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUrl}api/screen/brightness");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret-foxu-token");
        postReq.Content = new StringContent("{\"serial\":\"SN_TEST\",\"value\":42}", Encoding.UTF8, "application/json");
        var postResp = await http.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        Assert.Equal(42, receivedBrightness);
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
