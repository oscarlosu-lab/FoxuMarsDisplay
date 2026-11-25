using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FoxuMarsDisplay.Core.Host;

/// <summary>
/// Lightweight Win32 user-idle and foreground-process detector for automatic
/// screensaver activation and game-profile theme switching.
/// </summary>
public static class IdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public static TimeSpan GetIdleTime()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info))
                return TimeSpan.Zero;

            uint tick = unchecked((uint)Environment.TickCount);
            uint idleMs = unchecked(tick - info.dwTime);
            return TimeSpan.FromMilliseconds(idleMs);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    public static string GetForegroundProcessName()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return string.Empty;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
