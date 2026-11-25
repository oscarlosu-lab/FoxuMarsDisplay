using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace FoxuMarsDisplay.Setup;

public sealed class InstallOptions
{
    public string InstallDir { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FoxuMarsDisplay");
    public bool CreateDesktopShortcut { get; init; } = true;
    public bool EnableAutoBoot { get; init; } = true;
    public bool DisableSoeyiPowerBoot { get; init; }
    public bool LaunchAfterInstall { get; init; } = true;
}

public static class InstallerEngine
{
    public const string AppDisplayName = "FoxuMarsDisplay";
    public const string AppVersion = "1.0.0";
    public const string Publisher = "FoxuLabs";
    public const string BootTaskName = "FoxuMarsDisplayPowerBoot";
    public const string LaunchTaskName = "FoxuMarsDisplayLaunch";
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FoxuMarsDisplay";

    public static void Install(InstallOptions opts, Action<int, string> report)
    {
        report(5, "Deteniendo instancias previas de FoxuMarsDisplay...");
        KillProcessIfRunning("FoxuMarsDisplay");
        KillProcessIfRunning("FoxuMarsDisplay.Launcher");
        Thread.Sleep(350);

        report(15, $"Preparando {opts.InstallDir}...");
        Directory.CreateDirectory(opts.InstallDir);

        report(30, "Extrayendo archivos de FoxuMarsDisplay...");
        ExtractPayload(opts.InstallDir);

        report(50, "Copiando SDK nativo USB (MSDISPLAY / AIC)...");
        CopyNativeDlls(opts.InstallDir);

        report(65, "Creando desinstalador...");
        string selfExe = Environment.ProcessPath ?? string.Empty;
        string uninstallExe = Path.Combine(opts.InstallDir, "Uninstall.exe");
        if (!string.IsNullOrWhiteSpace(selfExe) && File.Exists(selfExe) &&
            !selfExe.Equals(uninstallExe, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(selfExe, uninstallExe, overwrite: true);
        }

        report(78, "Configurando tareas programadas (sin UAC al iniciar)...");
        string mainExe = Path.Combine(opts.InstallDir, "FoxuMarsDisplay.exe");
        string launcherExe = Path.Combine(opts.InstallDir, "FoxuMarsDisplay.Launcher.exe");
        string userAccount = WindowsIdentity.GetCurrent().Name;

        CreateScheduledTask(LaunchTaskName, mainExe, "", opts.InstallDir, userAccount, withLogonTrigger: false);
        if (opts.EnableAutoBoot)
            CreateScheduledTask(BootTaskName, mainExe, "--tray", opts.InstallDir, userAccount, withLogonTrigger: true);
        else
            DeleteScheduledTask(BootTaskName);

        if (opts.DisableSoeyiPowerBoot)
            RunSchtasks("/Change /TN \"SOEYIPowerBoot\" /DISABLE");

        report(88, "Creando accesos directos...");
        string startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs",
            AppDisplayName);
        Directory.CreateDirectory(startMenuDir);
        CreateShortcut(Path.Combine(startMenuDir, $"{AppDisplayName}.lnk"), launcherExe, opts.InstallDir, "FoxuMarsDisplay — Cyber-HUD USB Engine");
        CreateShortcut(Path.Combine(startMenuDir, $"Desinstalar {AppDisplayName}.lnk"), uninstallExe, opts.InstallDir, "Desinstalar FoxuMarsDisplay", "--uninstall");

        if (opts.CreateDesktopShortcut)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktop, $"{AppDisplayName}.lnk"), launcherExe, opts.InstallDir, "FoxuMarsDisplay — Cyber-HUD USB Engine");
        }

        report(94, "Registrando en Programas y Características...");
        RegisterUninstallEntry(opts.InstallDir, uninstallExe, mainExe);

        if (opts.LaunchAfterInstall)
        {
            report(98, "Iniciando FoxuMarsDisplay...");
            RunSchtasks($"/Run /TN \"{LaunchTaskName}\"");
        }

        report(100, "¡Instalación completada!");
    }

    public static void Uninstall(string installDir, Action<int, string> report)
    {
        report(10, "Deteniendo FoxuMarsDisplay...");
        KillProcessIfRunning("FoxuMarsDisplay");
        KillProcessIfRunning("FoxuMarsDisplay.Launcher");
        Thread.Sleep(350);

        report(35, "Eliminando tareas programadas...");
        DeleteScheduledTask(BootTaskName);
        DeleteScheduledTask(LaunchTaskName);

        report(55, "Eliminando accesos directos...");
        TryDeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppDisplayName}.lnk"));
        string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", AppDisplayName);
        if (Directory.Exists(startMenuDir))
        {
            try { Directory.Delete(startMenuDir, recursive: true); } catch { }
        }

        report(75, "Eliminando registro de desinstalación...");
        try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false); } catch { }

        report(90, "Eliminando archivos instalados...");
        if (Directory.Exists(installDir))
        {
            foreach (var f in Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith("Uninstall.exe", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); } catch { }
            }
            // Schedule self-delete of Uninstall.exe and folder after exit
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping 127.0.0.1 -n 3 > nul & rd /s /q \"{installDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        report(100, "FoxuMarsDisplay desinstalado correctamente.");
    }

    private static void ExtractPayload(string installDir)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FoxuMarsDisplay.Setup.payload.zip");
        if (stream is not null)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(installDir, overwriteFiles: true);
            return;
        }

        string adjacentPayload = Path.Combine(AppContext.BaseDirectory, "payload");
        if (Directory.Exists(adjacentPayload))
        {
            CopyDirectory(adjacentPayload, installDir);
            return;
        }

        throw new FileNotFoundException("No se encontró el paquete payload.zip embebido.");
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    private static void CopyNativeDlls(string installDir)
    {
        string nativeOut = Path.Combine(installDir, "native", "x64");
        Directory.CreateDirectory(nativeOut);

        string localSoeyi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SOEYI", "dll", "x64");
        string progSoeyi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SOEYI", "dll", "x64");

        foreach (var srcDir in new[] { localSoeyi, progSoeyi })
        {
            if (!Directory.Exists(srcDir)) continue;
            foreach (var dll in Directory.EnumerateFiles(srcDir, "*.dll"))
            {
                string dest = Path.Combine(nativeOut, Path.GetFileName(dll));
                if (!File.Exists(dest))
                    File.Copy(dll, dest, overwrite: true);
            }
        }
    }

    private static void CreateScheduledTask(string taskName, string exePath, string args, string workDir, string userAccount, bool withLogonTrigger)
    {
        string triggerXml = withLogonTrigger
            ? "<LogonTrigger><Enabled>true</Enabled><Delay>PT3S</Delay></LogonTrigger>"
            : "";
        string argsXml = string.IsNullOrWhiteSpace(args)
            ? ""
            : $"<Arguments>{SecurityElement.Escape(args)}</Arguments>";

        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(userAccount)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Triggers>
                {triggerXml}
              </Triggers>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exePath)}</Command>
                  {argsXml}
                  <WorkingDirectory>{SecurityElement.Escape(workDir)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        string tmpXml = Path.Combine(Path.GetTempPath(), $"foxu_{taskName}_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tmpXml, xml, System.Text.Encoding.Unicode);
            RunSchtasks($"/Create /TN \"{taskName}\" /XML \"{tmpXml}\" /F");
        }
        finally
        {
            TryDeleteFile(tmpXml);
        }
    }

    private static void DeleteScheduledTask(string taskName) => RunSchtasks($"/Delete /TN \"{taskName}\" /F");

    private static void RegisterUninstallEntry(string installDir, string uninstallExe, string displayIcon)
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", AppDisplayName);
        key.SetValue("DisplayVersion", AppVersion);
        key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", displayIcon);
        key.SetValue("UninstallString", $"\"{uninstallExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void CreateShortcut(string lnkPath, string targetPath, string workingDir, string description, string arguments = "")
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        if (t is null) return;
        dynamic shell = Activator.CreateInstance(t)!;
        try
        {
            dynamic sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath = targetPath;
            sc.WorkingDirectory = workingDir;
            sc.Description = description;
            if (!string.IsNullOrEmpty(arguments))
                sc.Arguments = arguments;
            sc.Save();
            Marshal.FinalReleaseComObject(sc);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void KillProcessIfRunning(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(); p.WaitForExit(1500); } catch { }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
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
            proc.WaitForExit(5000);
            return proc.ExitCode;
        }
        catch { return -1; }
    }
}
