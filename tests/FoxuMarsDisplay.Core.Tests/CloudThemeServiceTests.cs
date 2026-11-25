using System.Net;
using System.Net.Http;
using System.Text;
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
}
