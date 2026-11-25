using System.Globalization;

namespace FoxuMarsDisplay.Core.Sensors;

public sealed class SensorSnapshot
{
    public double CpuTemp { get; init; }
    public double CpuUsage { get; init; }
    public double CpuFrequencyMhz { get; init; }
    public double CpuPowerW { get; init; }
    public double CpuVoltageV { get; init; }

    public double GpuTemp { get; init; }
    public double GpuUsage { get; init; }
    public double GpuFrequencyMhz { get; init; }
    public double GpuPowerW { get; init; }
    public double GpuMemoryLoad { get; init; }
    public double GpuMemoryFreqMhz { get; init; }
    public double GpuMemoryUsedGb { get; init; }
    public double GpuMemoryTotalGb { get; init; }
    public double GpuFanPct { get; init; }

    public double MemoryUsagePct { get; init; }
    public double MemoryUsedGb { get; init; }
    public double MemoryFreeGb { get; init; }
    public double MemoryTotalGb { get; init; }

    public double DiskUsagePct { get; init; }
    public double DiskUsedGb { get; init; }
    public double DiskFreeGb { get; init; }
    public double DiskTemp { get; init; }

    public double NetUpKbps { get; init; }
    public double NetDownKbps { get; init; }

    public DateTime Now { get; init; } = DateTime.Now;

    public static string NormalizeKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;

        string key = rawKey.Trim().TrimEnd('@');
        if (key.EndsWith("PU", StringComparison.OrdinalIgnoreCase) && key.Length > 3)
        {
            string trimmed = key[..^2];
            if (trimmed.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("DISK", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }
        return key;
    }

    public double GetNumericValue(string? rawKey)
    {
        var key = NormalizeKey(rawKey).ToUpperInvariant();
        return key switch
        {
            "CPUT" or "CPUTEMP" or "CPU_TEMP" or "CPU_TEMPERATURE" or "CPU温度" => CpuTemp,
            "CPUUSAGE" or "CPU_PERCENTAGE" or "CPU_LOAD" or "核心占用" or "CPU监控" => CpuUsage,
            "CPUFREQUENCY" or "CPU_FREQUENCY" or "CPU频率" => CpuFrequencyMhz,
            "CPUTEC" or "CPUPOWER" or "CPU_POWER" or "CPU功耗" => CpuPowerW,
            "CPUVOLTAGE" or "CPU_VOLTAGE" => CpuVoltageV,

            "GPUT" or "GPUTEMP" or "GPU_TEMP" or "GPU_TEMPERATURE" or "GPU温度" => GpuTemp,
            "GPUUSAGE" or "GPU_PERCENTAGE" or "GPU_LOAD" or "GPU监控" => GpuUsage,
            "GPUFREQUENCY" or "GPU_FREQUENCY" or "GPU频率" => GpuFrequencyMhz,
            "GPUTEC" or "GPUPOWER" or "GPU_POWER" or "GPU功耗" => GpuPowerW,
            "GPUMEMORYLOAD" or "GPU_MEMORY_PERCENT" or "GPU显存" => GpuMemoryLoad,
            "MEMORYFREQUENCY" or "GPUMEMORYFREQUENCY" or "VRAMFREQUENCY" => GpuMemoryFreqMhz,
            "GPUMEMORYUSED" or "GPU_MEM_USED_GB" => GpuMemoryUsedGb,
            "GPUMEMORYTOTAL" => GpuMemoryTotalGb,
            "GPUFAN" or "GPUFANSPEED" or "GPU_FAN" or "FAN1" => GpuFanPct,

            "MEMORYUSAGE" or "MEMORY_PERCENT" or "RAM" or "内存占用" => MemoryUsagePct,
            "MEMORYUSEINT" or "MEMORYUSED" or "MEM_USED_GB" => MemoryUsedGb,
            "MEMORYFREE" or "MEM_FREE_GB" => MemoryFreeGb,
            "MEMORYTOTAL" => MemoryTotalGb,
            "REALMEMORYFREQUENCY" => 3200,

            "DISKUSAGE" or "DISK_PERCENT" => DiskUsagePct,
            "DISKUSED" or "DISK_USED_GB" => DiskUsedGb,
            "DISKFREE" or "DISK_FREE_GB" => DiskFreeGb,
            "DISKTEMP" or "DISK_TEMP" => DiskTemp,

            "NETUP" or "UPNETSPEED" or "NET_UPLOAD" => NetUpKbps,
            "NETDOWN" or "DOWNNETSPEED" or "NET_DOWNLOAD" => NetDownKbps,
            "REFRESHRATE" => 60,
            _ => 0
        };
    }

    public string FormatValue(string? rawKey, string? unit)
    {
        var key = NormalizeKey(rawKey);
        var upper = key.ToUpperInvariant();
        string suffix = unit ?? string.Empty;

        string? text = upper switch
        {
            "CURRENTTIME" or "CURRENTTIMESHUT" or "TIME" or "HOUR" => Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            "CURRENTH" => Now.ToString("HH", CultureInfo.InvariantCulture),
            "CURRENTM" => Now.ToString("mm", CultureInfo.InvariantCulture),
            "CURRENTDATE" or "DATE" or "DAY" => Now.ToString("MM/dd", CultureInfo.InvariantCulture),
            "CURRENTDATES" => Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
            "CURRENTDATESD" => Now.ToString("dd", CultureInfo.InvariantCulture),
            "CURRENTDATESM" => Now.ToString("MM", CultureInfo.InvariantCulture),
            "CURRENTDATESY" => Now.ToString("yyyy", CultureInfo.InvariantCulture),
            "CURRENTWEEK" or "WEEKDAYS" => Now.ToString("ddd", CultureInfo.CurrentCulture),
            "LUNARDATE" => Now.ToString("MM/dd", CultureInfo.InvariantCulture),
            "NIGHTWEATHER" or "HEIGHTWEATHER" or "TODAY" or "WEATHERINFO" => "--",
            _ => null
        };

        if (text != null)
            return text;

        double val = GetNumericValue(key);
        string numStr = upper switch
        {
            "CPUVOLTAGE" or "CPU_VOLTAGE" => val.ToString("0.00", CultureInfo.InvariantCulture),
            "MEMORYUSEINT" or "MEMORYUSED" or "MEM_USED_GB" or
            "MEMORYFREE" or "MEM_FREE_GB" or "MEMORYTOTAL" or
            "GPUMEMORYUSED" or "GPU_MEM_USED_GB" or "GPUMEMORYTOTAL" or
            "DISKUSED" or "DISK_USED_GB" or "DISKFREE" or "DISK_FREE_GB" => val.ToString("0.0", CultureInfo.InvariantCulture),
            _ => Math.Round(val).ToString("0", CultureInfo.InvariantCulture)
        };

        return string.IsNullOrEmpty(suffix) ? numStr : $"{numStr}{suffix}";
    }
}
