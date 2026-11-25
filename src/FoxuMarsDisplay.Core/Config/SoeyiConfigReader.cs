using System.Text.Json;

namespace FoxuMarsDisplay.Core.Config;

public sealed record SoeyiDeviceSetting(
    string SerialNumber,
    string SkinName,
    bool IsDisplay,
    int Brightness);

public sealed class SoeyiConfigReader
{
    public string SoeyiProgrammeRoot { get; }
    public string SoeyiThemeSchemeRoot { get; }
    public string ConfigJsonPath { get; }

    public SoeyiConfigReader()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        SoeyiProgrammeRoot = Path.Combine(localAppData, "SOEYI", "Programme");
        SoeyiThemeSchemeRoot = Path.Combine(localAppData, "SOEYI", "ThemeScheme");
        ConfigJsonPath = Path.Combine(roamingAppData, "Hailian Zhixin (Shenzhen) Technology Co., Ltd", "SOEYI", "Config.json");
    }

    public IReadOnlyList<SoeyiDeviceSetting> ReadDeviceSettings()
    {
        var list = new List<SoeyiDeviceSetting>();
        if (!File.Exists(ConfigJsonPath))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigJsonPath));
            if (!doc.RootElement.TryGetProperty("DeviceConfigs", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in arr.EnumerateArray())
            {
                string sn = item.TryGetProperty("SN", out var snEl) ? snEl.GetString() ?? "" : "";
                string skin = item.TryGetProperty("Skin", out var skEl) ? skEl.GetString() ?? "" : "";
                bool isDisplay = !item.TryGetProperty("IsDisplay", out var dispEl) || dispEl.GetBoolean();
                int brightness = item.TryGetProperty("Brightness", out var brEl) && brEl.TryGetInt32(out var b) ? b : 100;

                if (!string.IsNullOrEmpty(sn) && !string.IsNullOrEmpty(skin))
                    list.Add(new SoeyiDeviceSetting(sn, skin, isDisplay, brightness));
            }
        }
        catch { }

        return list;
    }

    public (string? ThemePath, string? SchemeJsonPath) ResolveSkinPaths(string serialNumber, string skinName)
    {
        string themeDir = Path.Combine(SoeyiProgrammeRoot, skinName);
        if (!Directory.Exists(themeDir))
            return (null, null);

        string sanitizedSn = serialNumber.Replace('*', '_');
        string schemeJson = Path.Combine(SoeyiThemeSchemeRoot, sanitizedSn, $"{skinName}.json");
        if (!File.Exists(schemeJson))
            schemeJson = Path.Combine(SoeyiThemeSchemeRoot, $"{skinName}.json");

        return (themeDir, File.Exists(schemeJson) ? schemeJson : null);
    }
}
