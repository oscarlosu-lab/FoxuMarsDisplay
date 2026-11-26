using SkiaSharp;

namespace FoxuMarsDisplay.Core.Backends;

/// <summary>
/// Combines MacroSilicon (MSDISPLAYSDKWRRAPER) and AIC (AicUsbDisplay) backends under a single IDisplayBackend.
/// </summary>
public sealed class CompositeDisplayBackend : IDisplayBackend
{
    private readonly IDisplayBackend[] _backends;
    private bool _disposed;

    public event Action<DisplayDeviceInfo>? DeviceAttached;
    public event Action<uint>? DeviceDetached;

    public IReadOnlyList<DisplayDeviceInfo> Devices =>
        _backends.SelectMany(b => b.Devices).ToArray();

    public CompositeDisplayBackend(params IDisplayBackend[] backends)
    {
        _backends = backends.Length > 0
            ? backends
            : [new MsDisplayBackend(), new AicDisplayBackend(), new SerialCdcVmaxBackend(), new TurzxSerialBackend(), new TurzxWinUsbBackend()];
        foreach (var b in _backends)
        {
            b.DeviceAttached += d => DeviceAttached?.Invoke(d);
            b.DeviceDetached += h => DeviceDetached?.Invoke(h);
        }
    }

    public bool Start(int logLevel = 0)
    {
        bool anyStarted = false;
        foreach (var b in _backends)
        {
            try
            {
                if (b.Start(logLevel))
                    anyStarted = true;
            }
            catch
            {
                // Optional backend DLL may be absent; keep others running.
            }
        }
        return anyStarted;
    }

    public bool SetResolution(uint handle, int width, int height, int refresh = 60)
    {
        var owner = FindOwner(handle);
        return owner?.SetResolution(handle, width, height, refresh) ?? false;
    }

    public int SendBgraFrame(uint handle, SKBitmap bgraBitmap)
    {
        var owner = FindOwner(handle);
        return owner?.SendBgraFrame(handle, bgraBitmap) ?? -1;
    }

    public void Stop()
    {
        foreach (var b in _backends)
        {
            try { b.Stop(); } catch { }
        }
    }

    public bool TrySetHardwareBrightness(uint handle, int brightness)
    {
        var owner = FindOwner(handle);
        return owner?.TrySetHardwareBrightness(handle, brightness) ?? false;
    }

    public string? ReadFirmwareVersion(uint handle)
    {
        var owner = FindOwner(handle);
        return owner?.ReadFirmwareVersion(handle);
    }

    private IDisplayBackend? FindOwner(uint handle) =>
        _backends.FirstOrDefault(b => b.Devices.Any(d => d.Handle == handle));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var b in _backends)
        {
            try { b.Dispose(); } catch { }
        }
    }
}
