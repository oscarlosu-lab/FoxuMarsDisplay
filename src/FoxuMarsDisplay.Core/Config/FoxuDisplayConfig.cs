using System.Text.Json;

namespace FoxuMarsDisplay.Core.Config;

public sealed class FoxuDisplayConfig
{
    public int TargetFps { get; set; } = 12;
    public bool PauseOnFullscreen { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStartWithWindows { get; set; } = false;
    public int NativeLogLevel { get; set; } = 0;
    public string Language { get; set; } = "auto";
    public List<string> ExtraThemeFolders { get; set; } = [];
    public List<ScreenProfile> Screens { get; set; } = [];
}

public sealed class ScreenProfile
{
    public string SerialNumber { get; set; } = string.Empty;
    public string ThemePath { get; set; } = string.Empty;
    public string? SchemeJsonPath { get; set; }
    public int Brightness { get; set; } = 100;
    public int Rotation { get; set; } = -1;
    public int? HardwareRotationOffset { get; set; }
    public int CalibrationVersion { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
    public bool SlideshowEnabled { get; set; } = false;
    public int SlideshowIntervalSec { get; set; } = 300;
    public List<string> SlideshowThemes { get; set; } = [];
}

public sealed class FoxuDisplayConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string ConfigPath { get; }

    public FoxuDisplayConfigStore(string? customConfigPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customConfigPath))
        {
            ConfigPath = customConfigPath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ConfigPath = Path.Combine(localAppData, "FoxuMarsDisplay", "config.json");
        }
    }

    public FoxuDisplayConfig LoadOrImportSoeyi(SoeyiConfigReader? soeyiReader = null)
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<FoxuDisplayConfig>(File.ReadAllText(ConfigPath), JsonOpts);
                if (cfg != null)
                {
                    cfg.Screens ??= [];
                    cfg.ExtraThemeFolders ??= [];
                    return cfg;
                }
            }
            catch { }
        }

        var imported = new FoxuDisplayConfig();
        var reader = soeyiReader ?? new SoeyiConfigReader();
        foreach (var s in reader.ReadDeviceSettings())
        {
            var (themePath, schemeJson) = reader.ResolveSkinPaths(s.SerialNumber, s.SkinName);
            imported.Screens.Add(new ScreenProfile
            {
                SerialNumber = s.SerialNumber,
                ThemePath = themePath ?? string.Empty,
                SchemeJsonPath = schemeJson,
                Brightness = Math.Clamp(s.Brightness, 0, 100),
                IsEnabled = s.IsDisplay
            });
        }

        return imported;
    }

    public void Save(FoxuDisplayConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOpts));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    public static ScreenProfile? FindProfile(FoxuDisplayConfig config, string serialNumber)
    {
        var norm = NormalizeSn(serialNumber);
        return config.Screens.FirstOrDefault(s => NormalizeSn(s.SerialNumber) == norm);
    }

    public static ScreenProfile GetOrCreateProfile(FoxuDisplayConfig config, string serialNumber)
    {
        var existing = FindProfile(config, serialNumber);
        if (existing != null) return existing;

        var created = new ScreenProfile { SerialNumber = serialNumber };
        config.Screens.Add(created);
        return created;
    }

    public static string NormalizeSn(string sn) =>
        (sn ?? string.Empty).Replace('*', '_').Trim().ToUpperInvariant();
}
