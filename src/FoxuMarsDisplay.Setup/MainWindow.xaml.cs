using System.IO;
using System.Windows;

namespace FoxuMarsDisplay.Setup;

public partial class MainWindow : Window
{
    private readonly bool _isUninstall;
    private bool _finished;

    public MainWindow()
    {
        InitializeComponent();

        _isUninstall = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            InstallerEngine.AppDisplayName);
        TxtInstallDir.Text = defaultDir;

        if (_isUninstall)
        {
            Title = "FOXU // MARS DISPLAY — Desinstalador";
            TxtSubtitle.Text = "Se eliminarán los archivos instalados, tareas programadas y accesos directos.";
            OptionsPanel.IsEnabled = false;
            OptionsPanel.Opacity = 0.55;
            TxtStatus.Text = "Listo para desinstalar";
            BtnAction.Content = "Desinstalar";
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
            BtnAction.Content = "Finalizar";
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
