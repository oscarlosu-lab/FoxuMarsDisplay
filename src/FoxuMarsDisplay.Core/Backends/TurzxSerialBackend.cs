using System.Collections.Concurrent;
using System.IO.Ports;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Backends;

/// <summary>
/// Clean-room USB Serial backend for Turing Smart Screen Rev A/B/C (1A86:5722, 1A86:CA21)
/// and TURZX 5" USB Serial (1D6B:0106).
/// </summary>
public sealed class TurzxSerialBackend : IDisplayBackend
{
    public const uint HandleBase = 0x40000000u;

    private static readonly (ushort Vid, ushort Pid)[] SupportedIds =
    [
        (0x1A86, 0x5722),
        (0x1A86, 0xCA21),
        (0x1D6B, 0x0106)
    ];

    private sealed class SerialSession : IDisposable
    {
        public uint Handle;
        public string PortName = string.Empty;
        public SerialPort Port = null!;
        public DisplayDeviceInfo Info = null!;
        public readonly object SyncRoot = new();

        public void Dispose()
        {
            try { if (Port.IsOpen) Port.Close(); } catch { }
            try { Port.Dispose(); } catch { }
        }
    }

    private readonly ConcurrentDictionary<uint, SerialSession> _sessions = new();
    private CancellationTokenSource? _cts;
    private Task? _scanTask;
    private uint _nextHandle = HandleBase + 1;
    private bool _started;
    private bool _disposed;

    public event Action<DisplayDeviceInfo>? DeviceAttached;
    public event Action<uint>? DeviceDetached;

    public IReadOnlyList<DisplayDeviceInfo> Devices =>
        _sessions.Values.Select(s => s.Info).ToArray();

    public bool Start(int logLevel = 0)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TurzxSerialBackend));
        if (_started) return true;
        _started = true;

        ScanPortsOnce();
        _cts = new CancellationTokenSource();
        _scanTask = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
        return _sessions.Count > 0;
    }

    public bool SetResolution(uint handle, int width, int height, int refresh = 60) =>
        _sessions.ContainsKey(handle);

    public int SendBgraFrame(uint handle, SKBitmap bgraBitmap)
    {
        if (!_started || !_sessions.TryGetValue(handle, out var session))
            return -1;

        byte[] header = BuildRevACommand(197, 0, 0, bgraBitmap.Width - 1, bgraBitmap.Height - 1);
        byte[] rgb565 = ConvertBgraToRgb565Le(bgraBitmap);

        lock (session.SyncRoot)
        {
            try
            {
                if (!session.Port.IsOpen)
                    session.Port.Open();
                session.Port.Write(header, 0, header.Length);
                session.Port.Write(rgb565, 0, rgb565.Length);
                return 0;
            }
            catch
            {
                return -3;
            }
        }
    }

    public bool TrySetHardwareBrightness(uint handle, int brightness)
    {
        if (!_started || !_sessions.TryGetValue(handle, out var session))
            return false;

        int level = Math.Clamp((int)Math.Round(255.0 * (100 - Math.Clamp(brightness, 0, 100)) / 100.0), 0, 255);
        byte[] cmd = BuildRevACommand(110, level, 0, 0, 0);
        lock (session.SyncRoot)
        {
            try
            {
                if (!session.Port.IsOpen) return false;
                session.Port.Write(cmd, 0, cmd.Length);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        try { _cts?.Cancel(); } catch { }
        try { _scanTask?.Wait(500); } catch { }
        _cts?.Dispose();
        _cts = null;

        foreach (var kv in _sessions)
        {
            kv.Value.Dispose();
            DeviceDetached?.Invoke(kv.Key);
        }
        _sessions.Clear();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(5000, ct).ConfigureAwait(false); }
            catch { break; }
            if (!ct.IsCancellationRequested)
                ScanPortsOnce();
        }
    }

    private void ScanPortsOnce()
    {
        var matches = UsbDisplayCatalog.FindComPortsForVidPids(SupportedIds);
        var activePorts = new HashSet<string>(matches.Select(m => m.PortName), StringComparer.OrdinalIgnoreCase);

        foreach (var kv in _sessions.ToArray())
        {
            if (!activePorts.Contains(kv.Value.PortName))
            {
                if (_sessions.TryRemove(kv.Key, out var removed))
                {
                    removed.Dispose();
                    DeviceDetached?.Invoke(kv.Key);
                }
            }
        }

        foreach (var (portName, spec, serialKey) in matches)
        {
            if (_sessions.Values.Any(s => s.PortName.Equals(portName, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 2000,
                    RtsEnable = true,
                    DtrEnable = true
                };
                port.Open();

                uint handle = Interlocked.Increment(ref _nextHandle);
                string serial = $"TURZX_{spec.Pid:X4}_{serialKey}";
                var info = new DisplayDeviceInfo(handle, serial, spec.DefaultWidth, spec.DefaultHeight, 60, 0);
                var session = new SerialSession
                {
                    Handle = handle,
                    PortName = portName,
                    Port = port,
                    Info = info
                };
                if (_sessions.TryAdd(handle, session))
                    DeviceAttached?.Invoke(info);
                else
                    session.Dispose();
            }
            catch
            {
                // Ignore busy or inaccessible ports.
            }
        }
    }

    public static byte[] BuildRevACommand(byte cmd, int x, int y, int ex, int ey) =>
    [
        (byte)((x >> 2) & 0xFF),
        (byte)(((x & 3) << 6) | ((y >> 4) & 0x3F)),
        (byte)(((y & 15) << 4) | ((ex >> 6) & 0x0F)),
        (byte)(((ex & 63) << 2) | ((ey >> 8) & 0x03)),
        (byte)(ey & 0xFF),
        cmd
    ];

    public static byte[] ConvertBgraToRgb565Le(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888)
            throw new ArgumentException("Bitmap must be Bgra8888", nameof(bitmap));

        int w = bitmap.Width;
        int h = bitmap.Height;
        byte[] output = new byte[w * h * 2];

        unsafe
        {
            byte* srcBase = (byte*)bitmap.GetPixels().ToPointer();
            int stride = bitmap.RowBytes;
            int outIdx = 0;

            for (int y = 0; y < h; y++)
            {
                byte* row = srcBase + y * stride;
                for (int x = 0; x < w; x++)
                {
                    byte b = row[x * 4 + 0];
                    byte g = row[x * 4 + 1];
                    byte r = row[x * 4 + 2];
                    ushort rgb565 = (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
                    output[outIdx++] = (byte)(rgb565 & 0xFF);
                    output[outIdx++] = (byte)(rgb565 >> 8);
                }
            }
        }
        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
