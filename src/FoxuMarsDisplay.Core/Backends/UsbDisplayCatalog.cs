using Microsoft.Win32;

namespace FoxuMarsDisplay.Core.Backends;

public enum DisplayProtocolFamily
{
    MacroSiliconMs9132,
    HlVmaxCdcDls,
    HlVmaxBulk,
    ArtInChipAic,
    TurzxWinUsb,
    TuringSerial,
    ThermalrightLy,
    ThermalrightHid,
    ChiZhuSomore,
    MiraBox,
    WeActSerial,
    ThermaltakeHid,
    NzxtKraken,
    CorsairHid,
    GigabyteAorus
}

public sealed record KnownUsbDisplaySpec(
    ushort Vid,
    ushort Pid,
    string ModelName,
    DisplayProtocolFamily Family,
    int DefaultWidth,
    int DefaultHeight);

/// <summary>
/// Registry of known USB secondary display VID:PIDs across all 6 hardware families.
/// </summary>
public static class UsbDisplayCatalog
{
    public static readonly IReadOnlyList<KnownUsbDisplaySpec> KnownDisplays =
    [
        // MacroSilicon MS9132 / MS912C
        new(0x345F, 0x9132, "MacroSilicon MS9132", DisplayProtocolFamily.MacroSiliconMs9132, 360, 960),
        new(0x345F, 0x9133, "MacroSilicon MS9133", DisplayProtocolFamily.MacroSiliconMs9132, 1024, 600),
        new(0x345F, 0x9135, "MacroSilicon MS9135", DisplayProtocolFamily.MacroSiliconMs9132, 1024, 600),
        new(0x534D, 0x6021, "MacroSilicon MS912C", DisplayProtocolFamily.MacroSiliconMs9132, 800, 600),

        // HL-VMAX / SOEYI CDC DLS / RT-Thread Serial
        new(0x33C3, 0xF101, "HL-VMAX / SOEYI CH1L", DisplayProtocolFamily.HlVmaxCdcDls, 320, 1480),
        new(0x33C3, 0x7788, "SOEYI VMAXC170 / RT-Thread", DisplayProtocolFamily.HlVmaxCdcDls, 320, 1480),
        new(0x33C3, 0x7791, "Delta CW360 / HONGTAI", DisplayProtocolFamily.HlVmaxCdcDls, 480, 320),
        new(0x33C3, 0x7792, "Delta / RT-Thread Display", DisplayProtocolFamily.HlVmaxCdcDls, 480, 320),

        // ArtInChip AicUsbDisplay
        new(0x33C3, 0x0E02, "ArtInChip AicUsbDisplay", DisplayProtocolFamily.ArtInChipAic, 480, 480),

        // TURZX Win207 / SLV3 WinUSB
        new(0x1CBE, 0x0021, "TURZX 2.1\" Round", DisplayProtocolFamily.TurzxWinUsb, 480, 480),
        new(0x1CBE, 0xB021, "TURZX 2.1\" Round SLC", DisplayProtocolFamily.TurzxWinUsb, 480, 480),
        new(0x1CBE, 0x0028, "TURZX 2.8\" Round", DisplayProtocolFamily.TurzxWinUsb, 480, 480),
        new(0x1CBE, 0x0040, "TURZX 4.0\" Square", DisplayProtocolFamily.TurzxWinUsb, 720, 720),
        new(0x1CBE, 0x0045, "TURZX 4.5\"", DisplayProtocolFamily.TurzxWinUsb, 1024, 768),
        new(0x1CBE, 0x0046, "TURZX 4.6\" Strip", DisplayProtocolFamily.TurzxWinUsb, 320, 960),
        new(0x1CBE, 0x0050, "TURZX 5.2\"", DisplayProtocolFamily.TurzxWinUsb, 720, 1280),
        new(0x1CBE, 0x0055, "TURZX 5.0\"", DisplayProtocolFamily.TurzxWinUsb, 720, 1280),
        new(0x1CBE, 0xA018, "USB Square 400x400 SLV3", DisplayProtocolFamily.TurzxWinUsb, 400, 400),
        new(0x1CBE, 0xA019, "USB Square 400x400 SLV3 Alt", DisplayProtocolFamily.TurzxWinUsb, 400, 400),
        new(0x1CBE, 0xA062, "TURZX 6.2\" V1", DisplayProtocolFamily.TurzxWinUsb, 368, 960),
        new(0x1CBE, 0xB062, "TURZX 6.2\" V2", DisplayProtocolFamily.TurzxWinUsb, 448, 1280),
        new(0x1CBE, 0xC062, "TURZX 6.2\" V3", DisplayProtocolFamily.TurzxWinUsb, 480, 1280),
        new(0x1CBE, 0xA065, "TURZX 6.5\"", DisplayProtocolFamily.TurzxWinUsb, 720, 1472),
        new(0x1CBE, 0xB065, "TURZX 6.5\" 1568", DisplayProtocolFamily.TurzxWinUsb, 720, 1568),
        new(0x1CBE, 0x0068, "TURZX 6.8\"", DisplayProtocolFamily.TurzxWinUsb, 1080, 2320),
        new(0x1CBE, 0xA068, "TURZX 6.8\" 2288", DisplayProtocolFamily.TurzxWinUsb, 1080, 2288),
        new(0x1CBE, 0x0070, "TURZX 7.0\"", DisplayProtocolFamily.TurzxWinUsb, 800, 1280),
        new(0x1CBE, 0x0080, "TURZX 8.0\"", DisplayProtocolFamily.TurzxWinUsb, 800, 1280),
        new(0x1CBE, 0x0088, "TURZX 8.8\" Bar", DisplayProtocolFamily.TurzxWinUsb, 480, 1920),
        new(0x1CBE, 0xA088, "USB Bar 8.8\" SLV3", DisplayProtocolFamily.TurzxWinUsb, 480, 1920),
        new(0x1CBE, 0x0092, "TURZX 9.2\" Bar", DisplayProtocolFamily.TurzxWinUsb, 462, 1920),
        new(0x1CBE, 0xA092, "TURZX 9.2\" Bar Alt", DisplayProtocolFamily.TurzxWinUsb, 464, 1920),
        new(0x1CBE, 0x0123, "TURZX 3.5\" Win207", DisplayProtocolFamily.TurzxWinUsb, 480, 320),

        // WeAct / Thermaltake / NZXT / Corsair / Gigabyte Registry
        new(0x1A86, 0xFE0C, "WeAct Display FS", DisplayProtocolFamily.WeActSerial, 320, 480),
        new(0x264A, 0x2347, "Thermaltake 6\" LCD", DisplayProtocolFamily.ThermaltakeHid, 1480, 720),
        new(0x26CE, 0x0A10, "ASRock Phantom 360 LCD", DisplayProtocolFamily.ThermaltakeHid, 480, 480),
        new(0x1E71, 0x3008, "NZXT Kraken LCD", DisplayProtocolFamily.NzxtKraken, 320, 320),
        new(0x1E71, 0x300C, "NZXT Kraken Elite LCD", DisplayProtocolFamily.NzxtKraken, 640, 640),
        new(0x1B1C, 0x0C39, "Corsair Elite LCD", DisplayProtocolFamily.CorsairHid, 480, 480),
        new(0x048D, 0x5702, "Gigabyte AORUS Edge LCD", DisplayProtocolFamily.GigabyteAorus, 320, 172),

        // Turing / TURZX Serial
        new(0x1A86, 0x5722, "Turing Smart Screen 3.5\" Rev A/B", DisplayProtocolFamily.TuringSerial, 320, 480),
        new(0x1A86, 0xCA21, "Turing Smart Screen 2.1\" Rev C", DisplayProtocolFamily.TuringSerial, 480, 480),
        new(0x1D6B, 0x0106, "TURZX 5\" USB Serial", DisplayProtocolFamily.TuringSerial, 800, 480),

        // Thermalright / ChiZhu / LY / MiraBox
        new(0x0416, 0x5408, "Thermalright Trofeo Vision LY", DisplayProtocolFamily.ThermalrightLy, 1920, 462),
        new(0x0416, 0x5409, "Thermalright / LOVINGCOOL LY", DisplayProtocolFamily.ThermalrightLy, 480, 320),
        new(0x0416, 0x5302, "Thermalright HID Type-2", DisplayProtocolFamily.ThermalrightHid, 1280, 480),
        new(0x0416, 0x8001, "Thermalright HID 480x480", DisplayProtocolFamily.ThermalrightHid, 480, 480),
        new(0x87AD, 0x70DB, "ChiZhu / Somore USBDISPLAY", DisplayProtocolFamily.ChiZhuSomore, 320, 320),
        new(0x87CD, 0x70DB, "ChiZhu / Somore Variant", DisplayProtocolFamily.ChiZhuSomore, 320, 320),
        new(0x5548, 0x1011, "MiraBox D92 9.2\"", DisplayProtocolFamily.MiraBox, 1920, 462),
    ];

    public static KnownUsbDisplaySpec? FindByVidPid(ushort vid, ushort pid) =>
        KnownDisplays.FirstOrDefault(d => d.Vid == vid && d.Pid == pid);

    public static IReadOnlyList<(string PortName, KnownUsbDisplaySpec Spec, string SerialKey)> FindComPortsForVidPids(
        params (ushort Vid, ushort Pid)[] targets)
    {
        var results = new List<(string, KnownUsbDisplaySpec, string)>();
        if (!OperatingSystem.IsWindows())
            return results;

        try
        {
            using var usbRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbRoot is null) return results;

            foreach (var subName in usbRoot.GetSubKeyNames())
            {
                KnownUsbDisplaySpec? matchedSpec = null;
                foreach (var (vid, pid) in targets)
                {
                    string prefix = $"VID_{vid:X4}&PID_{pid:X4}";
                    if (subName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedSpec = FindByVidPid(vid, pid);
                        break;
                    }
                }
                if (matchedSpec is null) continue;

                using var vidPidKey = usbRoot.OpenSubKey(subName);
                if (vidPidKey is null) continue;

                foreach (var instName in vidPidKey.GetSubKeyNames())
                {
                    using var devParams = vidPidKey.OpenSubKey($@"{instName}\Device Parameters");
                    if (devParams?.GetValue("PortName") is string portName &&
                        portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add((portName.Trim(), matchedSpec, instName));
                    }
                }
            }
        }
        catch
        {
            // Registry enumeration best-effort.
        }

        return results;
    }
}
