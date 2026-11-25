using System.Collections.Concurrent;
using System.Diagnostics;
using FoxuMarsDisplay.Core.Backends;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Rendering;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;

namespace FoxuMarsDisplay.Core.Host;

public sealed record ScreenRuntimeStatus(
    uint Handle,
    string SerialNumber,
    int Width,
    int Height,
    string ThemeName,
    string ThemePath,
    int Brightness,
    int Rotation,
    bool IsEnabled,
    bool IsAnimated,
    long FramesSent,
    long FramesSkipped,
    long KeepAlivesSent,
    long BusyDrops,
    long SendErrors);

/// <summary>
/// Central orchestrator for USB LCD displays: manages device attach/detach,
/// sensor polling, SkiaSharp dirty-region rendering, 500ms USB keep-alive
/// (preventing MS9132 2500ms OSD watchdog reboots), fullscreen game pause,
/// slideshows, and Named Pipe IPC.
/// </summary>
public sealed class DisplayHost : IDisposable
{
    public const int KeepAliveIntervalMs = 500;
    private const int BufferBusyReturnCode = -5;

    private sealed class ScreenSession : IDisposable
    {
        public DisplayDeviceInfo Device { get; }
        public ScreenProfile Profile { get; }
        public SkiaThemeRenderer? Renderer { get; set; }
        public DisplayTheme? Theme { get; set; }
        public long FramesSent;
        public long FramesSkipped;
        public long KeepAlivesSent;
        public long BusyDrops;
        public long SendErrors;
        public long LastSendMs;
        public long LastSlideshowSwitchMs;
        public int SlideshowIndex;
        public bool BlackSent;
        public readonly object SyncRoot = new();

        public ScreenSession(DisplayDeviceInfo device, ScreenProfile profile)
        {
            Device = device;
            Profile = profile;
        }

        public void Dispose()
        {
            lock (SyncRoot)
            {
                Renderer?.Dispose();
                Renderer = null;
            }
        }
    }

    private readonly IDisplayBackend _backend;
    private readonly LightweightSensorProvider _sensors;
    private readonly FoxuDisplayConfigStore _configStore;
    private readonly ConcurrentDictionary<uint, ScreenSession> _sessions = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string? _customCacheDir;

    private IpcPipeServer? _ipcServer;
    private CancellationTokenSource? _cts;
    private Task? _renderLoopTask;
    private int _targetFps;
    private bool _dryRun;
    private volatile bool _isFullscreenPaused;

    public FoxuDisplayConfig Config { get; }
    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;
    public bool IsFullscreenPaused => _isFullscreenPaused;
    public SensorSnapshot CurrentSensors => _sensors.Current;

    public event Action? StateChanged;

    public DisplayHost(
        IDisplayBackend? backend = null,
        LightweightSensorProvider? sensors = null,
        FoxuDisplayConfigStore? configStore = null,
        FoxuDisplayConfig? config = null,
        string? customCacheDir = null)
    {
        _backend = backend ?? new CompositeDisplayBackend();
        _sensors = sensors ?? new LightweightSensorProvider();
        _configStore = configStore ?? new FoxuDisplayConfigStore();
        Config = config ?? _configStore.LoadOrImportSoeyi();
        _targetFps = Math.Clamp(Config.TargetFps, 1, 60);
        _customCacheDir = customCacheDir;

        _backend.DeviceAttached += OnDeviceAttached;
        _backend.DeviceDetached += OnDeviceDetached;
    }

    public void Start(bool dryRun = false, bool enableIpc = true)
    {
        if (_cts is not null) return;
        _dryRun = dryRun;
        _cts = new CancellationTokenSource();

        _sensors.Start(1000);

        if (!_dryRun)
        {
            _backend.Start(Config.NativeLogLevel);
            foreach (var dev in _backend.Devices)
                OnDeviceAttached(dev);
        }

        if (enableIpc)
        {
            _ipcServer = new IpcPipeServer(HandleIpcRequest);
            _ipcServer.Start();
        }

        _renderLoopTask = Task.Run(() => RenderLoopAsync(_cts.Token), _cts.Token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _renderLoopTask?.Wait(1500); } catch { }
        _ipcServer?.Dispose();
        _ipcServer = null;

        foreach (var kv in _sessions)
            kv.Value.Dispose();
        _sessions.Clear();

        if (!_dryRun)
            _backend.Stop();

        _sensors.Dispose();
        _cts?.Dispose();
        _cts = null;
    }

    public void SetTargetFps(int fps)
    {
        _targetFps = Math.Clamp(fps, 1, 60);
        Config.TargetFps = _targetFps;
        SaveConfig();
    }

    public void SaveConfig() => _configStore.Save(Config);

    public IReadOnlyList<ScreenRuntimeStatus> GetStatuses()
    {
        var list = new List<ScreenRuntimeStatus>(_sessions.Count);
        foreach (var s in _sessions.Values.OrderBy(x => x.Device.SerialNumber, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(new ScreenRuntimeStatus(
                s.Device.Handle,
                s.Device.SerialNumber,
                s.Device.NativeWidth,
                s.Device.NativeHeight,
                s.Theme?.Name ?? "(Sin tema)",
                s.Profile.ThemePath,
                s.Profile.Brightness,
                s.Renderer?.EffectiveRotation ?? s.Profile.Rotation,
                s.Profile.IsEnabled,
                s.Renderer?.IsAnimated ?? false,
                Interlocked.Read(ref s.FramesSent),
                Interlocked.Read(ref s.FramesSkipped),
                Interlocked.Read(ref s.KeepAlivesSent),
                Interlocked.Read(ref s.BusyDrops),
                Interlocked.Read(ref s.SendErrors)));
        }
        return list;
    }

    public bool ApplyTheme(string serialNumber, string themePath, string? schemeJsonPath = null)
    {
        var session = FindSession(serialNumber);
        if (session is null) return false;

        var theme = UnifiedThemeLoader.Load(themePath, schemeJsonPath);
        if (theme is null) return false;

        lock (session.SyncRoot)
        {
            session.Renderer?.Dispose();
            session.Theme = theme;
            session.Profile.ThemePath = themePath;
            session.Profile.SchemeJsonPath = schemeJsonPath;
            session.Renderer = new SkiaThemeRenderer(
                theme,
                session.Device.NativeWidth,
                session.Device.NativeHeight,
                _targetFps,
                session.Profile.Brightness,
                session.Profile.Rotation,
                _customCacheDir);
            session.BlackSent = false;
            session.LastSendMs = 0; // force immediate send
        }

        SaveConfig();
        StateChanged?.Invoke();
        return true;
    }

    public bool SetBrightness(string serialNumber, int brightness)
    {
        var session = FindSession(serialNumber);
        if (session is null) return false;

        int clamped = Math.Clamp(brightness, 0, 100);
        lock (session.SyncRoot)
        {
            session.Profile.Brightness = clamped;
            if (session.Renderer is not null)
                session.Renderer.Brightness = clamped;
            session.LastSendMs = 0;
        }
        SaveConfig();
        StateChanged?.Invoke();
        return true;
    }

    public bool SetRotation(string serialNumber, int rotation)
    {
        var session = FindSession(serialNumber);
        if (session is null) return false;

        int norm = ((rotation % 4) + 4) % 4;
        lock (session.SyncRoot)
        {
            session.Profile.Rotation = norm;
            session.Renderer?.SetRotation(norm);
            session.LastSendMs = 0;
        }
        SaveConfig();
        StateChanged?.Invoke();
        return true;
    }

    public bool SetScreenEnabled(string serialNumber, bool enabled)
    {
        var session = FindSession(serialNumber);
        if (session is null) return false;

        lock (session.SyncRoot)
        {
            session.Profile.IsEnabled = enabled;
            session.BlackSent = false;
            session.LastSendMs = 0;
        }
        SaveConfig();
        StateChanged?.Invoke();
        return true;
    }

    public byte[]? CapturePreviewPng(string serialNumber, int maxHeight = 240)
    {
        var session = FindSession(serialNumber);
        if (session is null) return null;

        lock (session.SyncRoot)
        {
            return session.Renderer?.CopyPreviewPngBytes(maxHeight);
        }
    }

    public static int ClearAnimCache(string? customCacheDir = null)
    {
        string dir = customCacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FoxuMarsDisplay", "cache", "anim");
        if (!Directory.Exists(dir)) return 0;

        int deleted = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*.sdac"))
        {
            try { File.Delete(f); deleted++; } catch { }
        }
        return deleted;
    }

    private ScreenSession? FindSession(string serialNumber)
    {
        string norm = FoxuDisplayConfigStore.NormalizeSn(serialNumber);
        return _sessions.Values.FirstOrDefault(s =>
            FoxuDisplayConfigStore.NormalizeSn(s.Device.SerialNumber) == norm ||
            s.Device.SerialNumber.Equals(serialNumber, StringComparison.OrdinalIgnoreCase));
    }

    private void OnDeviceAttached(DisplayDeviceInfo device)
    {
        if (!_dryRun)
            _backend.SetResolution(device.Handle, device.NativeWidth, device.NativeHeight, device.RefreshHz);

        var profile = FoxuDisplayConfigStore.GetOrCreateProfile(Config, device.SerialNumber);
        var session = new ScreenSession(device, profile);

        if (!string.IsNullOrWhiteSpace(profile.ThemePath))
        {
            var theme = UnifiedThemeLoader.Load(profile.ThemePath, profile.SchemeJsonPath);
            if (theme is not null)
            {
                session.Theme = theme;
                session.Renderer = new SkiaThemeRenderer(
                    theme,
                    device.NativeWidth,
                    device.NativeHeight,
                    _targetFps,
                    profile.Brightness,
                    profile.Rotation,
                    _customCacheDir);
            }
        }

        _sessions[device.Handle] = session;
        StateChanged?.Invoke();
    }

    private void OnDeviceDetached(uint handle)
    {
        if (_sessions.TryRemove(handle, out var session))
        {
            session.Dispose();
            StateChanged?.Invoke();
        }
    }

    private async Task RenderLoopAsync(CancellationToken ct)
    {
        long lastFullscreenCheckMs = 0;

        while (!ct.IsCancellationRequested)
        {
            long tickStart = _clock.ElapsedMilliseconds;

            if (Config.PauseOnFullscreen && tickStart - lastFullscreenCheckMs >= 1000)
            {
                lastFullscreenCheckMs = tickStart;
                _isFullscreenPaused = FullscreenDetector.IsFullscreenGameActive();
            }
            else if (!Config.PauseOnFullscreen)
            {
                _isFullscreenPaused = false;
            }

            var snapshot = _sensors.Current;

            foreach (var session in _sessions.Values)
            {
                if (ct.IsCancellationRequested) break;
                ProcessScreenTick(session, snapshot, tickStart);
            }

            int frameBudgetMs = Math.Max(16, 1000 / Math.Max(1, _targetFps));
            long elapsed = _clock.ElapsedMilliseconds - tickStart;
            int delayMs = Math.Max(8, frameBudgetMs - (int)elapsed);

            try { await Task.Delay(delayMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ProcessScreenTick(ScreenSession session, SensorSnapshot snapshot, long nowMs)
    {
        lock (session.SyncRoot)
        {
            var renderer = session.Renderer;
            if (renderer is null) return;

            if (!session.Profile.IsEnabled)
            {
                bool dueKeepAlive = !session.BlackSent || (nowMs - session.LastSendMs >= KeepAliveIntervalMs);
                if (dueKeepAlive)
                {
                    if (!session.BlackSent)
                    {
                        renderer.RenderBlackFrame();
                        session.BlackSent = true;
                    }
                    SendFrame(session, renderer, nowMs, isKeepAlive: session.FramesSent > 0);
                }
                else
                {
                    Interlocked.Increment(ref session.FramesSkipped);
                }
                return;
            }

            // Handle slideshow if enabled
            if (session.Profile.SlideshowEnabled &&
                session.Profile.SlideshowThemes.Count > 1 &&
                nowMs - session.LastSlideshowSwitchMs >= Math.Max(5, session.Profile.SlideshowIntervalSec) * 1000L)
            {
                session.LastSlideshowSwitchMs = nowMs;
                session.SlideshowIndex = (session.SlideshowIndex + 1) % session.Profile.SlideshowThemes.Count;
                string nextPath = session.Profile.SlideshowThemes[session.SlideshowIndex];
                var nextTheme = UnifiedThemeLoader.Load(nextPath);
                if (nextTheme is not null)
                {
                    renderer.Dispose();
                    session.Theme = nextTheme;
                    session.Profile.ThemePath = nextPath;
                    renderer = new SkiaThemeRenderer(
                        nextTheme,
                        session.Device.NativeWidth,
                        session.Device.NativeHeight,
                        _targetFps,
                        session.Profile.Brightness,
                        session.Profile.Rotation,
                        _customCacheDir);
                    session.Renderer = renderer;
                    session.LastSendMs = 0;
                }
            }

            bool dirty = !_isFullscreenPaused && renderer.RenderIfDirty(snapshot, nowMs);

            if (dirty)
            {
                SendFrame(session, renderer, nowMs, isKeepAlive: false);
            }
            else if (nowMs - session.LastSendMs >= KeepAliveIntervalMs)
            {
                // Keep-alive resend of already-rendered OutputBitmap prevents MS9132
                // hardware OSD watchdog (2500ms) from rebooting static/paused panels.
                SendFrame(session, renderer, nowMs, isKeepAlive: true);
            }
            else
            {
                Interlocked.Increment(ref session.FramesSkipped);
            }
        }
    }

    private void SendFrame(ScreenSession session, SkiaThemeRenderer renderer, long nowMs, bool isKeepAlive)
    {
        if (_dryRun)
        {
            session.LastSendMs = nowMs;
            Interlocked.Increment(ref session.FramesSent);
            if (isKeepAlive) Interlocked.Increment(ref session.KeepAlivesSent);
            return;
        }

        int rc = _backend.SendBgraFrame(session.Device.Handle, renderer.OutputBitmap);
        if (rc == 0)
        {
            session.LastSendMs = nowMs;
            Interlocked.Increment(ref session.FramesSent);
            if (isKeepAlive) Interlocked.Increment(ref session.KeepAlivesSent);
        }
        else if (rc == BufferBusyReturnCode)
        {
            Interlocked.Increment(ref session.BusyDrops);
        }
        else
        {
            Interlocked.Increment(ref session.SendErrors);
        }
    }

    private IpcResponse HandleIpcRequest(IpcRequest req)
    {
        try
        {
            return req.Command.ToLowerInvariant() switch
            {
                "ping" => new IpcResponse(true, "pong"),
                "get_status" => new IpcResponse(true, null, GetStatuses()),
                "apply_theme" => (!string.IsNullOrWhiteSpace(req.SerialNumber) && !string.IsNullOrWhiteSpace(req.ThemePath))
                    ? (ApplyTheme(req.SerialNumber, req.ThemePath, req.SchemeJsonPath)
                        ? new IpcResponse(true, "Theme applied")
                        : new IpcResponse(false, "Failed to apply theme or screen not found"))
                    : new IpcResponse(false, "Missing serialNumber or themePath"),
                "set_brightness" => (!string.IsNullOrWhiteSpace(req.SerialNumber) && req.Value.HasValue)
                    ? new IpcResponse(SetBrightness(req.SerialNumber, req.Value.Value))
                    : new IpcResponse(false, "Missing serialNumber or value"),
                "set_rotation" => (!string.IsNullOrWhiteSpace(req.SerialNumber) && req.Value.HasValue)
                    ? new IpcResponse(SetRotation(req.SerialNumber, req.Value.Value))
                    : new IpcResponse(false, "Missing serialNumber or value"),
                "set_enabled" => (!string.IsNullOrWhiteSpace(req.SerialNumber) && req.Flag.HasValue)
                    ? new IpcResponse(SetScreenEnabled(req.SerialNumber, req.Flag.Value))
                    : new IpcResponse(false, "Missing serialNumber or flag"),
                "set_fps" => req.Value.HasValue
                    ? RunAndOk(() => SetTargetFps(req.Value.Value))
                    : new IpcResponse(false, "Missing value"),
                _ => new IpcResponse(false, $"Unknown command: {req.Command}")
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse(false, ex.Message);
        }
    }

    private static IpcResponse RunAndOk(Action action)
    {
        action();
        return new IpcResponse(true);
    }

    public void Dispose()
    {
        Stop();
    }
}
