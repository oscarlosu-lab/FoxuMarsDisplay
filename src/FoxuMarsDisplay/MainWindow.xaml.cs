using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Core.Themes;

namespace FoxuMarsDisplay;

public sealed class ScreenItemVm : INotifyPropertyChanged
{
    private string _resolutionText = string.Empty;
    private string _themeSummary = string.Empty;
    private string _statsText = string.Empty;
    private int _brightness;
    private int _rotation;
    private bool _isEnabled;
    private BitmapImage? _previewImage;

    public string SerialNumber { get; init; } = string.Empty;

    public string ResolutionText
    {
        get => _resolutionText;
        set { if (_resolutionText != value) { _resolutionText = value; OnPropertyChanged(); } }
    }

    public string ThemeSummary
    {
        get => _themeSummary;
        set { if (_themeSummary != value) { _themeSummary = value; OnPropertyChanged(); } }
    }

    public string StatsText
    {
        get => _statsText;
        set { if (_statsText != value) { _statsText = value; OnPropertyChanged(); } }
    }

    public int Brightness
    {
        get => _brightness;
        set { if (_brightness != value) { _brightness = value; OnPropertyChanged(); } }
    }

    public int Rotation
    {
        get => _rotation;
        set { if (_rotation != value) { _rotation = value; OnPropertyChanged(); } }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
    }

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        set { _previewImage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GalleryItemVm
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string? SchemeJsonPath { get; init; }
    public string MetaText { get; init; } = string.Empty;
}

public partial class MainWindow : Window
{
    private readonly DisplayHost _host;
    private readonly DispatcherTimer _uiTimer;
    private readonly ObservableCollection<ScreenItemVm> _screenItems = new();
    private readonly List<GalleryItemVm> _allGalleryItems = new();
    private bool _forceClose;
    private bool _isInitializing = true;
    private bool _isRefreshingUi;

    public MainWindow(DisplayHost host)
    {
        InitializeComponent();
        _host = host;

        BtnElevate.Visibility = App.IsElevated() ? Visibility.Collapsed : Visibility.Visible;
        ScreensList.ItemsSource = _screenItems;

        LoadSystemSettings();
        LoadGallery();
        RefreshUi();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _uiTimer.Tick += (_, _) =>
        {
            if (IsVisible && WindowState != WindowState.Minimized)
                RefreshUi();
        };
        _uiTimer.Start();

        _isInitializing = false;
    }

    public void ForceClose()
    {
        _forceClose = true;
        _uiTimer.Stop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose && _host.Config.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _uiTimer.Stop();
        base.OnClosing(e);
    }

    private void LoadSystemSettings()
    {
        SldTargetFps.Value = _host.Config.TargetFps;
        TxtFpsLabel.Text = $"FPS Animación: {_host.Config.TargetFps} FPS";
        ChkPauseFullscreen.IsChecked = _host.Config.PauseOnFullscreen;
        ChkMinimizeToTray.IsChecked = _host.Config.MinimizeToTray;
        ChkAutoStart.IsChecked = AutoStartHelper.IsEnabled();
    }

    private void LoadGallery()
    {
        _allGalleryItems.Clear();
        var entries = ThemeLibraryScanner.ScanAll(_host.Config.ExtraThemeFolders);
        foreach (var e in entries)
        {
            _allGalleryItems.Add(new GalleryItemVm
            {
                Name = e.Name,
                FullPath = e.ThemePath,
                SchemeJsonPath = e.SchemeJsonPath,
                MetaText = $"{e.Format} • {e.Resolution}"
            });
        }
        ApplyGalleryFilter();
    }

    private void ApplyGalleryFilter()
    {
        string q = (TxtGallerySearch.Text ?? string.Empty).Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _allGalleryItems
            : _allGalleryItems.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                          i.MetaText.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        GalleryList.ItemsSource = filtered;
    }

    private void RefreshUi()
    {
        _isRefreshingUi = true;
        try
        {
            var snap = _host.CurrentSensors;
            TxtQuickTelemetry.Text = $"CPU {snap.CpuTemp:F0}°C ({snap.CpuUsage:F0}%) | GPU {snap.GpuTemp:F0}°C ({snap.GpuUsage:F0}%) | RAM {snap.MemoryUsagePct:F0}%";
            TxtHeaderStatus.Text = _host.IsFullscreenPaused ? "MODO JUEGO (PAUSADO)" : "MOTOR ACTIVO";

            TxtTelCpuTemp.Text = $"{snap.CpuTemp:F1} °C";
            TxtTelCpuSub.Text = $"Uso: {snap.CpuUsage:F0}% | Potencia: {snap.CpuPowerW:F1} W";
            TxtTelGpuTemp.Text = $"{snap.GpuTemp:F1} °C";
            TxtTelGpuSub.Text = $"Uso: {snap.GpuUsage:F0}% | Potencia: {snap.GpuPowerW:F1} W";
            TxtTelRam.Text = $"{snap.MemoryUsagePct:F0} %";
            TxtTelNet.Text = $"↓ {snap.NetDownKbps:F0} KB/s | ↑ {snap.NetUpKbps:F0} KB/s";

            var statuses = _host.GetStatuses();
            var activeSns = statuses.Select(s => s.SerialNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Remove detached screens
            for (int i = _screenItems.Count - 1; i >= 0; i--)
            {
                if (!activeSns.Contains(_screenItems[i].SerialNumber))
                    _screenItems.RemoveAt(i);
            }

            // Update existing or add newly attached screens in-place
            foreach (var s in statuses)
            {
                BitmapImage? preview = null;
                byte[]? png = _host.CapturePreviewPng(s.SerialNumber, 150);
                if (png is not null && png.Length > 0)
                {
                    preview = new BitmapImage();
                    using var ms = new MemoryStream(png);
                    preview.BeginInit();
                    preview.CacheOption = BitmapCacheOption.OnLoad;
                    preview.StreamSource = ms;
                    preview.EndInit();
                    preview.Freeze();
                }

                var vm = _screenItems.FirstOrDefault(x => x.SerialNumber.Equals(s.SerialNumber, StringComparison.OrdinalIgnoreCase));
                if (vm is null)
                {
                    vm = new ScreenItemVm { SerialNumber = s.SerialNumber };
                    _screenItems.Add(vm);
                }

                vm.ResolutionText = $"{s.Width}×{s.Height}";
                vm.ThemeSummary = $"Tema: {s.ThemeName}";
                vm.StatsText = $"Enviados: {s.FramesSent} | KeepAlive: {s.KeepAlivesSent} | Busy: {s.BusyDrops} | Errores: {s.SendErrors}";
                vm.Brightness = s.Brightness;
                vm.Rotation = Math.Clamp(s.Rotation, 0, 3);
                vm.IsEnabled = s.IsEnabled;
                vm.PreviewImage = preview;
            }

            // Only update CmbTargetScreen if serial list changed
            var currentCmb = CmbTargetScreen.Items.Cast<string>().ToList();
            var nextCmb = statuses.Select(s => s.SerialNumber).ToList();
            if (!currentCmb.SequenceEqual(nextCmb, StringComparer.OrdinalIgnoreCase))
            {
                string? prevSelected = CmbTargetScreen.SelectedItem as string;
                CmbTargetScreen.Items.Clear();
                foreach (var sn in nextCmb)
                    CmbTargetScreen.Items.Add(sn);

                if (prevSelected != null && nextCmb.Contains(prevSelected, StringComparer.OrdinalIgnoreCase))
                    CmbTargetScreen.SelectedItem = prevSelected;
                else if (CmbTargetScreen.Items.Count > 0)
                    CmbTargetScreen.SelectedIndex = 0;
            }

            TxtNoScreens.Visibility = statuses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isRefreshingUi = false;
        }
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        PageScreens.Visibility = NavScreens.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageGallery.Visibility = NavGallery.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageTelemetry.Visibility = NavTelemetry.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSystem.Visibility = NavSystem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnElevateClick(object sender, RoutedEventArgs e)
    {
        ((App)System.Windows.Application.Current).RestartElevated();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => Hide();
    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnRefreshScreensClick(object sender, RoutedEventArgs e) => RefreshUi();

    private void OnToggleScreenEnabledClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox cb && cb.Tag is string sn)
            _host.SetScreenEnabled(sn, cb.IsChecked == true);
    }

    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || _isRefreshingUi) return;
        if (sender is Slider s && s.Tag is string sn)
            _host.SetBrightness(sn, (int)Math.Round(e.NewValue));
    }

    private void OnRotationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isRefreshingUi) return;
        if (sender is System.Windows.Controls.ComboBox cmb && cmb.Tag is string sn && cmb.SelectedIndex >= 0)
            _host.SetRotation(sn, cmb.SelectedIndex);
    }

    private void OnPickThemeForScreenClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string sn) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Seleccionar tema para {sn}",
            Filter = "Temas (*.photo;*.smtheme;Setting.txt;theme.yaml;*.gif;*.png;*.jpg)|*.photo;*.smtheme;Setting.txt;theme.yaml;*.gif;*.png;*.jpg|Todos (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            string target = dlg.FileName;
            string fn = Path.GetFileName(target);
            if (fn.Equals("Setting.txt", StringComparison.OrdinalIgnoreCase) ||
                fn.Equals("theme.yaml", StringComparison.OrdinalIgnoreCase))
            {
                target = Path.GetDirectoryName(target)!;
            }
            _host.ApplyTheme(sn, target);
            RefreshUi();
        }
    }

    private void OnGallerySearchChanged(object sender, TextChangedEventArgs e) => ApplyGalleryFilter();
    private void OnRescanGalleryClick(object sender, RoutedEventArgs e) => LoadGallery();

    private void OnApplyGalleryThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn &&
            btn.Tag is GalleryItemVm item &&
            CmbTargetScreen.SelectedItem is string sn)
        {
            _host.ApplyTheme(sn, item.FullPath, item.SchemeJsonPath);
            NavScreens.IsChecked = true;
            RefreshUi();
        }
    }

    private void OnTargetFpsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        int fps = (int)Math.Round(e.NewValue);
        TxtFpsLabel.Text = $"FPS Animación: {fps} FPS";
        _host.SetTargetFps(fps);
    }

    private void OnSystemFlagsChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _host.Config.PauseOnFullscreen = ChkPauseFullscreen.IsChecked == true;
        _host.Config.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
        _host.SaveConfig();
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool enabled = ChkAutoStart.IsChecked == true;
        _host.Config.AutoStartWithWindows = enabled;
        AutoStartHelper.SetEnabled(enabled);
        _host.SaveConfig();
    }

    private void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        int removed = DisplayHost.ClearAnimCache();
        System.Windows.MessageBox.Show($"Caché .sdac limpiada ({removed} archivos eliminados).", "FoxuMarsDisplay", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnKillSoeyiClick(object sender, RoutedEventArgs e)
    {
        foreach (var p in Process.GetProcessesByName("Soeyi"))
        {
            try { p.Kill(); } catch { }
        }
        System.Windows.MessageBox.Show("Procesos Soeyi.exe detenidos.", "FoxuMarsDisplay", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
