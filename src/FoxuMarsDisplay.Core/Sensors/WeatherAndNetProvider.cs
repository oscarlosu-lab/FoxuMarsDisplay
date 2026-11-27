using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace FoxuMarsDisplay.Core.Sensors;

public sealed record WeatherLocationCandidate(
    string Name,
    string Admin1,
    string Country,
    double Latitude,
    double Longitude)
{
    public string DisplayName => string.Join(", ",
        new[] { Name, Admin1, Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public override string ToString() => DisplayName;
}

/// <summary>
/// Lightweight, zero-API-key background provider for ICMP Ping and Open-Meteo weather telemetry.
/// Never blocks the render loop; fails silently when offline.
/// </summary>
public sealed class WeatherAndNetProvider : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly CancellationTokenSource _cts = new();
    private readonly object _locLock = new();

    private double _lat;
    private double _lon;
    private bool _hasCoords;
    private bool _useManualLocation;
    private double _manualLat;
    private double _manualLon;
    private string _manualCity = string.Empty;
    private volatile bool _weatherResyncNeeded;
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
    public bool IsManualLocation { get { lock (_locLock) return _useManualLocation; } }
    public double Latitude { get { lock (_locLock) return _lat; } }
    public double Longitude { get { lock (_locLock) return _lon; } }

    public WeatherAndNetProvider()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FoxuMarsDisplay/2.1");
    }

    public void ConfigureLocation(string? city, double? latitude, double? longitude)
    {
        lock (_locLock)
        {
            if (latitude.HasValue && longitude.HasValue &&
                double.IsFinite(latitude.Value) && double.IsFinite(longitude.Value) &&
                latitude.Value is >= -90.0 and <= 90.0 &&
                longitude.Value is >= -180.0 and <= 180.0)
            {
                string trimmedCity = (city ?? string.Empty).Trim();
                _useManualLocation = true;
                _manualLat = latitude.Value;
                _manualLon = longitude.Value;
                _manualCity = trimmedCity;
                _lat = latitude.Value;
                _lon = longitude.Value;
                _hasCoords = true;
                WeatherCity = trimmedCity;
            }
            else
            {
                _useManualLocation = false;
                _manualLat = 0;
                _manualLon = 0;
                _manualCity = string.Empty;
                _hasCoords = false;
                WeatherCity = string.Empty;
                HasWeather = false;
            }
        }

        _lastWeatherTickMs = -1_800_000;
        _weatherResyncNeeded = true;
        if (!_disposed && Interlocked.CompareExchange(ref _weatherInFlight, 1, 0) == 0)
        {
            _lastWeatherTickMs = Environment.TickCount64;
            _ = RefreshWeatherAsync(_cts.Token);
        }
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

        if ((_weatherResyncNeeded || now - _lastWeatherTickMs >= 1_200_000) &&
            Interlocked.CompareExchange(ref _weatherInFlight, 1, 0) == 0)
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
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool useManual;
                bool hasCoords;
                double lat;
                double lon;
                lock (_locLock)
                {
                    useManual = _useManualLocation;
                    hasCoords = _hasCoords;
                    lat = useManual ? _manualLat : _lat;
                    lon = useManual ? _manualLon : _lon;
                    if (useManual && !string.IsNullOrEmpty(_manualCity))
                        WeatherCity = _manualCity;
                }
                _weatherResyncNeeded = false;

                if (!useManual && !hasCoords)
                {
                    using var geoResp = await _http.GetAsync("https://ipapi.co/json/", ct).ConfigureAwait(false);
                    if (geoResp.IsSuccessStatusCode)
                    {
                        var geoJson = await geoResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var geoDoc = JsonDocument.Parse(geoJson);
                        var root = geoDoc.RootElement;
                        if (root.TryGetProperty("latitude", out var latEl) && latEl.TryGetDouble(out var detectedLat) &&
                            root.TryGetProperty("longitude", out var lonEl) && lonEl.TryGetDouble(out var detectedLon))
                        {
                            string detectedCity = root.TryGetProperty("city", out var cityEl) && cityEl.ValueKind == JsonValueKind.String
                                ? (cityEl.GetString() ?? string.Empty)
                                : string.Empty;

                            lock (_locLock)
                            {
                                if (!_useManualLocation)
                                {
                                    _lat = detectedLat;
                                    _lon = detectedLon;
                                    _hasCoords = true;
                                    WeatherCity = detectedCity;
                                    lat = detectedLat;
                                    lon = detectedLon;
                                    hasCoords = true;
                                }
                            }
                        }
                    }
                }

                if (_weatherResyncNeeded)
                    continue;

                if (!hasCoords)
                    break;

                string url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0:F4}&longitude={1:F4}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1",
                    lat,
                    lon);

                using var wResp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!wResp.IsSuccessStatusCode)
                {
                    if (_weatherResyncNeeded) continue;
                    break;
                }

                var wJson = await wResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (_weatherResyncNeeded)
                    continue;

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
                if (!_weatherResyncNeeded)
                    break;
            }
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

    public static async Task<List<WeatherLocationCandidate>> SearchCitiesAsync(
        string query,
        string language = "es",
        CancellationToken ct = default)
    {
        var results = new List<WeatherLocationCandidate>();
        string q = (query ?? string.Empty).Trim();
        if (q.Length < 2) return results;

        string lang = string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : language.Trim().ToLowerInvariant();
        if (lang.Length != 2) lang = "en";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FoxuMarsDisplay/2.1");
            string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(q)}&count=6&language={Uri.EscapeDataString(lang)}&format=json";
            using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return results;

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) continue;
                if (!item.TryGetProperty("latitude", out var latEl) || !latEl.TryGetDouble(out double lat)) continue;
                if (!item.TryGetProperty("longitude", out var lonEl) || !lonEl.TryGetDouble(out double lon)) continue;

                string name = nameEl.GetString() ?? string.Empty;
                string admin1 = item.TryGetProperty("admin1", out var aEl) && aEl.ValueKind == JsonValueKind.String ? (aEl.GetString() ?? string.Empty) : string.Empty;
                string country = item.TryGetProperty("country", out var cEl) && cEl.ValueKind == JsonValueKind.String ? (cEl.GetString() ?? string.Empty) : string.Empty;
                results.Add(new WeatherLocationCandidate(name, admin1, country, lat, lon));
            }
        }
        catch
        {
            // Network errors return empty list gracefully.
        }

        return results;
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
