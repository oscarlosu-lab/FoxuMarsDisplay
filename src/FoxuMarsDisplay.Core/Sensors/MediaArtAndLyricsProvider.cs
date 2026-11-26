using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Sensors;

/// <summary>
/// Fetches and caches album artwork (iTunes Search with Deezer fallback) and synced LRC lyrics (LRCLIB)
/// when the active track changes, with configurable timing offset, query cleanup, and accent extraction.
/// </summary>
public sealed class MediaArtAndLyricsProvider : IDisposable
{
    private static readonly Regex LrcTagRegex = new(@"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex TrackNoiseRegex = new(
        @"\s*(?:\((?:official\s*(?:music\s*)?(?:video|audio|lyric\s*video)|lyrics?|visualizer|hd|4k)\)|\[(?:official\s*(?:music\s*)?(?:video|audio)|lyrics?|visualizer)\]|\s*[-|]\s*youtube(?:\s*music)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly CancellationTokenSource _cts = new();

    private string _activeTrackKey = string.Empty;
    private long _trackStartedMs;
    private int _inFlight;
    private int _lyricsOffsetMs;
    private IReadOnlyList<(long TimeMs, string Text)> _parsedLyrics = [];
    private byte[]? _albumArtBytes;
    private bool _disposed;

    public int LyricsOffsetMs
    {
        get => Volatile.Read(ref _lyricsOffsetMs);
        set => Volatile.Write(ref _lyricsOffsetMs, Math.Clamp(value, -10_000, 10_000));
    }

    public MediaArtAndLyricsProvider()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FoxuMarsDisplay/2.1");
    }

    public void UpdateAndGet(string artist, string songTitle, out byte[]? albumArtBytes, out string currentLyricLine)
    {
        string key = $"{artist}|{songTitle}".Trim();
        if (string.IsNullOrWhiteSpace(songTitle))
        {
            _activeTrackKey = string.Empty;
            _parsedLyrics = [];
            _albumArtBytes = null;
            albumArtBytes = null;
            currentLyricLine = string.Empty;
            return;
        }

        if (!string.Equals(key, _activeTrackKey, StringComparison.OrdinalIgnoreCase))
        {
            _activeTrackKey = key;
            _trackStartedMs = Environment.TickCount64;
            _parsedLyrics = [];
            _albumArtBytes = null;

            if (!_disposed && Interlocked.CompareExchange(ref _inFlight, 1, 0) == 0)
                _ = FetchTrackMetadataAsync(artist, songTitle, key, _cts.Token);
        }

        albumArtBytes = _albumArtBytes;
        long posMs = Math.Max(0, (Environment.TickCount64 - _trackStartedMs) + LyricsOffsetMs);
        currentLyricLine = GetLineAt(_parsedLyrics, posMs);
    }

    public static string CleanTrackQuery(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();
        for (int i = 0; i < 3; i++)
        {
            string next = TrackNoiseRegex.Replace(s, string.Empty).Trim();
            if (next.Length == s.Length) break;
            s = next;
        }
        return s;
    }

    private async Task FetchTrackMetadataAsync(string artist, string songTitle, string trackKey, CancellationToken ct)
    {
        string cleanArtist = CleanTrackQuery(artist);
        string cleanTitle = CleanTrackQuery(songTitle);
        try
        {
            string lrcUrl = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(cleanArtist)}&track_name={Uri.EscapeDataString(cleanTitle)}";
            using (var resp = await _http.GetAsync(lrcUrl, ct).ConfigureAwait(false))
            {
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("syncedLyrics", out var synced) &&
                        synced.ValueKind == JsonValueKind.String)
                    {
                        var lines = ParseLrc(synced.GetString() ?? string.Empty);
                        if (string.Equals(_activeTrackKey, trackKey, StringComparison.OrdinalIgnoreCase))
                            _parsedLyrics = lines;
                    }
                    else if (doc.RootElement.TryGetProperty("plainLyrics", out var plain) &&
                             plain.ValueKind == JsonValueKind.String)
                    {
                        string first = (plain.GetString() ?? string.Empty)
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .FirstOrDefault() ?? string.Empty;
                        if (!string.IsNullOrEmpty(first) && string.Equals(_activeTrackKey, trackKey, StringComparison.OrdinalIgnoreCase))
                            _parsedLyrics = [(0, first)];
                    }
                }
            }

            string query = string.IsNullOrWhiteSpace(cleanArtist) ? cleanTitle : $"{cleanArtist} {cleanTitle}";
            bool gotArt = false;
            string artUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&media=music&limit=1";
            using (var artResp = await _http.GetAsync(artUrl, ct).ConfigureAwait(false))
            {
                if (artResp.IsSuccessStatusCode)
                {
                    var json = await artResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results) &&
                        results.ValueKind == JsonValueKind.Array &&
                        results.GetArrayLength() > 0 &&
                        results[0].TryGetProperty("artworkUrl100", out var urlEl) &&
                        urlEl.ValueKind == JsonValueKind.String)
                    {
                        string? imgUrl = urlEl.GetString();
                        if (!string.IsNullOrWhiteSpace(imgUrl))
                        {
                            byte[] bytes = await _http.GetByteArrayAsync(imgUrl, ct).ConfigureAwait(false);
                            if (bytes.Length > 100 && string.Equals(_activeTrackKey, trackKey, StringComparison.OrdinalIgnoreCase))
                            {
                                _albumArtBytes = bytes;
                                gotArt = true;
                            }
                        }
                    }
                }
            }

            if (!gotArt)
            {
                string deezerUrl = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}&limit=1";
                using var dzResp = await _http.GetAsync(deezerUrl, ct).ConfigureAwait(false);
                if (dzResp.IsSuccessStatusCode)
                {
                    var dzJson = await dzResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var dzDoc = JsonDocument.Parse(dzJson);
                    if (dzDoc.RootElement.TryGetProperty("data", out var dataEl) &&
                        dataEl.ValueKind == JsonValueKind.Array &&
                        dataEl.GetArrayLength() > 0 &&
                        dataEl[0].TryGetProperty("album", out var albumEl))
                    {
                        string? coverUrl = albumEl.TryGetProperty("cover_medium", out var cm) ? cm.GetString()
                            : albumEl.TryGetProperty("cover_big", out var cb) ? cb.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(coverUrl))
                        {
                            byte[] bytes = await _http.GetByteArrayAsync(coverUrl, ct).ConfigureAwait(false);
                            if (bytes.Length > 100 && string.Equals(_activeTrackKey, trackKey, StringComparison.OrdinalIgnoreCase))
                                _albumArtBytes = bytes;
                        }
                    }
                }
            }
        }
        catch
        {
            // Offline or API miss: leave graceful empty state.
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    public static IReadOnlyList<(long TimeMs, string Text)> ParseLrc(string lrcText)
    {
        var list = new List<(long TimeMs, string Text)>();
        if (string.IsNullOrWhiteSpace(lrcText))
            return list;

        foreach (var rawLine in lrcText.Split('\n'))
        {
            var matches = LrcTagRegex.Matches(rawLine);
            if (matches.Count == 0) continue;

            string text = LrcTagRegex.Replace(rawLine, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            foreach (Match m in matches)
            {
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int min)) continue;
                if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sec)) continue;
                int ms = 0;
                if (m.Groups[3].Success)
                {
                    string frac = m.Groups[3].Value.PadRight(3, '0')[..3];
                    int.TryParse(frac, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
                }
                long totalMs = (min * 60_000L) + (sec * 1000L) + ms;
                list.Add((totalMs, text));
            }
        }

        list.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return list;
    }

    public static string GetLineAt(IReadOnlyList<(long TimeMs, string Text)> lines, long positionMs)
    {
        if (lines.Count == 0) return string.Empty;
        string best = lines[0].Text;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TimeMs <= positionMs)
                best = lines[i].Text;
            else
                break;
        }
        return best;
    }

    public static SKColor? ExtractDominantAccentColor(byte[]? imageBytes)
    {
        if (imageBytes is null || imageBytes.Length < 16)
            return null;

        try
        {
            using var src = SKBitmap.Decode(imageBytes);
            if (src is null || src.Width <= 0 || src.Height <= 0)
                return null;

            using var thumb = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(thumb))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Low })
            {
                canvas.DrawBitmap(src, new SKRect(0, 0, 16, 16), paint);
            }

            long rSum = 0, gSum = 0, bSum = 0, weightSum = 0;
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    var c = thumb.GetPixel(x, y);
                    int maxC = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                    int minC = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                    int lum = (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000;
                    if (lum < 22 || lum > 240) continue;

                    int sat = maxC - minC;
                    if (sat < 18) continue;

                    int w = 1 + sat / 16;
                    rSum += (long)c.Red * w;
                    gSum += (long)c.Green * w;
                    bSum += (long)c.Blue * w;
                    weightSum += w;
                }
            }

            if (weightSum == 0)
                return null;

            return new SKColor(
                (byte)Math.Clamp(rSum / weightSum, 0, 255),
                (byte)Math.Clamp(gSum / weightSum, 0, 255),
                (byte)Math.Clamp(bSum / weightSum, 0, 255),
                255);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
        _http.Dispose();
    }
}
