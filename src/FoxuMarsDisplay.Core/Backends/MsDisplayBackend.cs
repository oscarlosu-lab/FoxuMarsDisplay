using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using FoxuMarsDisplay.Core.Native;

namespace FoxuMarsDisplay.Core.Backends;

public sealed class MsDisplayBackend : IDisplayBackend
{
    private readonly ConcurrentDictionary<uint, DisplayDeviceInfo> _devices = new();
    private MsDisplayApi.AttachProcHandler? _attachDelegate;
    private MsDisplayApi.DetachProcHandler? _detachDelegate;
    private volatile bool _started;
    private volatile bool _disposed;

    public event Action<DisplayDeviceInfo>? DeviceAttached;
    public event Action<uint>? DeviceDetached;

    public IReadOnlyList<DisplayDeviceInfo> Devices => _devices.Values.ToArray();

    public bool Start(int logLevel = 0)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MsDisplayBackend));
        if (_started) return true;

        _attachDelegate = OnAttach;
        _detachDelegate = OnDetach;

        var attachPtr = Marshal.GetFunctionPointerForDelegate(_attachDelegate);
        var detachPtr = Marshal.GetFunctionPointerForDelegate(_detachDelegate);
        MsDisplayApi.Wrraper_MSDisplayRegisterCallback(attachPtr, detachPtr);

        var edid = MsDisplayApi.CreateDefaultEdid();
        int edidSize = Marshal.SizeOf<MsDisplayApi.MSDisplayEdid>();
        IntPtr edidPtr = Marshal.AllocHGlobal(edidSize);
        try
        {
            Marshal.StructureToPtr(edid, edidPtr, false);
            int rc = MsDisplayApi.Wrraper_MSDisplayStart(logLevel, edidPtr);
            if (rc != 0) return false;
            _started = true;
            return true;
        }
        finally
        {
            Marshal.DestroyStructure<MsDisplayApi.MSDisplayEdid>(edidPtr);
            Marshal.FreeHGlobal(edidPtr);
        }
    }

    public bool SetResolution(uint handle, int width, int height, int refresh = 60)
    {
        if (!_started) return false;
        unsafe
        {
            var res = new MsDisplayApi.MSDisplayResolution { Width = width, Height = height, Refresh = refresh };
            return MsDisplayApi.MSDisplaySetVideoParam(handle, (IntPtr)(&res)) == 0;
        }
    }

    public int SendBgraFrame(uint handle, SKBitmap bgraBitmap)
    {
        if (!_started) return -2;
        if (bgraBitmap.ColorType != SKColorType.Bgra8888)
            throw new ArgumentException("Bitmap must be Bgra8888", nameof(bgraBitmap));

        IntPtr pixels = bgraBitmap.GetPixels();
        if (pixels == IntPtr.Zero) return -1;

        unsafe
        {
            var pic = new MsDisplayApi.MSDisplayPicture
            {
                Width = bgraBitmap.Width,
                Height = bgraBitmap.Height,
                Data = pixels
            };
            return MsDisplayApi.MSDisplaySendPicture(handle, (IntPtr)(&pic), false);
        }
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _devices.Clear();
        try { MsDisplayApi.Wrraper_MSDisplayStop(); } catch { }
    }

    private void OnAttach(uint handle, IntPtr resPtr, int count)
    {
        try
        {
            var resolutions = ReadResolutions(resPtr, count);
            string sn = ReadSerialNumber(handle);
            var (w, h, hz) = ResolvePanelSize(sn, resolutions);

            if (w > 0 && h > 0)
                SetResolution(handle, w, h, hz > 0 ? hz : 60);

            int hwOffset = ResolveHardwareRotationOffset(sn, w, h);
            var info = new DisplayDeviceInfo(handle, sn, w, h, hz > 0 ? hz : 60, hwOffset);
            _devices[handle] = info;
            DeviceAttached?.Invoke(info);
        }
        catch
        {
            // Never let unhandled exceptions escape native callback.
        }
    }

    private void OnDetach(uint handle)
    {
        _devices.TryRemove(handle, out _);
        DeviceDetached?.Invoke(handle);
    }

    private static List<MsDisplayApi.MSDisplayResolution> ReadResolutions(IntPtr resPtr, int count)
    {
        var list = new List<MsDisplayApi.MSDisplayResolution>();
        if (resPtr == IntPtr.Zero || count <= 0 || count > 32) return list;

        int stride = Marshal.SizeOf<MsDisplayApi.MSDisplayResolution>();
        for (int i = 0; i < count; i++)
        {
            var r = Marshal.PtrToStructure<MsDisplayApi.MSDisplayResolution>(resPtr + i * stride);
            if (r.IsValid) list.Add(r);
        }
        return list;
    }

    private static string ReadSerialNumber(uint handle)
    {
        unsafe
        {
            byte* buf = stackalloc byte[64];
            for (int i = 0; i < 64; i++) buf[i] = 0;

            int len = MsDisplayApi.MSDisplayReadSN(handle, (IntPtr)buf);
            if (len <= 0 || len > 63) return $"MS_{handle:X}";

            int actual = 0;
            while (actual < len && buf[actual] != 0) actual++;
            return actual > 0 ? Encoding.ASCII.GetString(buf, actual).Trim() : $"MS_{handle:X}";
        }
    }

    public static int ResolveHardwareRotationOffset(string serialNumber, int width, int height)
    {
        if (!string.IsNullOrWhiteSpace(serialNumber) &&
            serialNumber.StartsWith("VMAXC", StringComparison.OrdinalIgnoreCase) &&
            height > width)
        {
            return 2; // 180° inverted glass scan on VMAXC vertical chassis panels
        }
        return 0;
    }

    private static (int Width, int Height, int Refresh) ResolvePanelSize(
        string sn,
        List<MsDisplayApi.MSDisplayResolution> resolutions)
    {
        if (sn.Contains("360*0960", StringComparison.OrdinalIgnoreCase) || sn.Contains("360_0960", StringComparison.OrdinalIgnoreCase))
            return (360, 960, 60);
        if (sn.Contains("240*0320", StringComparison.OrdinalIgnoreCase) || sn.Contains("240_0320", StringComparison.OrdinalIgnoreCase))
            return (240, 320, 60);
        if (sn.Contains("320*0240", StringComparison.OrdinalIgnoreCase) || sn.Contains("320_0240", StringComparison.OrdinalIgnoreCase))
            return (320, 240, 60);
        if (sn.Contains("480*0480", StringComparison.OrdinalIgnoreCase) || sn.Contains("480_0480", StringComparison.OrdinalIgnoreCase))
            return (480, 480, 60);
        if (sn.Contains("462*1920", StringComparison.OrdinalIgnoreCase) || sn.Contains("462_1920", StringComparison.OrdinalIgnoreCase))
            return (462, 1920, 60);

        if (resolutions.Count > 0)
        {
            var first = resolutions[0];
            return (first.Width, first.Height, first.Refresh);
        }

        return (360, 960, 60);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
