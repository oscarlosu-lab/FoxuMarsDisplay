using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FoxuMarsDisplay.Core.Sensors;

/// <summary>
/// Fetches and caches album artwork (iTunes Search) and synced LRC lyrics (LRCLIB)
/// when the active track changes, with zero API keys and offline safety.
/// </summary>
public sealed class MediaArtAndLyricsProvider : IDisposable
{
    private static readonly Regex LrcTagRegex = new(@"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly CancellationTokenSource _cts = new();

    private string _activeTrackKey = string.Empty;
    private long _trackStartedMs;
    private int _inFlight;
    private IReadOnlyList<(long TimeMs, string Text)> _parsedLyrics = [];
    private byte[]? _albumArtBytes;
    private bool _disposed;

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
        long posMs = Math.Max(0, Environment.TickCount64 - _trackStartedMs);
        currentLyricLine = GetLineAt(_parsedLyrics, posMs);
    }

    private async Task FetchTrackMetadataAsync(string artist, string songTitle, string trackKey, CancellationToken ct)
    {
        try
        {
            string lrcUrl = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(songTitle)}";
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

            string query = string.IsNullOrWhiteSpace(artist) ? songTitle : $"{artist} {songTitle}";
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
        _http.Dispose();
    }
}
