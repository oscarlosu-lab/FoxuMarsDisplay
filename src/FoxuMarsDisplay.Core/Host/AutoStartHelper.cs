using System.Diagnostics;
using Microsoft.Win32;

namespace FoxuMarsDisplay.Core.Host;

/// <summary>
/// Manages Windows startup registration using Task Scheduler (/RL HIGHEST)
/// so elevated FoxuMarsDisplay launches cleanly at logon with --tray,
/// with HKCU\Run fallback if non-elevated.
/// </summary>
public static class AutoStartHelper
{
    private const string TaskName = "FoxuMarsDisplay";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "FoxuMarsDisplay";

    public static bool IsEnabled()
    {
        if (RunSchtasks($"/Query /TN \"{TaskName}\"") == 0)
            return true;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var val = key?.GetValue(AppValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled, string? exePath = null)
    {
        string path = exePath ?? Environment.ProcessPath ?? string.Empty;

        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string cmd = $"\"{path}\" --tray";
            if (RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{path}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F") == 0)
            {
                // Remove legacy HKCU Run entry if Task Scheduler succeeded
                DeleteRunKey();
                return;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.SetValue(AppValueName, cmd);
            }
            catch { }
        }
        else
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            DeleteRunKey();
        }
    }

    private static void DeleteRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppValueName, throwOnMissingValue: false);
        }
        catch { }
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (proc is null) return -1;
            proc.WaitForExit(3000);
            return proc.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
