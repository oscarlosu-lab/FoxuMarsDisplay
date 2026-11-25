using System.Runtime.InteropServices;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Native;
using Xunit;

namespace FoxuMarsDisplay.Core.Tests;

public sealed class AbiAndConfigTests
{
    [Fact]
    public void MsDisplay_NativeStructs_MatchExactAbiSizes()
    {
        Assert.Equal(16, Marshal.SizeOf<MsDisplayApi.MSDisplayPicture>());
        Assert.Equal(12, Marshal.SizeOf<MsDisplayApi.MSDisplayResolution>());
        Assert.Equal(48, Marshal.SizeOf<MsDisplayApi.MSDisplayTiming>());
        Assert.Equal(904, Marshal.SizeOf<MsDisplayApi.MSDisplayEdid>());
    }

    [Fact]
    public void ConfigStore_RoundTrip_NormalizesSerialNumber()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FoxuMarsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string cfgPath = Path.Combine(tempDir, "config.json");
            var store = new FoxuDisplayConfigStore(cfgPath);

            var cfg = new FoxuDisplayConfig { TargetFps = 15, PauseOnFullscreen = false };
            var p = FoxuDisplayConfigStore.GetOrCreateProfile(cfg, "vmaxa180240*0320s252400400");
            p.Brightness = 85;
            p.Rotation = 3;
            store.Save(cfg);

            var loaded = store.LoadOrImportSoeyi();
            Assert.Equal(15, loaded.TargetFps);
            Assert.False(loaded.PauseOnFullscreen);

            var found = FoxuDisplayConfigStore.FindProfile(loaded, "VMAXA180240*0320S252400400");
            Assert.NotNull(found);
            Assert.Equal(85, found!.Brightness);
            Assert.Equal(3, found.Rotation);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
