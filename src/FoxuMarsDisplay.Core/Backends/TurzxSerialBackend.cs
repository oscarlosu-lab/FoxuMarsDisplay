using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Backends;

/// <summary>
/// Clean-room USB Serial backend for Turing Smart Screen Rev A/B/C (1A86:5722, 1A86:CA21)
/// and TURZX 5" USB Serial (1D6B:0106), using direct Win32 serial handles and tile-based
/// differential updates for high frame rates.
/// </summary>
public sealed class TurzxSerialBackend : IDisplayBackend
{
    public const uint HandleBase = 0x40000000u;
    private const int DefaultDiffBudgetBytes = 32_768;

    private static readonly (ushort Vid, ushort Pid)[] SupportedIds =
    [
        (0x1A86, 0x5722),
        (0x1A86, 0xCA21),
        (0x1D6B, 0x0106)
    ];

    private sealed class Win32SerialLink : IDisposable
    {
        private const int WriteChunkBytes = 16_384;
        private SafeFileHandle? _handle;

        public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

        public void Open(string portName)
        {
            Close();
            var h = CreateFile(
                @"\\.\" + portName,
                0xC0000000u,
                0,
                IntPtr.Zero,
                3,
                0,
                IntPtr.Zero);

            if (h.IsInvalid)
                throw new IOException($"Cannot open {portName} (Win32 {Marshal.GetLastWin32Error()})");

            try
            {
                var dcb = new Dcb { DCBlength = (uint)Marshal.SizeOf<Dcb>() };
                if (!GetCommState(h, ref dcb))
                    throw new IOException($"GetCommState failed ({Marshal.GetLastWin32Error()})");

                dcb.BaudRate = 115200;
                dcb.Flags = 0x0001 | 0x0004 | 0x0010 | 0x2000;
                dcb.ByteSize = 8;
                dcb.Parity = 0;
                dcb.StopBits = 0;

                if (!SetCommState(h, ref dcb))
                    throw new IOException($"SetCommState failed ({Marshal.GetLastWin32Error()})");

                var timeouts = new CommTimeouts
                {
                    ReadIntervalTimeout = uint.MaxValue,
                    WriteTotalTimeoutConstant = 2000
                };
                if (!SetCommTimeouts(h, ref timeouts))
                    throw new IOException($"SetCommTimeouts failed ({Marshal.GetLastWin32Error()})");

                PurgeComm(h, 0x0004 | 0x0008);
            }
            catch
            {
                h.Dispose();
                throw;
            }

            _handle = h;
        }

        public unsafe void Write(ReadOnlySpan<byte> data)
        {
            if (!IsOpen || _handle is null)
                throw new IOException("Serial link is closed.");

            int offset = 0;
            fixed (byte* ptr = data)
            {
                while (offset < data.Length)
                {
                    int chunk = Math.Min(WriteChunkBytes, data.Length - offset);
                    if (!WriteFile(_handle, ptr + offset, (uint)chunk, out uint written, IntPtr.Zero) || written == 0)
                        throw new IOException($"WriteFile failed ({Marshal.GetLastWin32Error()})");
                    offset += (int)written;
                }
            }
        }

        public void Close()
        {
            _handle?.Dispose();
            _handle = null;
        }

        public void Dispose() => Close();

        [StructLayout(LayoutKind.Sequential)]
        private struct Dcb
        {
            public uint DCBlength, BaudRate, Flags;
            public ushort wReserved, XonLim, XoffLim;
            public byte ByteSize, Parity, StopBits;
            public sbyte XonChar, XoffChar, ErrorChar, EofChar, EvtChar;
            public ushort wReserved1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CommTimeouts
        {
            public uint ReadIntervalTimeout, ReadTotalTimeoutMultiplier, ReadTotalTimeoutConstant, WriteTotalTimeoutMultiplier, WriteTotalTimeoutConstant;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetCommState(SafeFileHandle h, ref Dcb dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommState(SafeFileHandle h, ref Dcb dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommTimeouts(SafeFileHandle h, ref CommTimeouts timeouts);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PurgeComm(SafeFileHandle h, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern unsafe bool WriteFile(SafeFileHandle h, byte* buf, uint count, out uint written, IntPtr overlapped);
    }

    private sealed class SerialSession : IDisposable
    {
        public uint Handle;
        public string PortName = string.Empty;
        public Win32SerialLink Link = new();
        public DisplayDeviceInfo Info = null!;
        public SerialTileDiffEngine? Diff;
        public readonly object SyncRoot = new();

        public void Dispose() => Link.Dispose();
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

    public bool SetResolution(uint handle, int width, int height, int refresh = 60)
    {
        if (!_sessions.TryGetValue(handle, out var session))
            return false;

        lock (session.SyncRoot)
        {
            session.Diff?.Invalidate();
        }
        return true;
    }

    public int SendBgraFrame(uint handle, SKBitmap bgraBitmap)
    {
        if (!_started || !_sessions.TryGetValue(handle, out var session))
            return -1;

        ushort[] rgb565 = SerialTileDiffEngine.ConvertBgraToRgb565Array(bgraBitmap);

        lock (session.SyncRoot)
        {
            try
            {
                if (!session.Link.IsOpen)
                {
                    session.Link.Open(session.PortName);
                    session.Diff?.Invalidate();
                }

                if (session.Diff is null ||
                    session.Diff.Width != bgraBitmap.Width ||
                    session.Diff.Height != bgraBitmap.Height)
                {
                    session.Diff = new SerialTileDiffEngine(bgraBitmap.Width, bgraBitmap.Height);
                }

                var dirtyRects = session.Diff.ComputeDirtyRects(rgb565, DefaultDiffBudgetBytes, out _);
                if (dirtyRects.Count == 0)
                    return 0;

                foreach (var rect in dirtyRects)
                {
                    byte[] cmd = BuildRevACommand(197, rect.X, rect.Y, rect.X + rect.Width - 1, rect.Y + rect.Height - 1);
                    byte[] payload = SerialTileDiffEngine.ExtractRectRgb565Le(rgb565, bgraBitmap.Width, rect);
                    session.Link.Write(cmd);
                    session.Link.Write(payload);
                }
                return 0;
            }
            catch
            {
                session.Link.Close();
                session.Diff?.Invalidate();
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
                if (!session.Link.IsOpen) return false;
                session.Link.Write(cmd);
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
                var link = new Win32SerialLink();
                link.Open(portName);

                uint handle = Interlocked.Increment(ref _nextHandle);
                string serial = $"TURZX_{spec.Pid:X4}_{serialKey}";
                var info = new DisplayDeviceInfo(handle, serial, spec.DefaultWidth, spec.DefaultHeight, 60, 0);
                var session = new SerialSession
                {
                    Handle = handle,
                    PortName = portName,
                    Link = link,
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
        ushort[] pixels = SerialTileDiffEngine.ConvertBgraToRgb565Array(bitmap);
        byte[] output = new byte[pixels.Length * 2];
        int outIdx = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            ushort rgb565 = pixels[i];
            output[outIdx++] = (byte)(rgb565 & 0xFF);
            output[outIdx++] = (byte)(rgb565 >> 8);
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
