using System.Runtime.InteropServices;

namespace FoxuMarsDisplay.Core.Host;

/// <summary>
/// Detects exclusive Direct3D fullscreen applications (games) via shell32
/// SHQueryUserNotificationState so animated displays can pause at 0% CPU.
/// Only state == 3 (QUNS_RUNNING_D3D_FULL_SCREEN) triggers pause.
/// </summary>
public static partial class FullscreenDetector
{
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int pquns);

    public static bool IsFullscreenGameActive()
    {
        try
        {
            int hr = SHQueryUserNotificationState(out int state);
            return hr == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;
        }
        catch
        {
            return false;
        }
    }
}
