using SkiaSharp;

namespace FoxuMarsDisplay.Core.Backends;

public sealed record DisplayDeviceInfo(
    uint Handle,
    string SerialNumber,
    int NativeWidth,
    int NativeHeight,
    int RefreshHz,
    int HardwareRotationOffset = 0);

public interface IDisplayBackend : IDisposable
{
    event Action<DisplayDeviceInfo>? DeviceAttached;
    event Action<uint>? DeviceDetached;

    IReadOnlyList<DisplayDeviceInfo> Devices { get; }

    bool Start(int logLevel = 0);
    bool SetResolution(uint handle, int width, int height, int refresh = 60);
    int SendBgraFrame(uint handle, SKBitmap bgraBitmap);
    void Stop();
}
