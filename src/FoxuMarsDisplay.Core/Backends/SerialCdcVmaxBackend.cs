using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Backends;

/// <summary>
/// Clean-room USB-CDC Serial (DLS) backend for HL-VMAX / SOEYI CH1L / VMAXC170 / Solarmax / Delta displays:
/// VID_33C3 PID_F101, PID_7788, PID_7791, PID_7792.
/// </summary>
public sealed class SerialCdcVmaxBackend : IDisplayBackend
{
    public const uint HandleBase = 0x30000000u;
    public static readonly byte[] HandshakeBytes = [0xF0, 0xA5, 0x5A, 0x0F];

    private static readonly (ushort Vid, ushort Pid)[] SupportedIds =
    [
        (0x33C3, 0xF101),
        (0x33C3, 0x7788),
        (0x33C3, 0x7791),
        (0x33C3, 0x7792)
    ];

    private sealed class PortSession : IDisposable
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

    private readonly ConcurrentDictionary<uint, PortSession> _sessions = new();
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
        if (_disposed) throw new ObjectDisposedException(nameof(SerialCdcVmaxBackend));
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

        byte[] jpeg = EncodeConstrainedJpeg(bgraBitmap);
        if (jpeg.Length == 0)
            return -2;

        lock (session.SyncRoot)
        {
            try
            {
                if (!session.Port.IsOpen)
                    session.Port.Open();
                session.Port.Write(jpeg, 0, jpeg.Length);
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

        byte[] cmd = BuildBrightnessCommand(brightness);
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
                    ReadTimeout = 400,
                    WriteTimeout = 1500,
                    DtrEnable = true
                };
                port.Open();
                port.Write(HandshakeBytes, 0, HandshakeBytes.Length);
                Thread.Sleep(60);

                string serial = string.Empty;
                int avail = port.BytesToRead;
                if (avail > 0)
                {
                    byte[] buf = new byte[Math.Min(128, avail)];
                    int read = port.Read(buf, 0, buf.Length);
                    serial = Encoding.ASCII.GetString(buf, 0, read).Trim('\0', '\r', '\n', ' ');
                }

                if (string.IsNullOrWhiteSpace(serial))
                    serial = $"VMAX_{spec.Pid:X4}_{serialKey}";

                var (w, h) = ParseVmaxSerialGeometry(serial, spec.DefaultWidth, spec.DefaultHeight);
                uint handle = Interlocked.Increment(ref _nextHandle);
                var info = new DisplayDeviceInfo(handle, serial, w, h, 60, h > w && serial.StartsWith("VMAXA", StringComparison.OrdinalIgnoreCase) ? 2 : 0);

                var session = new PortSession
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
                // Port busy (held by another app) or transient unplug.
            }
        }
    }

    public static (int Width, int Height) ParseVmaxSerialGeometry(string serial, int fallbackW = 320, int fallbackH = 1480)
    {
        if (!string.IsNullOrWhiteSpace(serial))
        {
            var m = Regex.Match(serial, @"(\d{3,4})\s*[*_xX]\s*(\d{3,4})");
            if (m.Success &&
                int.TryParse(m.Groups[1].Value, out int w) &&
                int.TryParse(m.Groups[2].Value, out int h) &&
                w >= 100 && h >= 100 && w <= 4096 && h <= 4096)
            {
                return (w, h);
            }
        }
        return (fallbackW, fallbackH);
    }

    public static byte[] BuildBrightnessCommand(int brightness) =>
        [0xAA, 0xBB, (byte)Math.Clamp(brightness, 0, 100), 0xCC, 0xDD];

    public static byte[] EncodeConstrainedJpeg(SKBitmap bitmap, int maxBytes = 98_000, int initialQuality = 84)
    {
        using var img = SKImage.FromBitmap(bitmap);
        foreach (int q in new[] { initialQuality, 72, 60, 48, 36 })
        {
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, q);
            if (data != null && (data.Size <= maxBytes || q == 36))
                return data.ToArray();
        }
        return [];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
