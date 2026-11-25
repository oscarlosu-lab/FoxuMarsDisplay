using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Windows;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Services;
using WinForms = System.Windows.Forms;

namespace FoxuMarsDisplay;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Global\FoxuMarsDisplay_SingleInstance_v1";

    private Mutex? _singleInstanceMutex;
    private WinForms.NotifyIcon? _trayIcon;
    private Icon? _trayCustomIcon;
    private DisplayHost? _host;
    private MainWindow? _mainWindow;

    public DisplayHost Host => _host ?? throw new InvalidOperationException("DisplayHost not initialized.");

    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            TrySignalExistingInstanceShowWindow();
            Shutdown();
            return;
        }

        StartHost();
        InitTrayIcon();

        bool startInTray = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (!startInTray)
            ShowMainWindow();
    }

    private static void TrySignalExistingInstanceShowWindow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", IpcPipeServer.PipeName, PipeDirection.InOut);
            client.Connect(500);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine("{\"command\":\"show_window\"}");
            reader.ReadLine();
        }
        catch { }
    }

    private void StartHost()
    {
        var store = new FoxuDisplayConfigStore();
        var config = store.LoadOrImportSoeyi();
        LocalizationService.ApplyLanguage(config.Language);
        _host = new DisplayHost(configStore: store, config: config);
        _host.ShowWindowRequested += () => Dispatcher.BeginInvoke(ShowMainWindow);
        _host.Start(dryRun: false, enableIpc: true);
    }

    public void RestartElevated()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath)) return;

        // Release USB handles and single-instance mutex so the elevated child can take over cleanly.
        try { _host?.Stop(); _host?.Dispose(); } catch { }
        _host = null;
        try { _singleInstanceMutex?.ReleaseMutex(); _singleInstanceMutex?.Dispose(); } catch { }
        _singleInstanceMutex = null;

        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });

            if (proc is not null)
            {
                try { _mainWindow?.ForceClose(); } catch { }
                try { _trayIcon?.Dispose(); } catch { }
                _trayIcon = null;
                Shutdown();
                return;
            }
        }
        catch (Win32Exception)
        {
            // User canceled UAC prompt: recover mutex and host so screens keep running.
        }

        _singleInstanceMutex = new Mutex(true, MutexName, out _);
        StartHost();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(Host);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void InitTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(LocalizationService.T("Str_TrayOpen"), null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add(LocalizationService.T("Str_TrayClearCache"), null, (_, _) =>
        {
            int count = DisplayHost.ClearAnimCache();
            _trayIcon?.ShowBalloonTip(2000, "FoxuMarsDisplay", $"{LocalizationService.T("Str_TrayClearCache")}: {count}", WinForms.ToolTipIcon.Info);
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.T("Str_TrayExit"), null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayCustomIcon = LoadAppIcon();
        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "FoxuMarsDisplay — Cyber-HUD USB Engine",
            Icon = _trayCustomIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var sri = GetResourceStream(new Uri("pack://application:,,,/FoxuMarsDisplay;component/Resources/foxu_mars_icon.ico", UriKind.Absolute));
            if (sri?.Stream is not null)
                return new Icon(sri.Stream);
        }
        catch { }

        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return Icon.ExtractAssociatedIcon(exe);
        }
        catch { }

        return null;
    }

    public void ExitApplication()
    {
        try { _mainWindow?.ForceClose(); } catch { }
        try { _trayIcon?.Dispose(); } catch { }
        _trayIcon = null;
        try { _trayCustomIcon?.Dispose(); } catch { }
        _trayCustomIcon = null;
        try { _host?.Stop(); _host?.Dispose(); } catch { }
        _host = null;
        try { _singleInstanceMutex?.ReleaseMutex(); _singleInstanceMutex?.Dispose(); } catch { }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _trayIcon?.Dispose(); } catch { }
        try { _host?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
