using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoxuMarsDisplay.Core.Sensors;

namespace FoxuMarsDisplay.Core.Host;

/// <summary>
/// Lightweight loopback HTTP REST + offline Web UI server for local remote control
/// of connected displays without requiring elevated URL ACL registration.
/// </summary>
public sealed class LocalHttpRemoteServer : IDisposable
{
    public sealed record RemoteCommand(string Action, string? Serial, int? IntValue, bool? FlagValue, string? StringValue);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<IReadOnlyList<ScreenRuntimeStatus>> _getScreens;
    private readonly Func<SensorSnapshot> _getSensors;
    private readonly Func<RemoteCommand, bool> _onCommand;
    private readonly int _port;
    private readonly string _token;
    private readonly string _appVersion;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;

    public int Port => _port;
    public string BaseUrl => $"http://127.0.0.1:{_port}/";
    public bool IsListening => _listener?.IsListening == true;

    public LocalHttpRemoteServer(
        int port,
        string? token,
        string appVersion,
        Func<IReadOnlyList<ScreenRuntimeStatus>> getScreens,
        Func<SensorSnapshot> getSensors,
        Func<RemoteCommand, bool> onCommand)
    {
        _port = Math.Clamp(port, 1024, 65535);
        _token = token?.Trim() ?? string.Empty;
        _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "2.1.1" : appVersion;
        _getScreens = getScreens;
        _getSensors = getSensors;
        _onCommand = onCommand;
    }

    public bool Start()
    {
        if (_disposed || IsListening) return IsListening;

        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(BaseUrl);
            listener.Start();
            _listener = listener;
            _cts = new CancellationTokenSource();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return true;
        }
        catch
        {
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        try { _acceptTask?.Wait(500); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _acceptTask = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleContextAsync(ctx), ct);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            string path = req.Url?.AbsolutePath ?? "/";

            if (path.Equals("/", StringComparison.Ordinal) && req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(resp, 200, "text/html; charset=utf-8", BuildDashboardHtml()).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !IsAuthorized(req))
            {
                await WriteResponseAsync(resp, 401, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Unauthorized\"}").ConfigureAwait(false);
                return;
            }

            if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase) && req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                var payload = new
                {
                    ok = true,
                    version = _appVersion,
                    screens = _getScreens(),
                    sensors = _getSensors()
                };
                await WriteResponseAsync(resp, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(payload, JsonOpts)).ConfigureAwait(false);
                return;
            }

            if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                string body = await reader.ReadToEndAsync().ConfigureAwait(false);
                using var doc = string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}") : JsonDocument.Parse(body);
                var root = doc.RootElement;

                string? serial = root.TryGetProperty("serial", out var sEl) && sEl.ValueKind == JsonValueKind.String ? sEl.GetString() : null;
                int? intVal = root.TryGetProperty("value", out var vEl) && vEl.TryGetInt32(out int iv) ? iv : null;
                bool? flagVal = root.TryGetProperty("flag", out var fEl) && (fEl.ValueKind == JsonValueKind.True || fEl.ValueKind == JsonValueKind.False) ? fEl.GetBoolean() : null;
                string? strVal = root.TryGetProperty("themePath", out var tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString() : null;

                string action = path.ToLowerInvariant() switch
                {
                    "/api/screen/brightness" => "set_brightness",
                    "/api/screen/rotation" => "set_rotation",
                    "/api/screen/enabled" => "set_enabled",
                    "/api/screen/theme" => "apply_theme",
                    "/api/fps" => "set_fps",
                    _ => string.Empty
                };

                if (string.IsNullOrEmpty(action))
                {
                    await WriteResponseAsync(resp, 404, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Not found\"}").ConfigureAwait(false);
                    return;
                }

                bool ok = _onCommand(new RemoteCommand(action, serial, intVal, flagVal, strVal));
                await WriteResponseAsync(resp, ok ? 200 : 400, "application/json; charset=utf-8", ok ? "{\"ok\":true}" : "{\"ok\":false}").ConfigureAwait(false);
                return;
            }

            await WriteResponseAsync(resp, 404, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Not found\"}").ConfigureAwait(false);
        }
        catch
        {
            try
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
            }
            catch { }
        }
    }

    private bool IsAuthorized(HttpListenerRequest req)
    {
        if (string.IsNullOrEmpty(_token))
            return true;

        string? candidate = null;
        string? authHeader = req.Headers["Authorization"];
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            candidate = authHeader["Bearer ".Length..].Trim();
        else if (!string.IsNullOrWhiteSpace(req.QueryString["token"]))
            candidate = req.QueryString["token"]!.Trim();

        if (string.IsNullOrEmpty(candidate))
            return false;

        byte[] expectedBytes = Encoding.UTF8.GetBytes(_token);
        byte[] actualBytes = Encoding.UTF8.GetBytes(candidate);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static async Task WriteResponseAsync(HttpListenerResponse resp, int statusCode, string contentType, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        resp.StatusCode = statusCode;
        resp.ContentType = contentType;
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        resp.Close();
    }

    private string BuildDashboardHtml()
    {
        const string template = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1" />
              <title>FoxuMarsDisplay Remote</title>
              <style>
                body { margin: 0; font-family: system-ui, Segoe UI, sans-serif; background: #0B0A0F; color: #F2EFF7; padding: 20px; }
                h1 { font-size: 18px; color: #FF6419; margin: 0 0 16px; letter-spacing: .5px; }
                .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 14px; }
                .card { background: #171421; border: 1px solid #2E273F; border-radius: 10px; padding: 14px; }
                .muted { color: #9D95B0; font-size: 12px; }
                .row { display: flex; align-items: center; justify-content: space-between; gap: 10px; margin-top: 10px; }
                button { background: #261F33; color: #F2EFF7; border: 1px solid #FF6419; border-radius: 6px; padding: 6px 10px; cursor: pointer; }
                input[type=range] { flex: 1; accent-color: #FF6419; }
              </style>
            </head>
            <body>
              <h1>FOXU // REMOTE CONTROL v{{APP_VERSION}}</h1>
              <div id="sensors" class="card" style="margin-bottom:14px;">Loading telemetry...</div>
              <div id="screens" class="grid"></div>
              <script>
                const tok = new URLSearchParams(location.search).get('token') || '';
                const hdrs = tok ? { 'Authorization': 'Bearer ' + tok, 'Content-Type': 'application/json' } : { 'Content-Type': 'application/json' };
                async function post(url, body) {
                  await fetch(url, { method: 'POST', headers: hdrs, body: JSON.stringify(body) });
                  setTimeout(refresh, 150);
                }
                async function refresh() {
                  try {
                    const r = await fetch('/api/status', { headers: hdrs });
                    if (!r.ok) { document.getElementById('sensors').textContent = 'Unauthorized (add ?token=...)'; return; }
                    const d = await r.json();
                    const s = d.sensors || {};
                    document.getElementById('sensors').innerHTML =
                      `<b>CPU:</b> ${(s.cpuTemp||0).toFixed(0)}°C (${(s.cpuUsage||0).toFixed(0)}%) &nbsp;|&nbsp; ` +
                      `<b>GPU:</b> ${(s.gpuTemp||0).toFixed(0)}°C (${(s.gpuUsage||0).toFixed(0)}%) &nbsp;|&nbsp; ` +
                      `<b>RAM:</b> ${(s.memoryUsagePct||0).toFixed(0)}%`;
                    const box = document.getElementById('screens');
                    box.innerHTML = (d.screens || []).map(sc => `
                      <div class="card">
                        <div><b>${sc.serialNumber}</b> <span class="muted">(${sc.width}x${sc.height})</span></div>
                        <div class="muted">${sc.themeName}</div>
                        <div class="row">
                          <span>Brightness: ${sc.brightness}%</span>
                          <input type="range" min="0" max="100" value="${sc.brightness}" onchange="post('/api/screen/brightness',{serial:'${sc.serialNumber}',value:Number(this.value)})" />
                        </div>
                        <div class="row">
                          <button onclick="post('/api/screen/rotation',{serial:'${sc.serialNumber}',value:${(sc.rotation + 1) % 4}})">Rotate 90°</button>
                          <button onclick="post('/api/screen/enabled',{serial:'${sc.serialNumber}',flag:${!sc.isEnabled}})">${sc.isEnabled ? 'Turn Off' : 'Turn On'}</button>
                        </div>
                      </div>`).join('') || '<div class="card muted">No USB displays connected.</div>';
                  } catch (e) {}
                }
                refresh();
                setInterval(refresh, 2000);
              </script>
            </body>
            </html>
            """;
        return template.Replace("{{APP_VERSION}}", WebUtility.HtmlEncode(_appVersion));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
