using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FoxuMarsDisplay.Core.Themes;
using Xunit;

namespace FoxuMarsDisplay.Core.Tests;

public sealed class CloudThemeServiceTests
{
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    [Fact]
    public void ParseSoeyiListResponse_ExtractsResolutionOrientationAndUrls()
    {
        string json = """
            {
              "code": 1,
              "rowtotal": 1,
              "data": [
                {
                  "id": "471",
                  "title": "PinkFlower",
                  "ico": "/static/upload/images/20260403/20260403373947.png",
                  "enclosure": "/static/upload/other/20260403/20260403618331.photo",
                  "ext_type": "240x320"
                }
              ]
            }
            """;

        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuCloudTest_" + Guid.NewGuid().ToString("N"));
        var (items, total) = CloudThemeService.ParseSoeyiListResponse(json, tempDir);

        Assert.Equal(1, total);
        var item = Assert.Single(items);
        Assert.Equal("PinkFlower", item.Name);
        Assert.Equal(240, item.Width);
        Assert.Equal(320, item.Height);
        Assert.Equal(ThemeOrientation.Vertical, item.Orientation);
        Assert.Equal("SOEYI", item.Source);
        Assert.Equal("https://www.soeyiphoto.com/static/upload/other/20260403/20260403618331.photo", item.DownloadUrl);
        Assert.Equal("https://www.soeyiphoto.com/static/upload/images/20260403/20260403373947.png", item.ThumbnailUrl);
    }

    [Fact]
    public async Task FetchCatalogAsync_FallsBackToHuggingFace_WhenSoeyiFails()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuCloudFallback_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var handler = new StubHttpHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("soeyiphoto.com", StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                string hfJson = """
                    {
                      "items": [
                        {
                          "id": "100_Braum",
                          "name": "100_Braum",
                          "download": "themes/100_Braum.photo",
                          "preview": "previews/100_Braum.png",
                          "resolution": "480x480",
                          "size": 3204504,
                          "type": "photo"
                        }
                      ]
                    }
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(hfJson, Encoding.UTF8, "application/json")
                };
            });

            using var http = new HttpClient(handler);
            var svc = new CloudThemeService(http, Path.Combine(tempDir, "themes"), Path.Combine(tempDir, "cache"));

            var res = await svc.FetchCatalogAsync(CloudSourceMode.AutoSoeyiWithHfFallback);
            Assert.True(res.UsedFallback);
            var item = Assert.Single(res.Items);
            Assert.Equal("100_Braum", item.Name);
            Assert.Equal("HF", item.Source);
            Assert.Equal(ThemeOrientation.Square, item.Orientation);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractApiDataJson_ParsesSingleQuotedJsonWithInnerDoubleQuotes()
    {
        string html = """
            <html><head>
            <meta name="api_data" content='{"appid":"webapi","timestamp":"1795571128","signature":"420a7c2fec81eba51dde8add6b1f0fcc","acode":"cn"}'>
            </head></html>
            """;

        string? json = CloudThemeService.ExtractApiDataJson(html);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json!);
        Assert.Equal("webapi", doc.RootElement.GetProperty("appid").GetString());
        Assert.Equal("1795571128", doc.RootElement.GetProperty("timestamp").GetString());
        Assert.Equal("420a7c2fec81eba51dde8add6b1f0fcc", doc.RootElement.GetProperty("signature").GetString());
        Assert.Equal("cn", doc.RootElement.GetProperty("acode").GetString());
    }

    [Fact]
    public void ParseHuggingFaceCatalog_UsesVerifiedTurzxResolutionsAndFriendlyNames()
    {
        string hfJson = """
            {
              "items": [
                {
                  "id": "TURZX_1_7201280",
                  "name": "1",
                  "source": "turzx",
                  "type": "smtheme",
                  "download": "smtheme/TURZX_1_7201280.smtheme",
                  "preview": "previews/TURZX_1_7201280.png",
                  "size": 283081
                },
                {
                  "id": "TURZX_AMD_320960",
                  "name": "AMD",
                  "source": "turzx",
                  "type": "smtheme",
                  "resolution": "1920x480",
                  "download": "smtheme/TURZX_AMD_320960.smtheme",
                  "preview": "previews/TURZX_AMD_320960.png",
                  "size": 203000
                },
                {
                  "id": "TURZX_Cyberpunk_320960",
                  "name": "Cyberpunk",
                  "source": "turzx",
                  "type": "smtheme",
                  "resolution": "320x480",
                  "download": "smtheme/TURZX_Cyberpunk_320960.smtheme",
                  "preview": "previews/TURZX_Cyberpunk_320960.png",
                  "size": 346000
                },
                {
                  "id": "TURZX_Dragon_Ball_vertical_800480",
                  "name": "Dragon Ball vertical",
                  "source": "turzx",
                  "type": "smtheme",
                  "download": "smtheme/TURZX_Dragon Ball_vertical_800480.smtheme",
                  "preview": "previews/TURZX_Dragon Ball_vertical_800480.png",
                  "size": 450000
                },
                {
                  "id": "TURZX_24_480480r",
                  "name": "24_480480r",
                  "source": "turzx",
                  "type": "smtheme",
                  "download": "smtheme/TURZX_24_480480r.smtheme",
                  "preview": "previews/TURZX_24_480480r.png",
                  "size": 216959
                }
              ]
            }
            """;

        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuHfVerified_" + Guid.NewGuid().ToString("N"));
        var items = CloudThemeService.ParseHuggingFaceCatalog(hfJson, tempDir);
        Assert.Equal(5, items.Count);

        Assert.Equal(("TURZX #1", 1280, 720, ThemeOrientation.Horizontal, "TURZX", false), (items[0].Name, items[0].Width, items[0].Height, items[0].Orientation, items[0].Source, items[0].HasLiveSensors));
        Assert.Equal(("AMD", 960, 320, ThemeOrientation.Horizontal, "TURZX", false), (items[1].Name, items[1].Width, items[1].Height, items[1].Orientation, items[1].Source, items[1].HasLiveSensors));
        Assert.Equal(("Cyberpunk", 960, 320, ThemeOrientation.Horizontal, "TURZX", false), (items[2].Name, items[2].Width, items[2].Height, items[2].Orientation, items[2].Source, items[2].HasLiveSensors));
        Assert.Equal(("Dragon Ball vertical", 720, 1280, ThemeOrientation.Vertical, "TURZX", false), (items[3].Name, items[3].Width, items[3].Height, items[3].Orientation, items[3].Source, items[3].HasLiveSensors));
        Assert.Equal(("TURZX #24", 480, 480, ThemeOrientation.Square, "TURZX", false), (items[4].Name, items[4].Width, items[4].Height, items[4].Orientation, items[4].Source, items[4].HasLiveSensors));
    }

    [Fact]
    public void ParseHuggingFaceCatalog_PrefersTurzxLiveOverStaticDuplicate()
    {
        string hfJson = """
            {
              "items": [
                {
                  "id": "TURZX_Cyberpunk_2077_Vertical_4801920",
                  "name": "Cyberpunk 2077 Vertical",
                  "source": "turzx",
                  "type": "smtheme",
                  "download": "smtheme/TURZX_Cyberpunk 2077 Vertical_4801920.smtheme",
                  "resolution": "480x1920"
                },
                {
                  "id": "Cyberpunk_2077_Vertical",
                  "name": "Cyberpunk 2077 Vertical",
                  "source": "python",
                  "type": "smtheme",
                  "download": "smtheme/Cyberpunk 2077 Vertical.smtheme",
                  "resolution": "480x1920"
                }
              ]
            }
            """;

        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuHfLiveDedup_" + Guid.NewGuid().ToString("N"));
        var items = CloudThemeService.ParseHuggingFaceCatalog(hfJson, tempDir);
        var single = Assert.Single(items);
        Assert.Equal("TURZX LIVE", single.Source);
        Assert.True(single.HasLiveSensors);
        Assert.Equal("Cyberpunk 2077 Vertical", single.Name);
        Assert.Equal(480, single.Width);
        Assert.Equal(1920, single.Height);
    }

    private static byte[] BuildSyntheticZip(params (string Name, byte[] Data, CompressionLevel Level)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data, level) in entries)
            {
                var entry = zip.CreateEntry(name, level);
                using var s = entry.Open();
                s.Write(data, 0, data.Length);
            }
        }
        return ms.ToArray();
    }

    private static HttpResponseMessage ServeRangeResponse(HttpRequestMessage req, byte[] payload)
    {
        var range = req.Headers.Range?.Ranges.FirstOrDefault();
        if (range is null)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

        long total = payload.Length;
        long from, to;
        if (range.From is null && range.To is not null)
        {
            long suffix = Math.Min(total, range.To.Value);
            from = total - suffix;
            to = total - 1;
        }
        else
        {
            from = Math.Clamp(range.From ?? 0, 0, total - 1);
            to = Math.Clamp(range.To ?? (total - 1), from, total - 1);
        }

        byte[] slice = payload.AsSpan((int)from, (int)(to - from + 1)).ToArray();
        var resp = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(slice)
        };
        resp.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, total);
        return resp;
    }

    [Fact]
    public async Task RemoteZipPreviewExtractor_ExtractsPreviewAcrossSingleRangeMultiRangeAndFull200()
    {
        byte[] fakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

        // Case 1: 206 single-range hit (Deflate preview.png inside tail)
        byte[] smallZip = BuildSyntheticZip(
            ("manifest.json", Encoding.UTF8.GetBytes("{\"Resolution\":\"800x480\"}"), CompressionLevel.Optimal),
            ("preview.png", fakePng, CompressionLevel.Optimal));

        int reqCount1 = 0;
        using (var http1 = new HttpClient(new StubHttpHandler(req => { reqCount1++; return ServeRangeResponse(req, smallZip); })))
        {
            byte[]? extracted = await RemoteZipPreviewExtractor.TryExtractPreviewAsync(http1, "https://example.test/small.smtheme");
            Assert.Equal(fakePng, extracted);
            Assert.Equal(1, reqCount1);
        }

        // Case 2: 206 multi-range hit (preview.png before 160 KB tail, Stored)
        byte[] bigPadding = new byte[160 * 1024];
        new Random(42).NextBytes(bigPadding);
        byte[] largeZip = BuildSyntheticZip(
            ("preview.png", fakePng, CompressionLevel.NoCompression),
            ("padding.bin", bigPadding, CompressionLevel.NoCompression));

        int reqCount2 = 0;
        using (var http2 = new HttpClient(new StubHttpHandler(req => { reqCount2++; return ServeRangeResponse(req, largeZip); })))
        {
            byte[]? extracted = await RemoteZipPreviewExtractor.TryExtractPreviewAsync(http2, "https://example.test/large.smtheme");
            Assert.Equal(fakePng, extracted);
            Assert.True(reqCount2 >= 2);
        }

        // Case 3: 200 full-body fallback + background.png fallback when preview.png absent
        byte[] bgOnlyZip = BuildSyntheticZip(
            ("background.png", fakePng, CompressionLevel.Optimal));

        using (var http3 = new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bgOnlyZip) })))
        {
            byte[]? extracted = await RemoteZipPreviewExtractor.TryExtractPreviewAsync(http3, "https://example.test/bgonly.smtheme");
            Assert.Equal(fakePng, extracted);
        }
    }

    [Fact]
    public async Task FetchHuggingFaceCatalogAsync_PrefersCatalogV2_AndFallsBackToCatalogV1()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuCloudV2Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Case 1: catalog_v2.json returns 200 -> does not request catalog.json
            var seenUrls1 = new List<string>();
            using (var http1 = new HttpClient(new StubHttpHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                seenUrls1.Add(url);
                if (url.EndsWith("catalog_v2.json", StringComparison.OrdinalIgnoreCase))
                {
                    string v2Json = """{"items":[{"id":"V2_ITEM","name":"V2Item","download":"themes/V2Item.photo","resolution":"360x960","type":"photo","source":"soeyi"}]}""";
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(v2Json, Encoding.UTF8, "application/json") };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })))
            {
                var svc1 = new CloudThemeService(http1, Path.Combine(tempDir, "t1"), Path.Combine(tempDir, "c1"));
                var items1 = await svc1.FetchHuggingFaceCatalogAsync();
                var item1 = Assert.Single(items1);
                Assert.Equal("V2Item", item1.Name);
                Assert.Single(seenUrls1);
                Assert.EndsWith("catalog_v2.json", seenUrls1[0], StringComparison.OrdinalIgnoreCase);
            }

            // Case 2: catalog_v2.json returns 404 -> falls back cleanly to catalog.json
            var seenUrls2 = new List<string>();
            using (var http2 = new HttpClient(new StubHttpHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                seenUrls2.Add(url);
                if (url.EndsWith("catalog_v2.json", StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (url.EndsWith("catalog.json", StringComparison.OrdinalIgnoreCase))
                {
                    string v1Json = """{"items":[{"id":"V1_ITEM","name":"V1Item","download":"themes/V1Item.photo","resolution":"480x480","type":"photo"}]}""";
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(v1Json, Encoding.UTF8, "application/json") };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })))
            {
                var svc2 = new CloudThemeService(http2, Path.Combine(tempDir, "t2"), Path.Combine(tempDir, "c2"));
                var items2 = await svc2.FetchHuggingFaceCatalogAsync();
                var item2 = Assert.Single(items2);
                Assert.Equal("V1Item", item2.Name);
                Assert.Equal(2, seenUrls2.Count);
                Assert.EndsWith("catalog_v2.json", seenUrls2[0], StringComparison.OrdinalIgnoreCase);
                Assert.EndsWith("catalog.json", seenUrls2[1], StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
