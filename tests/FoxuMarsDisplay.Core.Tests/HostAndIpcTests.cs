using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FoxuMarsDisplay.Core.Backends;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Core.Sensors;
using SkiaSharp;
using Xunit;

namespace FoxuMarsDisplay.Core.Tests;

public sealed class HostAndIpcTests
{
    private sealed class FakeBackend : IDisplayBackend
    {
        private readonly List<DisplayDeviceInfo> _devices = new();
        private readonly int _sendReturnCode;
        public int SendCount;

        public FakeBackend(int sendReturnCode = 0) => _sendReturnCode = sendReturnCode;

        public event Action<DisplayDeviceInfo>? DeviceAttached;
        public event Action<uint>? DeviceDetached;
        public IReadOnlyList<DisplayDeviceInfo> Devices => _devices;

        public void AddFakeDevice(DisplayDeviceInfo dev)
        {
            _devices.Add(dev);
            DeviceAttached?.Invoke(dev);
        }

        public bool Start(int logLevel = 0) => true;
        public bool SetResolution(uint handle, int width, int height, int refresh = 60) => true;

        public int SendBgraFrame(uint handle, SKBitmap bgraBitmap)
        {
            Interlocked.Increment(ref SendCount);
            return _sendReturnCode;
        }

        public void Stop()
        {
            foreach (var d in _devices.ToArray())
                DeviceDetached?.Invoke(d.Handle);
            _devices.Clear();
        }

        public void Dispose() => Stop();
    }

    [Fact]
    public async Task DisplayHost_SendsKeepAlives_Every500ms_ForStaticScreen()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuMarsHostTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Setting.txt"), "name:StaticTest\nwidth:240\nheight:320\nTYPE:0\n");

            var cfg = new FoxuDisplayConfig { TargetFps = 12, PauseOnFullscreen = false };
            cfg.Screens.Add(new ScreenProfile { SerialNumber = "TEST_STATIC_01", ThemePath = tempDir, IsEnabled = true });

            var backend = new FakeBackend(sendReturnCode: 0);
            var store = new FoxuDisplayConfigStore(Path.Combine(tempDir, "cfg.json"));
            using var sensors = new LightweightSensorProvider();
            using var host = new DisplayHost(backend, sensors, store, cfg, customCacheDir: Path.Combine(tempDir, "cache"));

            host.Start(dryRun: false, enableIpc: false);
            backend.AddFakeDevice(new DisplayDeviceInfo(1, "TEST_STATIC_01", 240, 320, 60));

            await Task.Delay(1700);
            var status = Assert.Single(host.GetStatuses());
            Assert.True(status.FramesSent >= 3, $"Expected >=3 frames with 500ms keepalive, got {status.FramesSent}");
            Assert.True(status.KeepAlivesSent >= 2, $"Expected >=2 keepalives, got {status.KeepAlivesSent}");
            Assert.Equal(0, status.SendErrors);
            host.Stop();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DisplayHost_TreatsMinus5_AsBusyDrops_NotErrors()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuMarsBusyTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Setting.txt"), "name:BusyTest\nwidth:240\nheight:320\nTYPE:0\n");

            var cfg = new FoxuDisplayConfig { TargetFps = 12, PauseOnFullscreen = false };
            cfg.Screens.Add(new ScreenProfile { SerialNumber = "TEST_BUSY_01", ThemePath = tempDir, IsEnabled = true });

            var backend = new FakeBackend(sendReturnCode: -5);
            var store = new FoxuDisplayConfigStore(Path.Combine(tempDir, "cfg.json"));
            using var sensors = new LightweightSensorProvider();
            using var host = new DisplayHost(backend, sensors, store, cfg, customCacheDir: Path.Combine(tempDir, "cache"));

            host.Start(dryRun: false, enableIpc: false);
            backend.AddFakeDevice(new DisplayDeviceInfo(2, "TEST_BUSY_01", 240, 320, 60));

            await Task.Delay(350);
            var status = Assert.Single(host.GetStatuses());
            Assert.True(status.BusyDrops > 0);
            Assert.Equal(0, status.SendErrors);
            host.Stop();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task IpcPipeServer_RespondsToPing()
    {
        using var server = new IpcPipeServer(req =>
            req.Command == "ping" ? new IpcResponse(true, "pong") : new IpcResponse(false, "nope"));
        server.Start();

        using var client = new NamedPipeClientStream(".", IpcPipeServer.PipeName, PipeDirection.InOut);
        await client.ConnectAsync(2000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "ping" }));
        string? line = await reader.ReadLineAsync();
        Assert.NotNull(line);
        Assert.Contains("pong", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisplayHost_CalibrationMigration_PreservesPhysicalOrientation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FoxuCalTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string cfgPath = Path.Combine(tempDir, "config.json");
            string cacheDir = Path.Combine(tempDir, "cache");
            var store = new FoxuDisplayConfigStore(cfgPath);
            var cfg = new FoxuDisplayConfig
            {
                Screens =
                [
                    new ScreenProfile
                    {
                        SerialNumber = "VMAXC230360*0960S00000001",
                        Rotation = 2,
                        CalibrationVersion = 0
                    }
                ]
            };
            store.Save(cfg);

            using var backend = new FakeBackend();
            backend.AddFakeDevice(new DisplayDeviceInfo(10, "VMAXC230360*0960S00000001", 360, 960, 60, HardwareRotationOffset: 2));
            backend.AddFakeDevice(new DisplayDeviceInfo(11, "VMAXC230360*0960S00000002", 360, 960, 60, HardwareRotationOffset: 2));

            using var host = new DisplayHost(backend: backend, configStore: store, customCacheDir: cacheDir);
            host.Start(dryRun: false, enableIpc: false);

            var statuses = host.GetStatuses();
            var legacy = statuses.First(s => s.SerialNumber.EndsWith("00001", StringComparison.Ordinal));
            var fresh = statuses.First(s => s.SerialNumber.EndsWith("00002", StringComparison.Ordinal));

            Assert.Equal(0, legacy.Rotation);
            Assert.Equal(2, legacy.HardwareRotationOffset);
            Assert.Equal(0, fresh.Rotation);
            Assert.Equal(2, fresh.HardwareRotationOffset);

            var reloaded = store.LoadOrImportSoeyi();
            var legacyProfile = FoxuDisplayConfigStore.FindProfile(reloaded, "VMAXC230360*0960S00000001");
            Assert.NotNull(legacyProfile);
            Assert.Equal(1, legacyProfile!.CalibrationVersion);
            Assert.Equal(0, legacyProfile.Rotation);

            host.Stop();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
