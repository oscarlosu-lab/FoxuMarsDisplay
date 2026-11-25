using System.Text.Json;

namespace FoxuMarsDisplay.Core.Sensors;

/// <summary>
/// Optional throttled reader for AI coding assistant quota telemetry
/// from %LOCALAPPDATA%\FoxuMarsDisplay\ai_quota.json.
/// </summary>
public sealed class AiQuotaSensorProvider
{
    private readonly string _quotaPath;
    private long _lastReadMs = -60_000;

    public double ClaudePct { get; private set; }
    public double CodexPct { get; private set; }
    public double GeminiPct { get; private set; }
    public int ActiveTasks { get; private set; }

    public AiQuotaSensorProvider(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _quotaPath = customPath;
        }
        else
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _quotaPath = Path.Combine(localAppData, "FoxuMarsDisplay", "ai_quota.json");
        }
    }

    public void SampleIfDue()
    {
        long now = Environment.TickCount64;
        if (now - _lastReadMs < 15_000)
            return;
        _lastReadMs = now;

        if (!File.Exists(_quotaPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_quotaPath));
            var r = doc.RootElement;
            if (r.TryGetProperty("claudePct", out var c) && c.TryGetDouble(out var cv)) ClaudePct = Math.Clamp(cv, 0, 100);
            if (r.TryGetProperty("codexPct", out var x) && x.TryGetDouble(out var xv)) CodexPct = Math.Clamp(xv, 0, 100);
            if (r.TryGetProperty("geminiPct", out var g) && g.TryGetDouble(out var gv)) GeminiPct = Math.Clamp(gv, 0, 100);
            if (r.TryGetProperty("activeTasks", out var t) && t.TryGetInt32(out var tv)) ActiveTasks = Math.Max(0, tv);
        }
        catch
        {
            // Ignore malformed or concurrently-written JSON.
        }
    }
}
