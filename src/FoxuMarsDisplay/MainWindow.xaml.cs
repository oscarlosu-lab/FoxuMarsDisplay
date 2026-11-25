using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Core.Themes;

namespace FoxuMarsDisplay;

public sealed record ScreenTargetItem(
    int Index,
    string SerialNumber,
    int NativeWidth,
    int NativeHeight,
    int EffectiveWidth,
    int EffectiveHeight,
    ThemeOrientation EffectiveOrientation,
    string DisplayLabel,
    string ToolTipText);

public sealed class ScreenItemVm : INotifyPropertyChanged
{
    private string _screenTitle = string.Empty;
    private string _resolutionText = string.Empty;
    private string _orientationLabel = string.Empty;
    private System.Windows.Media.Brush _orientationBrush = System.Windows.Media.Brushes.Cyan;
    private string _hardwareSubline = string.Empty;
    private string _themeSummary = string.Empty;
    private string _statsText = string.Empty;
    private int _brightness;
    private int _rotation;
    private bool _isEnabled;
    private BitmapImage? _previewImage;

    public string SerialNumber { get; init; } = string.Empty;

    public string ScreenTitle
    {
        get => _screenTitle;
        set { if (_screenTitle != value) { _screenTitle = value; OnPropertyChanged(); } }
    }

    public string ResolutionText
    {
        get => _resolutionText;
        set { if (_resolutionText != value) { _resolutionText = value; OnPropertyChanged(); } }
    }

    public string OrientationLabel
    {
        get => _orientationLabel;
        set { if (_orientationLabel != value) { _orientationLabel = value; OnPropertyChanged(); } }
    }

    public System.Windows.Media.Brush OrientationBrush
    {
        get => _orientationBrush;
        set { if (_orientationBrush != value) { _orientationBrush = value; OnPropertyChanged(); } }
    }

    public string HardwareSubline
    {
        get => _hardwareSubline;
        set { if (_hardwareSubline != value) { _hardwareSubline = value; OnPropertyChanged(); } }
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

public sealed class GalleryItemVm : INotifyPropertyChanged
{
    private BitmapImage? _thumbnailImage;

    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string? SchemeJsonPath { get; init; }
    public string? ThumbnailPath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public ThemeOrientation Orientation { get; init; }
    public string OrientationBadge { get; init; } = "AUTO";
    public System.Windows.Media.Brush OrientationBrush { get; init; } = System.Windows.Media.Brushes.Gray;
    public string MetaText { get; init; } = string.Empty;

    public BitmapImage? ThumbnailImage
    {
        get => _thumbnailImage;
        set { _thumbnailImage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CloudItemVm : INotifyPropertyChanged
{
    private BitmapImage? _thumbnailImage;
    private string _actionText = "⬇ Descargar y Aplicar";
    private bool _canClick = true;

    public required CloudThemeItem Model { get; init; }
    public string Name => Model.Name;
    public string Source => Model.Source;
    public string OrientationBadge { get; init; } = "AUTO";
    public System.Windows.Media.Brush OrientationBrush { get; init; } = System.Windows.Media.Brushes.Gray;
    public string MetaText { get; init; } = string.Empty;

    public BitmapImage? ThumbnailImage
    {
        get => _thumbnailImage;
        set { _thumbnailImage = value; OnPropertyChanged(); }
    }

    public string ActionText
    {
        get => _actionText;
        set { _actionText = value; OnPropertyChanged(); }
    }

    public bool CanClick
    {
        get => _canClick;
        set { _canClick = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private static readonly System.Windows.Media.Brush BrushVertical = CreateFrozenBrush(0x00, 0xE5, 0xFF);
    private static readonly System.Windows.Media.Brush BrushHorizontal = CreateFrozenBrush(0xF5, 0x9E, 0x0B);
    private static readonly System.Windows.Media.Brush BrushSquare = CreateFrozenBrush(0x10, 0xB9, 0x81);
    private static readonly System.Windows.Media.Brush BrushUnknown = CreateFrozenBrush(0x8A, 0x99, 0xB0);

    private readonly DisplayHost _host;
    private readonly DispatcherTimer _uiTimer;
    private readonly ObservableCollection<ScreenItemVm> _screenItems = new();
    private readonly List<GalleryItemVm> _allGalleryItems = new();
    private readonly CloudThemeService _cloudService = new();
    private readonly List<CloudItemVm> _allCloudItems = new();
    private CancellationTokenSource? _thumbCts;
    private CancellationTokenSource? _cloudCts;
    private bool _cloudLoadedOnce;
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

    private static System.Windows.Media.Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static (string Badge, string Label, System.Windows.Media.Brush Brush) GetOrientationMeta(ThemeOrientation o) => o switch
    {
        ThemeOrientation.Vertical => ("VERTICAL", "↕ VERTICAL", BrushVertical),
        ThemeOrientation.Horizontal => ("HORIZONTAL", "↔ HORIZONTAL", BrushHorizontal),
        ThemeOrientation.Square => ("CUADRADO", "▢ CUADRADO", BrushSquare),
        _ => ("AUTO", "AUTO", BrushUnknown)
    };

    public void ForceClose()
    {
        _forceClose = true;
        _thumbCts?.Cancel();
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
        _thumbCts?.Cancel();
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
        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;

        _allGalleryItems.Clear();
        var entries = ThemeLibraryScanner.ScanAll(_host.Config.ExtraThemeFolders);
        foreach (var e in entries)
        {
            var (badge, _, brush) = GetOrientationMeta(e.Orientation);
            string resLabel = e.Width > 0 && e.Height > 0 ? $"{e.Width}×{e.Height}" : e.Resolution;
            _allGalleryItems.Add(new GalleryItemVm
            {
                Name = e.Name,
                FullPath = e.ThemePath,
                SchemeJsonPath = e.SchemeJsonPath,
                ThumbnailPath = e.ThumbnailPath,
                Width = e.Width,
                Height = e.Height,
                Orientation = e.Orientation,
                OrientationBadge = badge,
                OrientationBrush = brush,
                MetaText = $"{resLabel} • {e.Format}"
            });
        }

        UpdateFilterCounts();
        ApplyGalleryFilter();
        _ = LoadThumbnailsAsync(_allGalleryItems.ToList(), ct);
    }

    private void UpdateFilterCounts()
    {
        int total = _allGalleryItems.Count;
        int vert = _allGalleryItems.Count(x => x.Orientation == ThemeOrientation.Vertical);
        int horiz = _allGalleryItems.Count(x => x.Orientation == ThemeOrientation.Horizontal);
        int sq = _allGalleryItems.Count(x => x.Orientation == ThemeOrientation.Square);

        FilterAll.Content = $"Todos ({total})";
        FilterVertical.Content = $"↕ Vertical ({vert})";
        FilterHorizontal.Content = $"↔ Horizontal ({horiz})";
        FilterSquare.Content = $"▢ Cuadrado ({sq})";
    }

    private async Task LoadThumbnailsAsync(List<GalleryItemVm> items, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(item.ThumbnailPath) || !File.Exists(item.ThumbnailPath))
                    continue;

                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(item.ThumbnailPath, UriKind.Absolute);
                    bmp.DecodePixelHeight = 140;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    Dispatcher.BeginInvoke(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            item.ThumbnailImage = bmp;
                    }, DispatcherPriority.Background);
                }
                catch { }
            }
        }, ct).ConfigureAwait(false);
    }

    private void ApplyGalleryFilter()
    {
        if (GalleryList is null) return;
        string q = (TxtGallerySearch.Text ?? string.Empty).Trim();

        IEnumerable<GalleryItemVm> seq = _allGalleryItems;

        if (FilterVertical.IsChecked == true)
            seq = seq.Where(x => x.Orientation == ThemeOrientation.Vertical);
        else if (FilterHorizontal.IsChecked == true)
            seq = seq.Where(x => x.Orientation == ThemeOrientation.Horizontal);
        else if (FilterSquare.IsChecked == true)
            seq = seq.Where(x => x.Orientation == ThemeOrientation.Square);
        else if (FilterCompatible.IsChecked == true && CmbTargetScreen.SelectedItem is ScreenTargetItem target)
        {
            seq = seq
                .Where(x => x.Orientation == target.EffectiveOrientation || x.Orientation == ThemeOrientation.Unknown)
                .OrderByDescending(x => (x.Width == target.EffectiveWidth && x.Height == target.EffectiveHeight) ||
                                         (x.Width == target.NativeWidth && x.Height == target.NativeHeight))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(q))
        {
            seq = seq.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                 i.MetaText.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        GalleryList.ItemsSource = seq.ToList();
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

            for (int i = _screenItems.Count - 1; i >= 0; i--)
            {
                if (!activeSns.Contains(_screenItems[i].SerialNumber))
                    _screenItems.RemoveAt(i);
            }

            var targetItems = new List<ScreenTargetItem>(statuses.Count);
            int idx = 1;
            foreach (var s in statuses)
            {
                int rot = Math.Clamp(s.Rotation, 0, 3);
                bool swapped = rot == 1 || rot == 3;
                int effW = swapped ? s.Height : s.Width;
                int effH = swapped ? s.Width : s.Height;
                var effOri = ThemeLibraryScanner.ClassifyOrientation(effW, effH);
                var (_, oriLabel, oriBrush) = GetOrientationMeta(effOri);

                string shortOri = effOri switch
                {
                    ThemeOrientation.Vertical => "Vertical",
                    ThemeOrientation.Horizontal => "Horizontal",
                    ThemeOrientation.Square => "Cuadrado",
                    _ => "Auto"
                };

                string displayLabel = swapped
                    ? $"Pantalla {idx} · {effW}×{effH} {shortOri} (Panel {s.Width}×{s.Height})"
                    : $"Pantalla {idx} · {s.Width}×{s.Height} {shortOri}";

                targetItems.Add(new ScreenTargetItem(
                    idx, s.SerialNumber, s.Width, s.Height, effW, effH, effOri,
                    displayLabel, $"SN: {s.SerialNumber}"));

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

                vm.ScreenTitle = $"PANTALLA {idx}";
                vm.ResolutionText = $"{effW} × {effH} px";
                vm.OrientationLabel = oriLabel;
                vm.OrientationBrush = oriBrush;
                vm.HardwareSubline = swapped
                    ? $"Panel nativo {s.Width}×{s.Height} (rotado {rot * 90}°) · SN: {s.SerialNumber}"
                    : $"Panel nativo {s.Width}×{s.Height} · SN: {s.SerialNumber}";
                vm.ThemeSummary = $"Tema activo: {s.ThemeName}";
                vm.StatsText = $"Enviados: {s.FramesSent} | KeepAlive: {s.KeepAlivesSent} | Busy: {s.BusyDrops} | Errores: {s.SendErrors}";
                vm.Brightness = s.Brightness;
                vm.Rotation = rot;
                vm.IsEnabled = s.IsEnabled;
                vm.PreviewImage = preview;

                idx++;
            }

            var currentLabels = CmbTargetScreen.Items.OfType<ScreenTargetItem>().Select(x => x.DisplayLabel).ToList();
            var nextLabels = targetItems.Select(x => x.DisplayLabel).ToList();
            if (!currentLabels.SequenceEqual(nextLabels))
            {
                string? prevSn = (CmbTargetScreen.SelectedItem as ScreenTargetItem)?.SerialNumber;
                CmbTargetScreen.Items.Clear();
                foreach (var item in targetItems)
                    CmbTargetScreen.Items.Add(item);

                var restored = prevSn != null
                    ? targetItems.FirstOrDefault(x => x.SerialNumber.Equals(prevSn, StringComparison.OrdinalIgnoreCase))
                    : null;
                CmbTargetScreen.SelectedItem = restored ?? targetItems.FirstOrDefault();

                string? prevCloudSn = (CmbCloudTargetScreen.SelectedItem as ScreenTargetItem)?.SerialNumber;
                CmbCloudTargetScreen.Items.Clear();
                foreach (var item in targetItems)
                    CmbCloudTargetScreen.Items.Add(item);
                var restoredCloud = prevCloudSn != null
                    ? targetItems.FirstOrDefault(x => x.SerialNumber.Equals(prevCloudSn, StringComparison.OrdinalIgnoreCase))
                    : null;
                CmbCloudTargetScreen.SelectedItem = restoredCloud ?? targetItems.FirstOrDefault();
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
        PageCloud.Visibility = NavCloud.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageTelemetry.Visibility = NavTelemetry.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSystem.Visibility = NavSystem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (NavCloud.IsChecked == true && !_cloudLoadedOnce)
            _ = LoadCloudCatalogAsync();
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
    private void OnOrientationFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing) ApplyGalleryFilter();
    }

    private void OnTargetScreenChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && !_isRefreshingUi && FilterCompatible.IsChecked == true)
            ApplyGalleryFilter();
    }

    private void OnRescanGalleryClick(object sender, RoutedEventArgs e) => LoadGallery();

    private void OnApplyGalleryThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn &&
            btn.Tag is GalleryItemVm item &&
            CmbTargetScreen.SelectedItem is ScreenTargetItem target)
        {
            _host.ApplyTheme(target.SerialNumber, item.FullPath, item.SchemeJsonPath);
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

    private CloudSourceMode GetSelectedCloudMode() => CmbCloudSource.SelectedIndex switch
    {
        1 => CloudSourceMode.HuggingFaceOnly,
        2 => CloudSourceMode.Both,
        _ => CloudSourceMode.AutoSoeyiWithHfFallback
    };

    private async Task LoadCloudCatalogAsync()
    {
        _cloudLoadedOnce = true;
        _cloudCts?.Cancel();
        _cloudCts?.Dispose();
        _cloudCts = new CancellationTokenSource();
        var ct = _cloudCts.Token;

        TxtCloudStatus.Text = "Cargando catálogo...";
        var result = await _cloudService.FetchCatalogAsync(GetSelectedCloudMode(), ct);
        if (ct.IsCancellationRequested) return;

        _allCloudItems.Clear();
        foreach (var item in result.Items)
        {
            var (badge, _, brush) = GetOrientationMeta(item.Orientation);
            string resLabel = item.Width > 0 && item.Height > 0 ? $"{item.Width}×{item.Height}" : item.Resolution;
            _allCloudItems.Add(new CloudItemVm
            {
                Model = item,
                OrientationBadge = badge,
                OrientationBrush = brush,
                MetaText = $"{resLabel} • {item.Format}",
                ActionText = item.IsDownloaded ? "▶ Aplicar ahora" : "⬇ Descargar y Aplicar"
            });
        }

        CloudFilterAll.Content = $"Todos ({_allCloudItems.Count})";
        CloudFilterVertical.Content = $"↕ Vertical ({_allCloudItems.Count(x => x.Model.Orientation == ThemeOrientation.Vertical)})";
        CloudFilterHorizontal.Content = $"↔ Horizontal ({_allCloudItems.Count(x => x.Model.Orientation == ThemeOrientation.Horizontal)})";
        CloudFilterSquare.Content = $"▢ Cuadrado ({_allCloudItems.Count(x => x.Model.Orientation == ThemeOrientation.Square)})";

        TxtCloudStatus.Text = $"{result.ActiveSourceLabel} · {_allCloudItems.Count} temas";
        ApplyCloudFilter();
        _ = LoadCloudThumbnailsAsync(_allCloudItems.ToList(), ct);
    }

    private async Task LoadCloudThumbnailsAsync(List<CloudItemVm> items, CancellationToken ct)
    {
        using var sem = new SemaphoreSlim(6);
        var tasks = items.Select(async vm =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (ct.IsCancellationRequested) return;
                string? localThumb = await _cloudService.GetOrDownloadThumbnailAsync(vm.Model, ct).ConfigureAwait(false);
                if (localThumb is null || ct.IsCancellationRequested) return;

                BitmapImage? bmp = DecodeBitmapWithSkiaFallback(localThumb);
                if (bmp is null) return;

                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (!ct.IsCancellationRequested)
                        vm.ThumbnailImage = bmp;
                }, DispatcherPriority.Background);
            }
            catch { }
            finally
            {
                sem.Release();
            }
        });

        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
    }

    private static BitmapImage? DecodeBitmapWithSkiaFallback(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelHeight = 140;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            try
            {
                using var sk = SkiaSharp.SKBitmap.Decode(path);
                if (sk is null) return null;
                using var img = SkiaSharp.SKImage.FromBitmap(sk);
                using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
                using var ms = new MemoryStream(data.ToArray());
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.DecodePixelHeight = 140;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }

    private void ApplyCloudFilter()
    {
        if (CloudList is null) return;
        string q = (TxtCloudSearch.Text ?? string.Empty).Trim();

        IEnumerable<CloudItemVm> seq = _allCloudItems;
        if (CloudFilterVertical.IsChecked == true)
            seq = seq.Where(x => x.Model.Orientation == ThemeOrientation.Vertical);
        else if (CloudFilterHorizontal.IsChecked == true)
            seq = seq.Where(x => x.Model.Orientation == ThemeOrientation.Horizontal);
        else if (CloudFilterSquare.IsChecked == true)
            seq = seq.Where(x => x.Model.Orientation == ThemeOrientation.Square);
        else if (CloudFilterCompatible.IsChecked == true && CmbCloudTargetScreen.SelectedItem is ScreenTargetItem target)
        {
            seq = seq
                .Where(x => x.Model.Orientation == target.EffectiveOrientation || x.Model.Orientation == ThemeOrientation.Unknown)
                .OrderByDescending(x => (x.Model.Width == target.EffectiveWidth && x.Model.Height == target.EffectiveHeight) ||
                                         (x.Model.Width == target.NativeWidth && x.Model.Height == target.NativeHeight))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(q))
            seq = seq.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.MetaText.Contains(q, StringComparison.OrdinalIgnoreCase));

        CloudList.ItemsSource = seq.ToList();
    }

    private void OnCloudSearchChanged(object sender, TextChangedEventArgs e) => ApplyCloudFilter();
    private void OnCloudSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && NavCloud.IsChecked == true)
            _ = LoadCloudCatalogAsync();
    }
    private void OnCloudFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing) ApplyCloudFilter();
    }
    private void OnCloudTargetScreenChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && !_isRefreshingUi && CloudFilterCompatible.IsChecked == true)
            ApplyCloudFilter();
    }
    private void OnReloadCloudClick(object sender, RoutedEventArgs e) => _ = LoadCloudCatalogAsync();

    private async void OnDownloadAndApplyCloudClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not CloudItemVm vm)
            return;

        if (CmbCloudTargetScreen.SelectedItem is not ScreenTargetItem target)
        {
            System.Windows.MessageBox.Show("No hay ninguna pantalla USB conectada.", "FoxuMarsDisplay", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        vm.CanClick = false;
        try
        {
            string localPath = vm.Model.LocalFilePath;
            if (!File.Exists(localPath))
            {
                var progress = new Progress<int>(pct => vm.ActionText = $"Descargando {pct}%");
                localPath = await _cloudService.DownloadThemeAsync(vm.Model, progress);
            }

            vm.ActionText = "▶ Aplicar ahora";
            _host.ApplyTheme(target.SerialNumber, localPath);
            LoadGallery();
            NavScreens.IsChecked = true;
            RefreshUi();
        }
        catch (Exception ex)
        {
            vm.ActionText = "⬇ Reintentar";
            System.Windows.MessageBox.Show($"Error al descargar '{vm.Name}':\n{ex.Message}", "FoxuMarsDisplay", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            vm.CanClick = true;
        }
    }
}
