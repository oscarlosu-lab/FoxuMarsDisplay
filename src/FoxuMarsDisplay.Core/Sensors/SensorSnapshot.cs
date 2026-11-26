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
    public double DiskTotalGb { get; init; }
    public double DiskCUsagePct { get; init; }
    public double DiskDUsagePct { get; init; }
    public double DiskTemp { get; init; }

    public double NetUpKbps { get; init; }
    public double NetDownKbps { get; init; }
    public double PingMs { get; init; }
    public double Fps { get; init; }

    public double Fan2Rpm { get; init; }
    public double Fan3Rpm { get; init; }
    public double Fan4Rpm { get; init; }
    public double SwapUsagePct { get; init; }
    public double SystemLoadPct { get; init; }

    public bool HasWeather { get; init; }
    public double WeatherTempC { get; init; }
    public double WeatherFeelsLikeC { get; init; }
    public double WeatherHumidityPct { get; init; }
    public double WeatherHighC { get; init; }
    public double WeatherLowC { get; init; }
    public string WeatherDescription { get; init; } = string.Empty;
    public string WeatherCity { get; init; } = string.Empty;

    public double AiClaudePct { get; init; }
    public double AiCodexPct { get; init; }
    public double AiGeminiPct { get; init; }
    public int AiActiveTasks { get; init; }

    public double VolumePct { get; init; }
    public bool IsMuted { get; init; }
    public float AudioPeak { get; init; }
    public float[]? AudioBands { get; init; }
    public byte[]? AlbumArtBytes { get; init; }
    public string CurrentLyricLine { get; init; } = string.Empty;
    public bool NumLockOn { get; init; }
    public bool CapsLockOn { get; init; }
    public bool NetworkConnected { get; init; }
    public string SongTitle { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;

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
            "CPUT" or "CPUTEMP" or "CPU_TEMP" or "CPU_TEMPERATURE" or "CPUPACKAGETEMP" or "CPUDIETEMP" or "CPU温度" => CpuTemp,
            "CPUUSAGE" or "CPU_PERCENTAGE" or "CPU_LOAD" or "CPULOAD" or "核心占用" or "CPU监控" => CpuUsage,
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
            "GPUMEMORYTOTAL" or "GPU_MEMORY_TOTAL" => GpuMemoryTotalGb,
            "GPUFAN" or "GPUFANSPEED" or "GPU_FAN" or "CPU_FAN" or "CPUFAN" or "FAN1" or "FANS" => GpuFanPct,
            "FAN2" => Fan2Rpm > 0 ? Fan2Rpm : GpuFanPct,
            "FAN3" => Fan3Rpm > 0 ? Fan3Rpm : GpuFanPct,
            "FAN4" => Fan4Rpm > 0 ? Fan4Rpm : GpuFanPct,

            "MEMORYUSAGE" or "MEMORYUSEINT" or "MEMORYUSE" or "MEMORY_PERCENT" or "RAM" or "内存占用" => MemoryUsagePct,
            "SWAP" => SwapUsagePct > 0 ? SwapUsagePct : MemoryUsagePct,
            "LOAD" or "SYSTEMLOAD" => SystemLoadPct > 0 ? SystemLoadPct : CpuUsage,
            "MEMORYUSED" or "MEM_USED_GB" => MemoryUsedGb,
            "MEMORYFREE" or "MEM_FREE_GB" => MemoryFreeGb,
            "MEMORYTOTAL" or "MEMORY_TOTAL" => MemoryTotalGb,
            "REALMEMORYFREQUENCY" => 3200,

            "DISKUSAGE" or "DISKUTILIZATIONS" or "DISK_PERCENT" => DiskUsagePct,
            "DISKCUSED" or "DISKC_USED" => DiskCUsagePct > 0 ? DiskCUsagePct : DiskUsagePct,
            "DISKDUSED" or "DISKD_USED" => DiskDUsagePct > 0 ? DiskDUsagePct : DiskUsagePct,
            "DISKUSED" or "DISK_USED_GB" => DiskUsedGb,
            "DISKFREE" or "DISK_FREE_GB" => DiskFreeGb,
            "DISKTOTAL" or "DISK_TOTAL" => DiskTotalGb > 0 ? DiskTotalGb : DiskUsedGb + DiskFreeGb,
            "DISKTEMP" or "DISK_TEMP" => DiskTemp,

            "NETUP" or "UPNETSPEED" or "NET_UPLOAD" => NetUpKbps,
            "NETDOWN" or "DOWNNETSPEED" or "NET_DOWNLOAD" => NetDownKbps,
            "VOLUME" => VolumePct,
            "FPS" => Fps,
            "PING" => PingMs,
            "TEMPERATURE_FELT" or "WEATHER_FEELS_LIKE" => WeatherFeelsLikeC,
            "HUMIDITY" or "WEATHER_HUMIDITY" => WeatherHumidityPct,
            "WEATHER_TEMP" or "OUTDOORTEMP" => WeatherTempC,
            "HEIGHTWEATHER" or "WEATHER_HIGH" => WeatherHighC,
            "NIGHTWEATHER" or "WEATHER_LOW" => WeatherLowC,
            "AI_CLAUDE" => AiClaudePct,
            "AI_CODEX" => AiCodexPct,
            "AI_GEMINI" => AiGeminiPct,
            "AI_TASKS" => AiActiveTasks,
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
            "CURRENTDATE" or "CURRENTDATESMD" or "DATE" or "DAY" => Now.ToString("MM/dd", CultureInfo.InvariantCulture),
            "CURRENTDATES" => Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
            "CURRENTDATESD" => Now.ToString("dd", CultureInfo.InvariantCulture),
            "CURRENTDATESM" => Now.ToString("MM", CultureInfo.InvariantCulture),
            "CURRENTDATESY" => Now.ToString("yyyy", CultureInfo.InvariantCulture),
            "CURRENTWEEK" or "WEEKDAYS" => Now.ToString("ddd", CultureInfo.CurrentCulture),
            "LUNARDATE" => Now.ToString("MM/dd", CultureInfo.InvariantCulture),
            "UPTIME" => FormatUptime(),
            "SONGTITLE" => string.IsNullOrWhiteSpace(SongTitle) ? "No Media" : SongTitle,
            "ARTIST" => string.IsNullOrWhiteSpace(Artist) ? string.Empty : Artist,
            "LYRIC" or "LYRICS" or "SONGLYRIC" or "SONGLYRICS" => !string.IsNullOrWhiteSpace(CurrentLyricLine)
                ? CurrentLyricLine
                : (!string.IsNullOrWhiteSpace(SongTitle) ? SongTitle : "No Media"),
            "WEATHER_DESCRIPTION" or "WEATHERINFO" or "TODAY" => HasWeather ? WeatherDescription : "--",
            "NIGHTWEATHER" => HasWeather ? $"{Math.Round(WeatherLowC)}°C" : "--",
            "HEIGHTWEATHER" => HasWeather ? $"{Math.Round(WeatherHighC)}°C" : "--",
            "TEMPERATURE_FELT" or "WEATHER_FEELS_LIKE" => HasWeather ? $"{Math.Round(WeatherFeelsLikeC)}{(string.IsNullOrEmpty(suffix) ? "°C" : suffix)}" : "--",
            "HUMIDITY" or "WEATHER_HUMIDITY" => HasWeather ? $"{Math.Round(WeatherHumidityPct)}{(string.IsNullOrEmpty(suffix) ? "%" : suffix)}" : "--",
            _ => null
        };

        if (text != null)
            return text;

        double val = GetNumericValue(key);
        string numStr = upper switch
        {
            "CPUVOLTAGE" or "CPU_VOLTAGE" => val.ToString("0.00", CultureInfo.InvariantCulture),
            "MEMORYUSED" or "MEM_USED_GB" or
            "MEMORYFREE" or "MEM_FREE_GB" or "MEMORYTOTAL" or "MEMORY_TOTAL" or
            "GPUMEMORYUSED" or "GPU_MEM_USED_GB" or "GPUMEMORYTOTAL" or "GPU_MEMORY_TOTAL" or
            "DISKUSED" or "DISK_USED_GB" or "DISKFREE" or "DISK_FREE_GB" or "DISKTOTAL" or "DISK_TOTAL" => val.ToString("0.0", CultureInfo.InvariantCulture),
            _ => Math.Round(val).ToString("0", CultureInfo.InvariantCulture)
        };

        return string.IsNullOrEmpty(suffix) ? numStr : $"{numStr}{suffix}";
    }

    public bool GetBoolState(string? rawKey)
    {
        var upper = NormalizeKey(rawKey).ToUpperInvariant();
        return upper switch
        {
            "NUMLOCKBOOL" or "NUMLOCK" => NumLockOn,
            "CAPLOCKBOOL" or "CAPSLOCKBOOL" or "CAPSLOCK" => CapsLockOn,
            "MUTEBOOL" or "MUTE" => IsMuted,
            "WIFIBOOL" or "WIFI" => NetworkConnected,
            "BLEBOOL" or "BLUETOOTH" => true,
            "ISFPS" => false,
            "ISLIGHT" => true,
            _ => false
        };
    }

    private static string FormatUptime()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return up.Days > 0 ? $"{up.Days}d {up.Hours:D2}:{up.Minutes:D2}" : $"{up.Hours:D2}:{up.Minutes:D2}";
    }
}
