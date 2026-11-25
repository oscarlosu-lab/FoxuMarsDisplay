using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace FoxuMarsDisplay.Launcher;

internal static class Program
{
    private const string PipeName = "FoxuMarsDisplay_IPC_v1";
    private const string LaunchTaskName = "FoxuMarsDisplayLaunch";

    [STAThread]
    private static void Main()
    {
        // 1. If FoxuMarsDisplay is already running in tray, bring its window to front with 0 UAC.
        if (TryShowExistingWindow())
            return;

        // 2. Ask Windows Task Scheduler to launch FoxuMarsDisplay elevated silently (0 UAC).
        if (TryRunScheduledLaunchTask())
            return;

        // 3. Fallback: launch FoxuMarsDisplay.exe directly from the install folder.
        string exe = Path.Combine(AppContext.BaseDirectory, "FoxuMarsDisplay.exe");
        if (File.Exists(exe))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private static bool TryShowExistingWindow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(300);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine("{\"command\":\"show_window\"}");
            string? resp = reader.ReadLine();
            return !string.IsNullOrEmpty(resp);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRunScheduledLaunchTask()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Run /TN \"{LaunchTaskName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (proc is null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
