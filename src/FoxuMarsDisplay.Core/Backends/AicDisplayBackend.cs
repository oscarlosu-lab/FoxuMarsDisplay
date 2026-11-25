using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SkiaSharp;
using FoxuMarsDisplay.Core.Native;

namespace FoxuMarsDisplay.Core.Backends;

public sealed class AicDisplayBackend : IDisplayBackend
{
    private const uint HandleOffset = 0x10000000;
    private readonly ConcurrentDictionary<uint, DisplayDeviceInfo> _devices = new();
    private AicDisplayApi.AttachProcHandler? _attachDelegate;
    private AicDisplayApi.DetachProcHandler? _detachDelegate;
    private volatile bool _started;
    private volatile bool _disposed;

    public event Action<DisplayDeviceInfo>? DeviceAttached;
    public event Action<uint>? DeviceDetached;

    public IReadOnlyList<DisplayDeviceInfo> Devices => _devices.Values.ToArray();

    public bool Start(int logLevel = 0)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AicDisplayBackend));
        if (_started) return true;

        try
        {
            _attachDelegate = OnAttach;
            _detachDelegate = OnDetach;
            AicDisplayApi.AICDispRegisterCallback(
                Marshal.GetFunctionPointerForDelegate(_attachDelegate),
                Marshal.GetFunctionPointerForDelegate(_detachDelegate));

            int rc = AicDisplayApi.AICDispStart();
            if (rc != 0) return false;
            _started = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetResolution(uint handle, int width, int height, int refresh = 60) => true;

    public int SendBgraFrame(uint handle, SKBitmap bgraBitmap)
    {
        if (!_started) return -2;
        if (bgraBitmap.ColorType != SKColorType.Bgra8888)
            throw new ArgumentException("Bitmap must be Bgra8888", nameof(bgraBitmap));

        IntPtr pixels = bgraBitmap.GetPixels();
        if (pixels == IntPtr.Zero) return -1;

        uint rawHandle = handle >= HandleOffset ? handle - HandleOffset : handle;
        unsafe
        {
            var pic = new AicDisplayApi.AICDispPicture
            {
                Width = bgraBitmap.Width,
                Height = bgraBitmap.Height,
                Data = pixels
            };
            return AicDisplayApi.AICDispSendPicture(rawHandle, (IntPtr)(&pic));
        }
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _devices.Clear();
        try { AicDisplayApi.AICDispStop(); } catch { }
    }

    private void OnAttach(uint handle, IntPtr resPtr, int count)
    {
        try
        {
            int w = 480, h = 480, hz = 60;
            if (resPtr != IntPtr.Zero && count > 0 && count < 32)
            {
                var r = Marshal.PtrToStructure<AicDisplayApi.AICDispResolution>(resPtr);
                if (r.Width > 0 && r.Height > 0)
                {
                    w = r.Width;
                    h = r.Height;
                    hz = r.Refresh > 0 ? r.Refresh : 60;
                }
            }

            uint compositeHandle = handle + HandleOffset;
            var info = new DisplayDeviceInfo(compositeHandle, $"AIC_{handle:X}", w, h, hz);
            _devices[compositeHandle] = info;
            DeviceAttached?.Invoke(info);
        }
        catch { }
    }

    private void OnDetach(uint handle, IntPtr resPtr)
    {
        uint compositeHandle = handle + HandleOffset;
        _devices.TryRemove(compositeHandle, out _);
        DeviceDetached?.Invoke(compositeHandle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
