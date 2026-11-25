using System.Globalization;
using System.IO;
using System.Windows;

namespace FoxuMarsDisplay.Setup;

public partial class MainWindow : Window
{
    private readonly bool _isUninstall;
    private readonly bool _isEs;
    private bool _finished;

    public MainWindow()
    {
        InitializeComponent();

        _isEs = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase);
        _isUninstall = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            InstallerEngine.AppDisplayName);
        TxtInstallDir.Text = defaultDir;

        if (!_isEs)
        {
            Title = "FOXU // MARS DISPLAY — Setup";
            TxtSubtitle.Text = "Permanent install in Program Files + UAC-free autostart (same as SOEYI)";
            TxtInstallDirLabel.Text = "Install directory:";
            ChkDesktopShortcut.Content = "Create Desktop shortcut (FoxuMarsDisplay)";
            ChkAutoBoot.Content = "Start automatically with Windows in tray (no UAC)";
            ChkDisableSoeyi.Content = "Disable SOEYI autostart (SOEYIPowerBoot)";
            ChkLaunchAfter.Content = "Launch FoxuMarsDisplay when finished";
            TxtStatus.Text = "Ready to install";
            BtnCancel.Content = "Close";
            BtnAction.Content = "Install now";
        }

        if (_isUninstall)
        {
            Title = _isEs ? "FOXU // MARS DISPLAY — Desinstalador" : "FOXU // MARS DISPLAY — Uninstaller";
            TxtSubtitle.Text = _isEs
                ? "Se eliminarán los archivos instalados, tareas programadas y accesos directos."
                : "Installed files, scheduled tasks, and shortcuts will be removed.";
            OptionsPanel.IsEnabled = false;
            OptionsPanel.Opacity = 0.55;
            TxtStatus.Text = _isEs ? "Listo para desinstalar" : "Ready to uninstall";
            BtnAction.Content = _isEs ? "Desinstalar" : "Uninstall";
        }
    }

    private async void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (_finished)
        {
            Close();
            return;
        }

        BtnAction.IsEnabled = false;
        BtnCancel.IsEnabled = false;

        try
        {
            if (_isUninstall)
            {
                string dir = TxtInstallDir.Text;
                await Task.Run(() => InstallerEngine.Uninstall(dir, ReportProgress));
            }
            else
            {
                var opts = new InstallOptions
                {
                    InstallDir = TxtInstallDir.Text,
                    CreateDesktopShortcut = ChkDesktopShortcut.IsChecked == true,
                    EnableAutoBoot = ChkAutoBoot.IsChecked == true,
                    DisableSoeyiPowerBoot = ChkDisableSoeyi.IsChecked == true,
                    LaunchAfterInstall = ChkLaunchAfter.IsChecked == true
                };
                await Task.Run(() => InstallerEngine.Install(opts, ReportProgress));
            }

            _finished = true;
            BtnAction.Content = _isEs ? "Finalizar" : "Finish";
            BtnAction.IsEnabled = true;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
            MessageBox.Show(ex.ToString(), "FoxuMarsDisplay Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            BtnAction.IsEnabled = true;
            BtnCancel.IsEnabled = true;
        }
    }

    private void ReportProgress(int pct, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Progress.Value = pct;
            TxtStatus.Text = message;
        });
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
