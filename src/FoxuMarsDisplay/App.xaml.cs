using System.Drawing;
using System.Windows;
using FoxuMarsDisplay.Core.Config;
using FoxuMarsDisplay.Core.Host;
using WinForms = System.Windows.Forms;

namespace FoxuMarsDisplay;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Global\FoxuMarsDisplay_SingleInstance_v1";

    private Mutex? _singleInstanceMutex;
    private WinForms.NotifyIcon? _trayIcon;
    private DisplayHost? _host;
    private MainWindow? _mainWindow;

    public DisplayHost Host => _host ?? throw new InvalidOperationException("DisplayHost not initialized.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "FoxuMarsDisplay ya está en ejecución en la bandeja del sistema.",
                "FoxuMarsDisplay",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var store = new FoxuDisplayConfigStore();
        var config = store.LoadOrImportSoeyi();
        _host = new DisplayHost(configStore: store, config: config);
        _host.Start(dryRun: false, enableIpc: true);

        InitTrayIcon();

        bool startInTray = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (!startInTray)
            ShowMainWindow();
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
        menu.Items.Add("Abrir FoxuMarsDisplay", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add("Limpiar caché .sdac", null, (_, _) =>
        {
            int count = DisplayHost.ClearAnimCache();
            _trayIcon?.ShowBalloonTip(2000, "FoxuMarsDisplay", $"Caché limpiada ({count} archivos .sdac).", WinForms.ToolTipIcon.Info);
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "FoxuMarsDisplay — Cyber-HUD USB Engine",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    public void ExitApplication()
    {
        try { _mainWindow?.ForceClose(); } catch { }
        try { _trayIcon?.Dispose(); } catch { }
        _trayIcon = null;
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
