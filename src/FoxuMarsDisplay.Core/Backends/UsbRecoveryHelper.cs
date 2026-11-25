using System.Diagnostics;

namespace FoxuMarsDisplay.Core.Backends;

public sealed record UsbDisplayPnpStatus(
    string Name,
    string InstanceId,
    string Status,
    bool IsHealthy);

/// <summary>
/// Clean-room USB display device enumeration and PnP recovery helper.
/// Uses pnputil.exe without shell execution and only targets known secondary display VIDs.
/// </summary>
public static class UsbRecoveryHelper
{
    private static readonly string[] KnownDisplayVidTokens =
    [
        "VID_33C3", // HL-VMAX / SOEYI / ArtInChip / Jungle Leopard / LovingCool
        "VID_345F", // MacroSilicon MS9132
        "VID_534D", // MacroSilicon MS912C
        "VID_1CBE", // TURZX Win207
        "VID_1A86", // Turing Smart Screen Serial
        "VID_1D6B", // TURZX 5" USB Serial
        "VID_0416", // Thermalright LY / HID
        "VID_87AD", // ChiZhu / Somore
        "VID_5548"  // MiraBox D92
    ];

    public static IReadOnlyList<UsbDisplayPnpStatus> EnumerateDisplayDevices(int timeoutMs = 4000)
    {
        var list = new List<UsbDisplayPnpStatus>();
        try
        {
            var psi = new ProcessStartInfo("pnputil.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/enum-devices");
            psi.ArgumentList.Add("/connected");

            using var proc = Process.Start(psi);
            if (proc is null) return list;

            string output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return list;
            }

            string? curId = null;
            string curName = "USB Display";
            string curStatus = "Unknown";

            void Flush()
            {
                if (!string.IsNullOrEmpty(curId) &&
                    KnownDisplayVidTokens.Any(v => curId.Contains(v, StringComparison.OrdinalIgnoreCase)))
                {
                    bool healthy = curStatus.Contains("Started", StringComparison.OrdinalIgnoreCase) ||
                                   curStatus.Contains("Iniciado", StringComparison.OrdinalIgnoreCase) ||
                                   curStatus.Equals("OK", StringComparison.OrdinalIgnoreCase);
                    list.Add(new UsbDisplayPnpStatus(curName, curId, curStatus, healthy));
                }
                curId = null;
                curName = "USB Display";
                curStatus = "Unknown";
            }

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    Flush();
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line[..colon].Trim();
                string val = line[(colon + 1)..].Trim();

                if (key.Contains("Instance ID", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("Id. de instancia", StringComparison.OrdinalIgnoreCase))
                    curId = val;
                else if (key.Contains("Device Description", StringComparison.OrdinalIgnoreCase) ||
                         key.Contains("Descripción del dispositivo", StringComparison.OrdinalIgnoreCase))
                    curName = val;
                else if (key.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("Estado", StringComparison.OrdinalIgnoreCase))
                    curStatus = val;
            }
            Flush();
        }
        catch
        {
            // Ignore in restricted environments.
        }
        return list;
    }

    public static bool TryRestartDevice(string instanceId, int timeoutMs = 6000)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return false;

        bool allowed = KnownDisplayVidTokens.Any(v => instanceId.Contains(v, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
            return false;

        try
        {
            var psi = new ProcessStartInfo("pnputil.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/restart-device");
            psi.ArgumentList.Add(instanceId);

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
