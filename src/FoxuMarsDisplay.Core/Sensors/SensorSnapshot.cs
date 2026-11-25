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

    public double MemoryUsagePct { get; init; }
    public double MemoryUsedGb { get; init; }

    public double NetUpKbps { get; init; }
    public double NetDownKbps { get; init; }

    public DateTime Now { get; init; } = DateTime.Now;

    public static string NormalizeKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;
        return rawKey.Trim().TrimEnd('@');
    }

    public double GetNumericValue(string? rawKey)
    {
        var key = NormalizeKey(rawKey).ToUpperInvariant();
        return key switch
        {
            "CPUT" or "CPUTEMP" or "CPU温度" => CpuTemp,
            "CPUUSAGE" or "核心占用" or "CPU监控" => CpuUsage,
            "CPUFREQUENCY" or "CPU频率" => CpuFrequencyMhz,
            "CPUTEC" or "CPUPOWER" or "CPU功耗" => CpuPowerW,
            "CPUVOLTAGE" => CpuVoltageV,

            "GPUT" or "GPUTEMP" or "GPU温度" => GpuTemp,
            "GPUUSAGE" or "GPU监控" => GpuUsage,
            "GPUFREQUENCY" or "GPU频率" => GpuFrequencyMhz,
            "GPUTEC" or "GPUPOWER" or "GPU功耗" => GpuPowerW,
            "GPUMEMORYLOAD" or "GPU显存" => GpuMemoryLoad,

            "MEMORYUSAGE" or "内存占用" => MemoryUsagePct,
            "MEMORYUSEINT" => MemoryUsedGb,
            "NETUP" => NetUpKbps,
            "NETDOWN" => NetDownKbps,
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
            "CURRENTTIME" or "CURRENTTIMESHUT" => Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            "CURRENTH" => Now.ToString("HH", CultureInfo.InvariantCulture),
            "CURRENTM" => Now.ToString("mm", CultureInfo.InvariantCulture),
            "CURRENTDATE" or "CURRENTDATES" => Now.ToString("MM/dd", CultureInfo.InvariantCulture),
            "CURRENTDATESD" => Now.ToString("dd", CultureInfo.InvariantCulture),
            "CURRENTDATESM" => Now.ToString("MM", CultureInfo.InvariantCulture),
            "CURRENTDATESY" => Now.ToString("yyyy", CultureInfo.InvariantCulture),
            "CURRENTWEEK" or "WEEKDAYS" => Now.ToString("ddd", CultureInfo.CurrentCulture),
            "LUNARDATE" => Now.ToString("MM/dd", CultureInfo.InvariantCulture),
            "NIGHTWEATHER" or "HEIGHTWEATHER" or "TODAY" => "--",
            _ => null
        };

        if (text != null)
            return text;

        double val = GetNumericValue(key);
        string numStr = upper switch
        {
            "CPUVOLTAGE" => val.ToString("0.00", CultureInfo.InvariantCulture),
            _ => Math.Round(val).ToString("0", CultureInfo.InvariantCulture)
        };

        return string.IsNullOrEmpty(suffix) ? numStr : $"{numStr}{suffix}";
    }
}
