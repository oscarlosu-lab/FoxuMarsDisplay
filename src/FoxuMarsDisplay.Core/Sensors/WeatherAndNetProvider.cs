using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace FoxuMarsDisplay.Core.Sensors;

/// <summary>
/// Lightweight, zero-API-key background provider for ICMP Ping and Open-Meteo weather telemetry.
/// Never blocks the render loop; fails silently when offline.
/// </summary>
public sealed class WeatherAndNetProvider : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly CancellationTokenSource _cts = new();

    private double _lat;
    private double _lon;
    private bool _hasCoords;
    private long _lastPingTickMs = -30_000;
    private long _lastWeatherTickMs = -1_800_000;
    private int _pingInFlight;
    private int _weatherInFlight;
    private bool _disposed;

    public double PingMs { get; private set; }
    public bool HasWeather { get; private set; }
    public double WeatherTempC { get; private set; }
    public double WeatherFeelsLikeC { get; private set; }
    public double WeatherHumidityPct { get; private set; }
    public double WeatherHighC { get; private set; }
    public double WeatherLowC { get; private set; }
    public string WeatherDescription { get; private set; } = string.Empty;
    public string WeatherCity { get; private set; } = string.Empty;

    public WeatherAndNetProvider()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FoxuMarsDisplay/1.2");
    }

    public void TriggerRefreshIfDue()
    {
        if (_disposed) return;
        long now = Environment.TickCount64;

        if (now - _lastPingTickMs >= 10_000 && Interlocked.CompareExchange(ref _pingInFlight, 1, 0) == 0)
        {
            _lastPingTickMs = now;
            _ = RefreshPingAsync(_cts.Token);
        }

        if (now - _lastWeatherTickMs >= 1_200_000 && Interlocked.CompareExchange(ref _weatherInFlight, 1, 0) == 0)
        {
            _lastWeatherTickMs = now;
            _ = RefreshWeatherAsync(_cts.Token);
        }
    }

    private async Task RefreshPingAsync(CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 1200).WaitAsync(ct).ConfigureAwait(false);
            PingMs = reply.Status == IPStatus.Success ? Math.Max(1, reply.RoundtripTime) : 0;
        }
        catch
        {
            PingMs = 0;
        }
        finally
        {
            Interlocked.Exchange(ref _pingInFlight, 0);
        }
    }

    private async Task RefreshWeatherAsync(CancellationToken ct)
    {
        try
        {
            if (!_hasCoords)
            {
                using var geoResp = await _http.GetAsync("https://ipapi.co/json/", ct).ConfigureAwait(false);
                if (geoResp.IsSuccessStatusCode)
                {
                    var geoJson = await geoResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var geoDoc = JsonDocument.Parse(geoJson);
                    var root = geoDoc.RootElement;
                    if (root.TryGetProperty("latitude", out var latEl) && latEl.TryGetDouble(out var lat) &&
                        root.TryGetProperty("longitude", out var lonEl) && lonEl.TryGetDouble(out var lon))
                    {
                        _lat = lat;
                        _lon = lon;
                        _hasCoords = true;
                        if (root.TryGetProperty("city", out var cityEl) && cityEl.ValueKind == JsonValueKind.String)
                            WeatherCity = cityEl.GetString() ?? string.Empty;
                    }
                }
            }

            if (!_hasCoords) return;

            string url = string.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0:F4}&longitude={1:F4}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1",
                _lat,
                _lon);

            using var wResp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!wResp.IsSuccessStatusCode) return;

            var wJson = await wResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var wDoc = JsonDocument.Parse(wJson);
            var wRoot = wDoc.RootElement;

            if (wRoot.TryGetProperty("current", out var cur))
            {
                if (cur.TryGetProperty("temperature_2m", out var tEl) && tEl.TryGetDouble(out var t))
                    WeatherTempC = t;
                if (cur.TryGetProperty("apparent_temperature", out var aEl) && aEl.TryGetDouble(out var a))
                    WeatherFeelsLikeC = a;
                if (cur.TryGetProperty("relative_humidity_2m", out var hEl) && hEl.TryGetDouble(out var h))
                    WeatherHumidityPct = h;
                if (cur.TryGetProperty("weather_code", out var cEl) && cEl.TryGetInt32(out var code))
                    WeatherDescription = MapWmoCode(code);
            }

            if (wRoot.TryGetProperty("daily", out var daily))
            {
                if (daily.TryGetProperty("temperature_2m_max", out var hiArr) && hiArr.ValueKind == JsonValueKind.Array && hiArr.GetArrayLength() > 0)
                    WeatherHighC = hiArr[0].GetDouble();
                if (daily.TryGetProperty("temperature_2m_min", out var loArr) && loArr.ValueKind == JsonValueKind.Array && loArr.GetArrayLength() > 0)
                    WeatherLowC = loArr[0].GetDouble();
            }

            HasWeather = true;
        }
        catch
        {
            // Offline or rate-limited: preserve last known state without throwing.
        }
        finally
        {
            Interlocked.Exchange(ref _weatherInFlight, 0);
        }
    }

    public static string MapWmoCode(int code) => code switch
    {
        0 => "Clear",
        1 or 2 => "Partly Cloudy",
        3 => "Cloudy",
        45 or 48 => "Fog",
        51 or 53 or 55 or 56 or 57 => "Drizzle",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "Rain",
        71 or 73 or 75 or 77 or 85 or 86 => "Snow",
        95 or 96 or 99 => "Storm",
        _ => "Fair"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
        _http.Dispose();
    }
}
