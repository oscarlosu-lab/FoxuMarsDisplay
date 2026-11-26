using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Backends;

/// <summary>
/// Clean-room WinUSB SLV3 backend for VID_1CBE USB secondary displays.
/// Uses standard Windows SetupAPI + WinUSB P/Invoke and DES-CBC framed 512-byte headers
/// followed by constrained JPEG payloads.
/// </summary>
public sealed class TurzxWinUsbBackend : IDisplayBackend
{
    public const uint HandleBase = 0x50000000u;
    private static readonly byte[] Slv3Key = Encoding.ASCII.GetBytes("slv3tuzx");
    private static readonly Guid WinUsbDeviceInterfaceGuid = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private sealed class WinUsbTransport : IDisposable
    {
        private SafeFileHandle? _file;
        private nint _winUsbHandle;
        private byte _pipeIn;
        private byte _pipeOut;
        private int _maxInPacket = 512;

        public void Open(string devicePath)
        {
            Close();
            var fh = CreateFile(devicePath, 0xC0000000u, 3u, IntPtr.Zero, 3u, 0x40000000u, IntPtr.Zero);
            if (fh.IsInvalid)
                throw new IOException($"WinUSB CreateFile failed ({Marshal.GetLastWin32Error()})");

            try
            {
                if (!WinUsb_Initialize(fh, out _winUsbHandle))
                    throw new IOException($"WinUsb_Initialize failed ({Marshal.GetLastWin32Error()})");

                if (!WinUsb_QueryInterfaceSettings(_winUsbHandle, 0, out var iface))
                    throw new IOException($"WinUsb_QueryInterfaceSettings failed ({Marshal.GetLastWin32Error()})");

                for (byte i = 0; i < iface.Endpoints; i++)
                {
                    if (WinUsb_QueryPipe(_winUsbHandle, 0, i, out var pipe) && pipe.Type == 2)
                    {
                        if ((pipe.Id & 0x80) != 0)
                        {
                            _pipeIn = pipe.Id;
                            _maxInPacket = Math.Max(64, (int)pipe.MaxPacket);
                        }
                        else
                        {
                            _pipeOut = pipe.Id;
                        }
                    }
                }

                if (_pipeIn == 0 || _pipeOut == 0)
                    throw new IOException("Bulk IN/OUT endpoints not found.");

                uint timeoutMs = 2000;
                WinUsb_SetPipePolicy(_winUsbHandle, _pipeOut, 3u, 4u, ref timeoutMs);
                WinUsb_SetPipePolicy(_winUsbHandle, _pipeIn, 3u, 4u, ref timeoutMs);
                WinUsb_AbortPipe(_winUsbHandle, _pipeOut);
                WinUsb_ResetPipe(_winUsbHandle, _pipeOut);
                WinUsb_ResetPipe(_winUsbHandle, _pipeIn);
                _file = fh;
            }
            catch
            {
                Close();
                fh.Dispose();
                throw;
            }
        }

        public void Write(byte[] data)
        {
            if (_winUsbHandle == IntPtr.Zero)
                throw new IOException("WinUSB transport closed.");
            if (!WinUsb_WritePipe(_winUsbHandle, _pipeOut, data, (uint)data.Length, out uint written, IntPtr.Zero) ||
                written != (uint)data.Length)
            {
                throw new IOException($"WinUsb_WritePipe failed ({Marshal.GetLastWin32Error()})");
            }
        }

        public byte[] Read(int timeoutMs)
        {
            if (_winUsbHandle == IntPtr.Zero)
                return [];

            uint ms = (uint)Math.Max(10, timeoutMs);
            WinUsb_SetPipePolicy(_winUsbHandle, _pipeIn, 3u, 4u, ref ms);
            byte[] buf = new byte[_maxInPacket];
            if (!WinUsb_ReadPipe(_winUsbHandle, _pipeIn, buf, (uint)buf.Length, out uint read, IntPtr.Zero))
                return [];
            if (read == 0) return [];
            Array.Resize(ref buf, (int)read);
            return buf;
        }

        public void Close()
        {
            if (_winUsbHandle != IntPtr.Zero)
            {
                WinUsb_Free(_winUsbHandle);
                _winUsbHandle = IntPtr.Zero;
            }
            _file?.Dispose();
            _file = null;
        }

        public void Dispose() => Close();

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct InterfaceDescriptor
        {
            public byte Length, Type, Number, Alternate, Endpoints, Class, SubClass, Protocol, Str;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PipeInfo
        {
            public int Type;
            public byte Id;
            public ushort MaxPacket;
            public byte Interval;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Initialize(SafeFileHandle fileHandle, out nint interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Free(nint interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_QueryInterfaceSettings(nint interfaceHandle, byte altIdx, out InterfaceDescriptor desc);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_QueryPipe(nint interfaceHandle, byte altIdx, byte pipeIdx, out PipeInfo pipeInfo);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_SetPipePolicy(nint interfaceHandle, byte pipeId, uint policyType, uint valueLen, ref uint value);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_WritePipe(nint interfaceHandle, byte pipeId, byte[] buffer, uint len, out uint written, IntPtr ov);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ReadPipe(nint interfaceHandle, byte pipeId, byte[] buffer, uint len, out uint read, IntPtr ov);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_AbortPipe(nint interfaceHandle, byte pipeId);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ResetPipe(nint interfaceHandle, byte pipeId);
    }

    private sealed class WinUsbSession : IDisposable
    {
        public uint Handle;
        public string DevicePath = string.Empty;
        public WinUsbTransport Transport = new();
        public DisplayDeviceInfo Info = null!;
        public string? FirmwareVersion;
        public uint SequenceId;
        public bool Initialized;
        public readonly object SyncRoot = new();

        public void Dispose() => Transport.Dispose();
    }

    private readonly ConcurrentDictionary<uint, WinUsbSession> _sessions = new();
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
        if (_disposed) throw new ObjectDisposedException(nameof(TurzxWinUsbBackend));
        if (_started) return true;
        _started = true;

        ScanDevicesOnce();
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

        byte[] jpeg = EncodeJpegUnderLimit(bgraBitmap);
        if (jpeg.Length == 0)
            return -2;

        lock (session.SyncRoot)
        {
            try
            {
                if (!session.Initialized)
                    InitializeSession(session);

                uint seq = ++session.SequenceId;
                ReadOnlySpan<byte> lenBe =
                [
                    (byte)(jpeg.Length >> 24),
                    (byte)(jpeg.Length >> 16),
                    (byte)(jpeg.Length >> 8),
                    (byte)jpeg.Length
                ];
                byte[] header = BuildSlv3Header(101, lenBe, seq);
                byte[] packet = new byte[512 + jpeg.Length];
                Buffer.BlockCopy(header, 0, packet, 0, 512);
                Buffer.BlockCopy(jpeg, 0, packet, 512, jpeg.Length);
                session.Transport.Write(packet);
                session.Transport.Read(400);
                return 0;
            }
            catch
            {
                session.Initialized = false;
                return -3;
            }
        }
    }

    public bool TrySetHardwareBrightness(uint handle, int brightness)
    {
        if (!_started || !_sessions.TryGetValue(handle, out var session))
            return false;

        byte level = (byte)Math.Clamp(brightness, 0, 100);
        lock (session.SyncRoot)
        {
            try
            {
                if (!session.Initialized)
                    InitializeSession(session);

                byte[] cmd = BuildSlv3Header(14, [level], ++session.SequenceId);
                session.Transport.Write(cmd);
                session.Transport.Read(300);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public string? ReadFirmwareVersion(uint handle) =>
        _sessions.TryGetValue(handle, out var session) ? session.FirmwareVersion : null;

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

    private static void InitializeSession(WinUsbSession session)
    {
        session.Transport.Write(BuildSlv3Header(123, [], ++session.SequenceId));
        session.Transport.Read(300);

        session.Transport.Write(BuildSlv3Header(10, [], ++session.SequenceId));
        byte[] fwReply = session.Transport.Read(500);
        session.FirmwareVersion = ParseFirmwareAscii(fwReply);

        session.Transport.Write(BuildSlv3Header(15, [120], ++session.SequenceId));
        session.Transport.Read(300);

        session.Transport.Write(BuildSlv3Header(14, [100], ++session.SequenceId));
        session.Transport.Read(300);

        session.Initialized = true;
    }

    private static string? ParseFirmwareAscii(byte[] reply)
    {
        if (reply.Length <= 8) return null;
        int end = Array.IndexOf(reply, (byte)0, 8);
        if (end < 0) end = Math.Min(reply.Length, 40);
        string text = Encoding.ASCII.GetString(reply, 8, Math.Max(0, end - 8)).Trim();
        return text.Length > 0 && text.All(c => c >= ' ' && c < '\u007f') ? text : null;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(5000, ct).ConfigureAwait(false); }
            catch { break; }
            if (!ct.IsCancellationRequested)
                ScanDevicesOnce();
        }
    }

    private void ScanDevicesOnce()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var discovered = EnumerateWinUsbPaths();
        var activeSet = new HashSet<string>(discovered.Select(d => d.DevicePath), StringComparer.OrdinalIgnoreCase);

        foreach (var kv in _sessions.ToArray())
        {
            if (!activeSet.Contains(kv.Value.DevicePath))
            {
                if (_sessions.TryRemove(kv.Key, out var removed))
                {
                    removed.Dispose();
                    DeviceDetached?.Invoke(kv.Key);
                }
            }
        }

        foreach (var (devicePath, spec, serialKey) in discovered)
        {
            if (_sessions.Values.Any(s => s.DevicePath.Equals(devicePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                var transport = new WinUsbTransport();
                transport.Open(devicePath);
                uint handle = Interlocked.Increment(ref _nextHandle);
                string serial = $"WINUSB_{spec.Pid:X4}_{serialKey}";
                var info = new DisplayDeviceInfo(handle, serial, spec.DefaultWidth, spec.DefaultHeight, 60, 0);
                var session = new WinUsbSession
                {
                    Handle = handle,
                    DevicePath = devicePath,
                    Transport = transport,
                    Info = info
                };
                if (_sessions.TryAdd(handle, session))
                    DeviceAttached?.Invoke(info);
                else
                    session.Dispose();
            }
            catch
            {
                // Device may be busy or non-WinUSB driver bound.
            }
        }
    }

    private static List<(string DevicePath, KnownUsbDisplaySpec Spec, string SerialKey)> EnumerateWinUsbPaths()
    {
        var results = new List<(string, KnownUsbDisplaySpec, string)>();
        Guid guid = WinUsbDeviceInterfaceGuid;
        nint devInfo = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (devInfo == new nint(-1))
            return results;

        try
        {
            var iface = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint idx = 0; SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref guid, idx, ref iface); idx++)
            {
                SetupDiGetDeviceInterfaceDetail(devInfo, ref iface, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                if (required < 8) continue;

                nint detailBuf = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref iface, detailBuf, required, out _, IntPtr.Zero))
                        continue;

                    string? path = Marshal.PtrToStringUni(detailBuf + 4);
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    if (TryParseVidPidFromPath(path, out ushort vid, out ushort pid, out string serialKey))
                    {
                        var spec = UsbDisplayCatalog.FindByVidPid(vid, pid);
                        if (spec is not null && spec.Family == DisplayProtocolFamily.TurzxWinUsb)
                            results.Add((path, spec, serialKey));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuf);
                }
            }
        }
        catch
        {
            // Enumeration best-effort.
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfo);
        }

        return results;
    }

    private static bool TryParseVidPidFromPath(string path, out ushort vid, out ushort pid, out string serialKey)
    {
        vid = 0;
        pid = 0;
        serialKey = "0";
        string upper = path.ToUpperInvariant();
        int vIdx = upper.IndexOf("VID_", StringComparison.Ordinal);
        int pIdx = upper.IndexOf("PID_", StringComparison.Ordinal);
        if (vIdx < 0 || pIdx < 0 || vIdx + 8 > upper.Length || pIdx + 8 > upper.Length)
            return false;

        if (!ushort.TryParse(upper.AsSpan(vIdx + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vid))
            return false;
        if (!ushort.TryParse(upper.AsSpan(pIdx + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out pid))
            return false;

        string[] parts = path.Split('#');
        serialKey = parts.Length >= 3 ? parts[2] : "0";
        return true;
    }

    /// <summary>
    /// Builds a 512-byte DES-CBC SLV3 command packet with optional legacy 504-byte plaintext framing.
    /// </summary>
    public static byte[] BuildSlv3Header(byte command, ReadOnlySpan<byte> payloadArgs, uint sequenceId, bool legacy504 = false)
    {
        int plainLen = legacy504 ? 504 : 500;
        byte[] plain = new byte[plainLen];
        plain[0] = command;
        plain[2] = 0x1A;
        plain[3] = 0x6D;
        BitConverter.TryWriteBytes(plain.AsSpan(4, 4), sequenceId);
        if (!payloadArgs.IsEmpty)
        {
            int copy = Math.Min(payloadArgs.Length, plainLen - 8);
            payloadArgs[..copy].CopyTo(plain.AsSpan(8, copy));
        }

#pragma warning disable SYSLIB0021
        using var des = DES.Create();
#pragma warning restore SYSLIB0021
        des.Key = Slv3Key;
        des.IV = Slv3Key;
        des.Mode = CipherMode.CBC;
        des.Padding = PaddingMode.PKCS7;
        byte[] encrypted = des.EncryptCbc(plain, Slv3Key);

        byte[] packet = new byte[512];
        Buffer.BlockCopy(encrypted, 0, packet, 0, Math.Min(encrypted.Length, 512));
        if (!legacy504)
        {
            packet[510] = 0xA1;
            packet[511] = 0x1A;
        }
        return packet;
    }

    public static byte[] EncodeJpegUnderLimit(SKBitmap bitmap, int maxBytes = 500_000)
    {
        using var image = SKImage.FromBitmap(bitmap);
        foreach (int quality in (ReadOnlySpan<int>)[88, 76, 64, 52, 40])
        {
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (data is null) continue;
            byte[] bytes = data.ToArray();
            if (bytes.Length <= maxBytes || quality == 40)
                return bytes;
        }
        return [];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint devInfo, nint devInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint devInfo, ref SpDeviceInterfaceData deviceInterfaceData, nint detailData, uint detailDataSize, out uint requiredSize, nint devInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint devInfo);
}
