using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;
using FoxuMarsDisplay.Services;

namespace FoxuMarsDisplay;

public sealed record ScreenTargetItem(
    int Index,
    string SerialNumber,
    string HardwareClass,
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
    private int _scaleModeIndex;
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

    public int ScaleModeIndex
    {
        get => _scaleModeIndex;
        set { if (_scaleModeIndex != value) { _scaleModeIndex = value; OnPropertyChanged(); } }
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

public sealed record ResolutionFilterItem(string Key, string Label);

public sealed class GalleryItemVm : INotifyPropertyChanged
{
    private BitmapImage? _thumbnailImage;
    private string _compatBadgeText = string.Empty;
    private System.Windows.Media.Brush _compatBadgeBrush = System.Windows.Media.Brushes.Gray;
    private Visibility _compatBadgeVisibility = Visibility.Collapsed;
    private double _cardOpacity = 1.0;
    private string _actionText = string.Empty;
    private bool _canClick = true;
    private string? _actionToolTip;

    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string? SchemeJsonPath { get; init; }
    public string? ThumbnailPath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public ThemeOrientation Orientation { get; init; }
    public ThemeFormFactor FormFactor { get; init; }
    public string OrientationBadge { get; init; } = "AUTO";
    public System.Windows.Media.Brush OrientationBrush { get; init; } = System.Windows.Media.Brushes.Gray;
    public string MetaText { get; init; } = string.Empty;

    public string CompatBadgeText
    {
        get => _compatBadgeText;
        set { if (_compatBadgeText != value) { _compatBadgeText = value; OnPropertyChanged(); } }
    }

    public System.Windows.Media.Brush CompatBadgeBrush
    {
        get => _compatBadgeBrush;
        set { if (!ReferenceEquals(_compatBadgeBrush, value)) { _compatBadgeBrush = value; OnPropertyChanged(); } }
    }

    public Visibility CompatBadgeVisibility
    {
        get => _compatBadgeVisibility;
        set { if (_compatBadgeVisibility != value) { _compatBadgeVisibility = value; OnPropertyChanged(); } }
    }

    public double CardOpacity
    {
        get => _cardOpacity;
        set { if (Math.Abs(_cardOpacity - value) > 0.01) { _cardOpacity = value; OnPropertyChanged(); } }
    }

    public string ActionText
    {
        get => _actionText;
        set { if (_actionText != value) { _actionText = value; OnPropertyChanged(); } }
    }

    public bool CanClick
    {
        get => _canClick;
        set { if (_canClick != value) { _canClick = value; OnPropertyChanged(); } }
    }

    public string? ActionToolTip
    {
        get => _actionToolTip;
        set { if (_actionToolTip != value) { _actionToolTip = value; OnPropertyChanged(); } }
    }

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
    private string? _actionToolTip;
    private string _compatBadgeText = string.Empty;
    private System.Windows.Media.Brush _compatBadgeBrush = System.Windows.Media.Brushes.Gray;
    private Visibility _compatBadgeVisibility = Visibility.Collapsed;
    private double _cardOpacity = 1.0;

    public required CloudThemeItem Model { get; init; }
    public string Name => Model.Name;
    public string Source => Model.Source;
    public string? CachedThumbPath { get; set; }
    public string OrientationBadge { get; init; } = "AUTO";
    public System.Windows.Media.Brush OrientationBrush { get; init; } = System.Windows.Media.Brushes.Gray;
    public string MetaText { get; init; } = string.Empty;

    public string CompatBadgeText
    {
        get => _compatBadgeText;
        set { if (_compatBadgeText != value) { _compatBadgeText = value; OnPropertyChanged(); } }
    }

    public System.Windows.Media.Brush CompatBadgeBrush
    {
        get => _compatBadgeBrush;
        set { if (!ReferenceEquals(_compatBadgeBrush, value)) { _compatBadgeBrush = value; OnPropertyChanged(); } }
    }

    public Visibility CompatBadgeVisibility
    {
        get => _compatBadgeVisibility;
        set { if (_compatBadgeVisibility != value) { _compatBadgeVisibility = value; OnPropertyChanged(); } }
    }

    public double CardOpacity
    {
        get => _cardOpacity;
        set { if (Math.Abs(_cardOpacity - value) > 0.01) { _cardOpacity = value; OnPropertyChanged(); } }
    }

    public BitmapImage? ThumbnailImage
    {
        get => _thumbnailImage;
        set { _thumbnailImage = value; OnPropertyChanged(); }
    }

    public string ActionText
    {
        get => _actionText;
        set { if (_actionText != value) { _actionText = value; OnPropertyChanged(); } }
    }

    public bool CanClick
    {
        get => _canClick;
        set { if (_canClick != value) { _canClick = value; OnPropertyChanged(); } }
    }

    public string? ActionToolTip
    {
        get => _actionToolTip;
        set { if (_actionToolTip != value) { _actionToolTip = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private static readonly System.Windows.Media.Brush BrushVertical = CreateFrozenBrush(0xFF, 0x7A, 0x2F);
    private static readonly System.Windows.Media.Brush BrushHorizontal = CreateFrozenBrush(0xFF, 0xB8, 0x20);
    private static readonly System.Windows.Media.Brush BrushSquare = CreateFrozenBrush(0xB5, 0x7B, 0xFF);
    private static readonly System.Windows.Media.Brush BrushUnknown = CreateFrozenBrush(0x98, 0x8F, 0xAD);
    private static readonly System.Windows.Media.Brush BrushExact = CreateFrozenBrush(0x22, 0xE5, 0x8B);
    private static readonly System.Windows.Media.Brush BrushIncompat = CreateFrozenBrush(0xFF, 0x1F, 0x4B);

    private readonly DisplayHost _host;
    private readonly DispatcherTimer _uiTimer;
    private readonly ObservableCollection<ScreenItemVm> _screenItems = new();
    private bool _suppressWeatherCombo;
    private readonly List<ScreenTargetItem> _screenTargets = new();
    private readonly List<GalleryItemVm> _allGalleryItems = new();
    private readonly CloudThemeService _cloudService = new();
    private readonly List<CloudItemVm> _allCloudItems = new();
    private CancellationTokenSource? _thumbCts;
    private CancellationTokenSource? _cloudCts;
    private CancellationTokenSource? _cloudThumbCts;
    private string? _selectedTargetSerial;
    private string _lastTargetSignature = string.Empty;
    private bool _cloudLoadedOnce;
    private bool _forceClose;
    private bool _isInitializing = true;
    private bool _isRefreshingUi;
    private bool _thumbnailsPurged;

    public MainWindow(DisplayHost host)
    {
        InitializeComponent();
        _host = host;

        BtnElevate.Visibility = App.IsElevated() ? Visibility.Collapsed : Visibility.Visible;
        ScreensList.ItemsSource = _screenItems;

        LoadSystemSettings();
        TxtUpdateStatus.Text = $"Versión instalada: v{GetCurrentAppVersion()}";
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
        ThemeOrientation.Vertical => (LocalizationService.T("Str_OriVertical"), $"↕ {LocalizationService.T("Str_OriVertical")}", BrushVertical),
        ThemeOrientation.Horizontal => (LocalizationService.T("Str_OriHorizontal"), $"↔ {LocalizationService.T("Str_OriHorizontal")}", BrushHorizontal),
        ThemeOrientation.Square => (LocalizationService.T("Str_OriSquare"), $"▢ {LocalizationService.T("Str_OriSquare")}", BrushSquare),
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
            TrimVisualMemoryOnHide();
            return;
        }
        _thumbCts?.Cancel();
        _cloudCts?.Cancel();
        _uiTimer.Stop();
        base.OnClosing(e);
    }

    public void TrimVisualMemoryOnHide()
    {
        if (_thumbnailsPurged) return;
        _thumbnailsPurged = true;

        _thumbCts?.Cancel();
        _cloudCts?.Cancel();
        CloseLightbox();

        foreach (var g in _allGalleryItems)
            g.ThumbnailImage = null;
        foreach (var c in _allCloudItems)
            c.ThumbnailImage = null;
        foreach (var s in _screenItems)
            s.PreviewImage = null;

        GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
    }

    public void RestoreVisualsOnShow()
    {
        if (_thumbnailsPurged)
        {
            _thumbnailsPurged = false;
            if (NavGallery.IsChecked == true)
                RestartGalleryThumbnails();
            else if (NavCloud.IsChecked == true && _allCloudItems.Count > 0)
                RestartCloudThumbnails();
        }
        RefreshUi();
    }

    private void RestartGalleryThumbnails()
    {
        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        _thumbCts = new CancellationTokenSource();
        _ = LoadThumbnailsAsync(_allGalleryItems.ToList(), _thumbCts.Token);
    }

    private void RestartCloudThumbnails()
    {
        _cloudCts?.Cancel();
        _cloudCts?.Dispose();
        _cloudCts = new CancellationTokenSource();
        _ = LoadCloudThumbnailsAsync(_allCloudItems.ToList(), _cloudCts.Token);
    }

    private void LoadSystemSettings()
    {
        SldTargetFps.Value = _host.Config.TargetFps;
        TxtFpsLabel.Text = LocalizationService.Format("Str_FpsLabelFmt", _host.Config.TargetFps);
        ChkPauseFullscreen.IsChecked = _host.Config.PauseOnFullscreen;
        ChkMinimizeToTray.IsChecked = _host.Config.MinimizeToTray;
        ChkAutoStart.IsChecked = AutoStartHelper.IsEnabled();

        for (int h = 0; h < 24; h++)
        {
            string label = $"{h:00}:00";
            CmbNightStart.Items.Add(new ComboBoxItem { Tag = h, Content = label });
            CmbNightEnd.Items.Add(new ComboBoxItem { Tag = h, Content = label });
        }
        ChkNightBrightness.IsChecked = _host.Config.NightBrightnessEnabled;
        CmbNightStart.SelectedIndex = Math.Clamp(_host.Config.NightStartHour, 0, 23);
        CmbNightEnd.SelectedIndex = Math.Clamp(_host.Config.NightEndHour, 0, 23);
        SldNightBrightness.Value = Math.Clamp(_host.Config.NightBrightnessPct, 5, 100);
        TxtNightLevelLabel.Text = LocalizationService.Format("Str_NightLevelFmt", Math.Clamp(_host.Config.NightBrightnessPct, 5, 100));

        SldLyricsOffset.Value = Math.Clamp(_host.Config.LyricsOffsetMs, -5000, 5000);
        TxtLyricsOffsetLabel.Text = LocalizationService.Format("Str_LyricsOffsetFmt", _host.Config.LyricsOffsetMs);

        ChkHttpRemote.IsChecked = _host.Config.HttpRemoteEnabled;
        BtnOpenHttpRemote.IsEnabled = _host.Config.HttpRemoteEnabled;

        TxtWeatherSearch.Text = _host.Config.WeatherCity;
        UpdateWeatherLocationStatus();

        string lang = string.IsNullOrWhiteSpace(_host.Config.Language) ? "auto" : _host.Config.Language;
        foreach (ComboBoxItem item in CmbLanguage.Items)
        {
            if ((item.Tag as string)?.Equals(lang, StringComparison.OrdinalIgnoreCase) == true)
            {
                CmbLanguage.SelectedItem = item;
                break;
            }
        }
        if (CmbLanguage.SelectedIndex < 0 && CmbLanguage.Items.Count > 0)
            CmbLanguage.SelectedIndex = 0;
    }

    private ScreenTargetItem? GetSelectedTargetScreen()
    {
        if (_screenTargets.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(_selectedTargetSerial))
        {
            var found = _screenTargets.FirstOrDefault(x => x.SerialNumber.Equals(_selectedTargetSerial, StringComparison.OrdinalIgnoreCase));
            if (found is not null) return found;
        }
        return _screenTargets[0];
    }

    private void RebuildScreenPills()
    {
        var selected = GetSelectedTargetScreen();
        _selectedTargetSerial = selected?.SerialNumber;

        void PopulatePanel(System.Windows.Controls.WrapPanel panel, System.Windows.Controls.TextBlock emptyText, string groupName)
        {
            panel.Children.Clear();
            emptyText.Visibility = _screenTargets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var pillStyle = TryFindResource("ScreenPillRadio") as Style
                ?? System.Windows.Application.Current?.TryFindResource("ScreenPillRadio") as Style;

            foreach (var screen in _screenTargets)
            {
                var rb = new System.Windows.Controls.RadioButton
                {
                    Content = $"🖥️ {screen.DisplayLabel}",
                    ToolTip = screen.ToolTipText,
                    GroupName = groupName,
                    Tag = screen.SerialNumber,
                    IsChecked = selected is not null && screen.SerialNumber.Equals(selected.SerialNumber, StringComparison.OrdinalIgnoreCase),
                    Margin = new Thickness(0, 2, 8, 2)
                };
                if (pillStyle is not null)
                    rb.Style = pillStyle;
                rb.Checked += OnScreenPillChecked;
                panel.Children.Add(rb);
            }
        }

        PopulatePanel(GalleryScreenPills, TxtGalleryNoScreen, "GalleryTargetScreen");
        PopulatePanel(CloudScreenPills, TxtCloudNoScreen, "CloudTargetScreen");
    }

    private void OnScreenPillChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _isRefreshingUi) return;
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string sn)
            SelectTargetScreen(sn);
    }

    private void SelectTargetScreen(string serialNumber)
    {
        if (string.Equals(_selectedTargetSerial, serialNumber, StringComparison.OrdinalIgnoreCase))
            return;

        _selectedTargetSerial = serialNumber;
        _isRefreshingUi = true;
        try
        {
            foreach (var panel in new[] { GalleryScreenPills, CloudScreenPills })
            {
                foreach (var rb in panel.Children.OfType<System.Windows.Controls.RadioButton>())
                {
                    rb.IsChecked = rb.Tag is string sn && sn.Equals(serialNumber, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (CmbGalleryResolution.Items.Count > 0) CmbGalleryResolution.SelectedIndex = 0;
            if (CmbCloudResolution.Items.Count > 0) CmbCloudResolution.SelectedIndex = 0;
        }
        finally
        {
            _isRefreshingUi = false;
        }

        UpdateGalleryFilterCounts();
        ApplyGalleryFilter();
        if (_cloudLoadedOnce)
        {
            UpdateCloudFilterCounts();
            ApplyCloudFilter();
        }
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
                FormFactor = e.FormFactor,
                OrientationBadge = badge,
                OrientationBrush = brush,
                MetaText = $"{resLabel} • {e.Format}"
            });
        }

        UpdateGalleryFilterCounts();
        ApplyGalleryFilter();
        _ = LoadThumbnailsAsync(_allGalleryItems.ToList(), ct);
    }

    private void UpdateGalleryFilterCounts()
    {
        int total = _allGalleryItems.Count;
        int vert = _allGalleryItems.Count(x => x.Orientation == ThemeOrientation.Vertical);
        int horiz = _allGalleryItems.Count(x => x.Orientation == ThemeOrientation.Horizontal);
        int sq = _allGalleryItems.Count(x => x.Orientation == ThemeOrientation.Square);

        var target = GetSelectedTargetScreen();
        int compat = target is null ? 0 : _allGalleryItems.Count(x =>
            ThemeLibraryScanner.EvaluateCompatibility(x.Width, x.Height, target.EffectiveWidth, target.EffectiveHeight) != ThemeCompatibility.Incompatible);

        ChkGalleryOnlyCompatible.Content = LocalizationService.Format("Str_OnlyCompatibleFmt", compat);
        FilterAll.Content = LocalizationService.Format("Str_FilterAllFmt", total);
        FilterVertical.Content = LocalizationService.Format("Str_FilterVertFmt", vert);
        FilterHorizontal.Content = LocalizationService.Format("Str_FilterHorizFmt", horiz);
        FilterSquare.Content = LocalizationService.Format("Str_FilterSquareFmt", sq);
    }

    private static void PopulateResolutionCombo(
        System.Windows.Controls.ComboBox combo,
        IEnumerable<(int W, int H)> sizes,
        ScreenTargetItem? target)
    {
        string? prevKey = (combo.SelectedItem as ResolutionFilterItem)?.Key;
        string? targetKey = target is not null ? $"{target.EffectiveWidth}x{target.EffectiveHeight}" : null;

        var grouped = sizes
            .Where(s => s.W > 0 && s.H > 0)
            .GroupBy(s => $"{s.W}x{s.H}")
            .Select(g => new { Key = g.Key, W = g.First().W, H = g.First().H, Count = g.Count() })
            .OrderByDescending(x => x.Key == targetKey)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.W)
            .ToList();

        combo.Items.Clear();
        combo.Items.Add(new ResolutionFilterItem("ALL", LocalizationService.T("Str_AllResolutions")));
        foreach (var g in grouped)
            combo.Items.Add(new ResolutionFilterItem(g.Key, $"{g.W}×{g.H} ({g.Count})"));

        var restore = prevKey != null
            ? combo.Items.OfType<ResolutionFilterItem>().FirstOrDefault(i => i.Key == prevKey)
            : null;
        combo.SelectedItem = restore ?? combo.Items[0];
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

    private static double AspectDelta(int w, int h, ScreenTargetItem target)
    {
        if (w <= 0 || h <= 0 || target.EffectiveWidth <= 0 || target.EffectiveHeight <= 0) return 999.0;
        double tr = (double)w / h;
        double sr = (double)target.EffectiveWidth / target.EffectiveHeight;
        return Math.Abs(tr - sr) / sr;
    }

    private static string FormatOrientationName(ThemeOrientation o) => o switch
    {
        ThemeOrientation.Vertical => LocalizationService.T("Str_OriVerticalFull"),
        ThemeOrientation.Horizontal => LocalizationService.T("Str_OriHorizontalFull"),
        ThemeOrientation.Square => LocalizationService.T("Str_OriSquareFull"),
        _ => "Auto"
    };

    private void ApplyGalleryFilter()
    {
        if (GalleryList is null) return;
        string q = (TxtGallerySearch.Text ?? string.Empty).Trim();
        TxtGallerySearchWatermark.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;

        var target = GetSelectedTargetScreen();
        bool onlyCompat = ChkGalleryOnlyCompatible.IsChecked == true && target is not null;
        GalleryExtraOrientPanel.Visibility = onlyCompat ? Visibility.Collapsed : Visibility.Visible;

        var resSource = onlyCompat
            ? _allGalleryItems.Where(x => ThemeLibraryScanner.EvaluateCompatibility(x.Width, x.Height, target!.EffectiveWidth, target.EffectiveHeight) != ThemeCompatibility.Incompatible)
            : _allGalleryItems;

        _isRefreshingUi = true;
        try
        {
            PopulateResolutionCombo(
                CmbGalleryResolution,
                resSource.Select(x => (x.Width, x.Height)),
                target);
        }
        finally
        {
            _isRefreshingUi = false;
        }

        string resKey = (CmbGalleryResolution.SelectedItem as ResolutionFilterItem)?.Key ?? "ALL";

        foreach (var item in _allGalleryItems)
        {
            var compat = target is null
                ? ThemeCompatibility.Incompatible
                : ThemeLibraryScanner.EvaluateCompatibility(item.Width, item.Height, target.EffectiveWidth, target.EffectiveHeight);

            if (target is null)
            {
                item.CompatBadgeVisibility = Visibility.Collapsed;
                item.CardOpacity = 1.0;
                item.CanClick = false;
                item.ActionText = LocalizationService.T("Str_ApplyOnScreen");
                item.ActionToolTip = LocalizationService.T("Str_NoScreenBanner");
            }
            else if (compat == ThemeCompatibility.Exact)
            {
                item.CompatBadgeText = LocalizationService.T("Str_BadgeExact");
                item.CompatBadgeBrush = BrushExact;
                item.CompatBadgeVisibility = Visibility.Visible;
                item.CardOpacity = 1.0;
                item.CanClick = true;
                item.ActionText = LocalizationService.Format("Str_ApplyToScreenFmt", target.Index);
                item.ActionToolTip = null;
            }
            else if (compat == ThemeCompatibility.Proportional)
            {
                item.CompatBadgeText = LocalizationService.T("Str_BadgeAdaptable");
                item.CompatBadgeBrush = BrushHorizontal;
                item.CompatBadgeVisibility = Visibility.Visible;
                item.CardOpacity = 1.0;
                item.CanClick = true;
                item.ActionText = LocalizationService.Format("Str_ApplyToScreenFmt", target.Index);
                item.ActionToolTip = null;
            }
            else
            {
                item.CompatBadgeText = LocalizationService.T("Str_BadgeOtherOri");
                item.CompatBadgeBrush = BrushIncompat;
                item.CompatBadgeVisibility = Visibility.Visible;
                item.CardOpacity = 0.78;
                item.CanClick = true;
                item.ActionText = LocalizationService.Format("Str_ApplyToScreenFmt", target.Index);
                string themeRes = item.Width > 0 && item.Height > 0 ? $"{item.Width}×{item.Height}" : "Auto";
                item.ActionToolTip = LocalizationService.Format(
                    "Str_IncompatTooltipFmt",
                    FormatOrientationName(item.Orientation),
                    themeRes,
                    $"{target.HardwareClass} {target.Index}",
                    $"{target.EffectiveWidth}×{target.EffectiveHeight}");
            }
        }

        IEnumerable<GalleryItemVm> seq = _allGalleryItems;

        if (onlyCompat)
        {
            seq = seq.Where(x => ThemeLibraryScanner.EvaluateCompatibility(x.Width, x.Height, target!.EffectiveWidth, target.EffectiveHeight) != ThemeCompatibility.Incompatible);
        }
        else if (FilterVertical.IsChecked == true)
            seq = seq.Where(x => x.Orientation == ThemeOrientation.Vertical);
        else if (FilterHorizontal.IsChecked == true)
            seq = seq.Where(x => x.Orientation == ThemeOrientation.Horizontal);
        else if (FilterSquare.IsChecked == true)
            seq = seq.Where(x => x.Orientation == ThemeOrientation.Square);

        if (resKey != "ALL")
            seq = seq.Where(x => $"{x.Width}x{x.Height}" == resKey);

        if (!string.IsNullOrEmpty(q))
        {
            seq = seq.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                 i.MetaText.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (target is not null)
        {
            seq = seq
                .OrderByDescending(x => (int)ThemeLibraryScanner.EvaluateCompatibility(x.Width, x.Height, target.EffectiveWidth, target.EffectiveHeight))
                .ThenBy(x => AspectDelta(x.Width, x.Height, target))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }

        var list = seq.ToList();
        GalleryList.ItemsSource = list;
        GalleryEmptyPanel.Visibility = list.Count == 0 && onlyCompat && string.IsNullOrEmpty(q)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshUi()
    {
        _isRefreshingUi = true;
        try
        {
            var snap = _host.CurrentSensors;
            TxtQuickTelemetry.Text = $"CPU {snap.CpuTemp:F0}°C ({snap.CpuUsage:F0}%) | GPU {snap.GpuTemp:F0}°C ({snap.GpuUsage:F0}%) | RAM {snap.MemoryUsagePct:F0}%";
            TxtHeaderStatus.Text = _host.IsFullscreenPaused
                ? LocalizationService.T("Str_GameModePaused")
                : LocalizationService.T("Str_EngineActive");

            TxtTelCpuTemp.Text = $"{snap.CpuTemp:F1} °C";
            TxtTelCpuSub.Text = LocalizationService.Format("Str_UsagePowerFmt", snap.CpuUsage, snap.CpuPowerW);
            TxtTelGpuTemp.Text = $"{snap.GpuTemp:F1} °C";
            TxtTelGpuSub.Text = LocalizationService.Format("Str_UsagePowerFmt", snap.GpuUsage, snap.GpuPowerW);
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

                string shortOri = FormatOrientationName(effOri);
                bool isAio = s.SerialNumber.StartsWith("VMAXA", StringComparison.OrdinalIgnoreCase) ||
                             Math.Max(s.Width, s.Height) <= 480;
                string hwClass = isAio
                    ? LocalizationService.T("Str_ScreenClassAio")
                    : LocalizationService.T("Str_ScreenClassPanel");

                string inlineScreen = LocalizationService.T("Str_ScreenTitleInline");
                string displayLabel = swapped
                    ? $"{inlineScreen} {idx} ({hwClass}) · {effW}×{effH} {shortOri}"
                    : $"{inlineScreen} {idx} ({hwClass}) · {s.Width}×{s.Height} {shortOri}";

                targetItems.Add(new ScreenTargetItem(
                    idx, s.SerialNumber, hwClass, s.Width, s.Height, effW, effH, effOri,
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

                vm.ScreenTitle = $"{LocalizationService.T("Str_ScreenPrefix")} {idx}";
                vm.ResolutionText = $"{effW} × {effH} px";
                vm.OrientationLabel = oriLabel;
                vm.OrientationBrush = oriBrush;
                string calSuffix = s.HardwareRotationOffset != 0
                    ? $" · auto-cal {s.HardwareRotationOffset * 90}°"
                    : string.Empty;
                vm.HardwareSubline = swapped
                    ? $"{LocalizationService.T("Str_NativePanel")} {s.Width}×{s.Height} ({LocalizationService.T("Str_Rotated")} {rot * 90}°){calSuffix} · SN: {s.SerialNumber}"
                    : $"{LocalizationService.T("Str_NativePanel")} {s.Width}×{s.Height}{calSuffix} · SN: {s.SerialNumber}";
                vm.ThemeSummary = $"{LocalizationService.T("Str_ActiveTheme")}: {s.ThemeName}";
                vm.StatsText = LocalizationService.Format("Str_StatsFmt", s.FramesSent, s.KeepAlivesSent, s.BusyDrops, s.SendErrors);
                vm.Brightness = s.Brightness;
                vm.Rotation = rot;
                vm.ScaleModeIndex = s.ScaleMode switch
                {
                    ThemeScaleMode.Fit => 1,
                    ThemeScaleMode.Stretch => 2,
                    _ => 0
                };
                vm.IsEnabled = s.IsEnabled;
                vm.PreviewImage = preview;

                idx++;
            }

            string signature = string.Join("|", targetItems.Select(x => $"{x.SerialNumber}:{x.DisplayLabel}"));
            if (!string.Equals(_lastTargetSignature, signature, StringComparison.Ordinal))
            {
                _lastTargetSignature = signature;
                _screenTargets.Clear();
                _screenTargets.AddRange(targetItems);

                RebuildScreenPills();
                UpdateGalleryFilterCounts();
                UpdateCloudFilterCounts();
                if (!_isInitializing)
                {
                    ApplyGalleryFilter();
                    if (_cloudLoadedOnce) ApplyCloudFilter();
                }
            }

            TxtNoScreens.Visibility = statuses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateWeatherLocationStatus();
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

        if (NavGallery.IsChecked == true && _thumbnailsPurged)
        {
            _thumbnailsPurged = false;
            RestartGalleryThumbnails();
        }

        if (NavCloud.IsChecked == true)
        {
            if (!_cloudLoadedOnce)
            {
                _ = LoadCloudCatalogAsync();
            }
            else if (_thumbnailsPurged && _allCloudItems.Count > 0)
            {
                _thumbnailsPurged = false;
                RestartCloudThumbnails();
            }
        }
    }

    private void OnElevateClick(object sender, RoutedEventArgs e)
    {
        ((App)System.Windows.Application.Current).RestartElevated();
    }

    private void OnOpenThemeEditorClick(object sender, RoutedEventArgs e)
    {
        var editor = new ThemeEditorWindow(_host, _selectedTargetSerial) { Owner = this };
        editor.ShowDialog();
        LoadGallery();
        RefreshUi();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        Hide();
        TrimVisualMemoryOnHide();
    }
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

    private void OnScaleModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isRefreshingUi) return;
        if (sender is System.Windows.Controls.ComboBox cmb && cmb.Tag is string sn && cmb.SelectedIndex >= 0)
        {
            var mode = cmb.SelectedIndex switch
            {
                1 => ThemeScaleMode.Fit,
                2 => ThemeScaleMode.Stretch,
                _ => ThemeScaleMode.Auto
            };
            _host.SetScaleMode(sn, mode);
        }
    }

    private void OnOpenCloudForScreenClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string sn) return;
        SelectTargetScreen(sn);
        ChkCloudOnlyCompatible.IsChecked = true;
        NavCloud.IsChecked = true;
    }

    private void OnOpenGalleryForScreenClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string sn) return;
        SelectTargetScreen(sn);
        ChkGalleryOnlyCompatible.IsChecked = true;
        NavGallery.IsChecked = true;
    }

    private void OnEditThemeForScreenClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string sn) return;
        SelectTargetScreen(sn);
        var editor = new ThemeEditorWindow(_host, sn) { Owner = this };
        var status = _host.GetStatuses().FirstOrDefault(s => s.SerialNumber.Equals(sn, StringComparison.OrdinalIgnoreCase));
        if (status is not null &&
            !string.IsNullOrWhiteSpace(status.ThemePath) &&
            (File.Exists(status.ThemePath) || Directory.Exists(status.ThemePath)))
        {
            try { editor.LoadThemeFromFile(status.ThemePath); } catch { }
        }
        editor.ShowDialog();
        RefreshUi();
        LoadGallery();
    }

    private void OnGoToCloudStoreClick(object sender, RoutedEventArgs e)
    {
        ChkCloudOnlyCompatible.IsChecked = true;
        NavCloud.IsChecked = true;
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

            var screenTarget = _screenTargets.FirstOrDefault(x => x.SerialNumber.Equals(sn, StringComparison.OrdinalIgnoreCase));
            if (screenTarget is not null)
            {
                var (tw, th, tori) = ThemeLibraryScanner.InspectThemePath(target);
                if (tw > 0 && th > 0 &&
                    ThemeLibraryScanner.EvaluateCompatibility(tw, th, screenTarget.EffectiveWidth, screenTarget.EffectiveHeight) == ThemeCompatibility.Incompatible)
                {
                    string msg = LocalizationService.Format(
                        "Str_ManualIncompatMsgFmt",
                        FormatOrientationName(tori),
                        $"{tw}×{th}",
                        $"{screenTarget.HardwareClass} {screenTarget.Index}",
                        $"{screenTarget.EffectiveWidth}×{screenTarget.EffectiveHeight}");
                    var confirm = System.Windows.MessageBox.Show(
                        msg,
                        LocalizationService.T("Str_ManualIncompatTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    if (confirm != MessageBoxResult.Yes)
                        return;
                }
            }

            _host.ApplyTheme(sn, target);
            RefreshUi();
        }
    }

    private void OnGallerySearchChanged(object sender, TextChangedEventArgs e) => ApplyGalleryFilter();
    private void OnGalleryOnlyCompatibleChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing && !_isRefreshingUi) ApplyGalleryFilter();
    }
    private void OnOrientationFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing) ApplyGalleryFilter();
    }

    private void OnGalleryResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && !_isRefreshingUi) ApplyGalleryFilter();
    }

    private void OnRescanGalleryClick(object sender, RoutedEventArgs e) => LoadGallery();

    private void OnApplyGalleryThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn &&
            btn.Tag is GalleryItemVm item &&
            GetSelectedTargetScreen() is { } target)
        {
            if (ThemeLibraryScanner.EvaluateCompatibility(item.Width, item.Height, target.EffectiveWidth, target.EffectiveHeight) == ThemeCompatibility.Incompatible)
            {
                string msg = LocalizationService.Format(
                    "Str_CrossOriConfirmMsgFmt",
                    FormatOrientationName(item.Orientation),
                    item.Width > 0 && item.Height > 0 ? $"{item.Width}×{item.Height}" : "Auto",
                    $"{target.HardwareClass} {target.Index}",
                    FormatOrientationName(target.EffectiveOrientation),
                    $"{target.EffectiveWidth}×{target.EffectiveHeight}");
                var confirm = System.Windows.MessageBox.Show(
                    msg,
                    LocalizationService.T("Str_ManualIncompatTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);
                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            _host.ApplyTheme(target.SerialNumber, item.FullPath, item.SchemeJsonPath);
            NavScreens.IsChecked = true;
            RefreshUi();
        }
    }

    private void OnTargetFpsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        int fps = (int)Math.Round(e.NewValue);
        TxtFpsLabel.Text = LocalizationService.Format("Str_FpsLabelFmt", fps);
        _host.SetTargetFps(fps);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isRefreshingUi) return;
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string code)
        {
            _host.Config.Language = code;
            _host.SaveConfig();
            LocalizationService.ApplyLanguage(code);

            TxtFpsLabel.Text = LocalizationService.Format("Str_FpsLabelFmt", _host.Config.TargetFps);
            TxtNightLevelLabel.Text = LocalizationService.Format("Str_NightLevelFmt", Math.Clamp(_host.Config.NightBrightnessPct, 5, 100));
            TxtLyricsOffsetLabel.Text = LocalizationService.Format("Str_LyricsOffsetFmt", _host.Config.LyricsOffsetMs);
            UpdateWeatherLocationStatus();
            _lastTargetSignature = string.Empty;
            RefreshUi();
            LoadGallery();
            if (_cloudLoadedOnce)
                _ = LoadCloudCatalogAsync();
        }
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

    private void OnNightScheduleChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _host.Config.NightBrightnessEnabled = ChkNightBrightness.IsChecked == true;
        _host.SaveConfig();
        _host.ApplyNightBrightnessScheduleNow();
    }

    private void OnNightScheduleComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CmbNightStart.SelectedIndex >= 0) _host.Config.NightStartHour = CmbNightStart.SelectedIndex;
        if (CmbNightEnd.SelectedIndex >= 0) _host.Config.NightEndHour = CmbNightEnd.SelectedIndex;
        _host.SaveConfig();
        _host.ApplyNightBrightnessScheduleNow();
    }

    private void OnNightBrightnessSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int pct = Math.Clamp((int)Math.Round(e.NewValue), 5, 100);
        if (TxtNightLevelLabel != null)
            TxtNightLevelLabel.Text = LocalizationService.Format("Str_NightLevelFmt", pct);
        if (_isInitializing) return;
        _host.Config.NightBrightnessPct = pct;
        _host.SaveConfig();
        _host.ApplyNightBrightnessScheduleNow();
    }

    private void OnLyricsOffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int ms = Math.Clamp((int)Math.Round(e.NewValue), -5000, 5000);
        if (TxtLyricsOffsetLabel != null)
            TxtLyricsOffsetLabel.Text = LocalizationService.Format("Str_LyricsOffsetFmt", ms);
        if (_isInitializing) return;
        _host.SetLyricsOffsetMs(ms);
    }

    private void OnHttpRemoteToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool enabled = ChkHttpRemote.IsChecked == true;
        bool ok = _host.SetHttpRemoteEnabled(enabled);
        BtnOpenHttpRemote.IsEnabled = enabled && ok;
    }

    private void OnOpenHttpRemoteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_host.HttpRemoteUrl) { UseShellExecute = true });
        }
        catch { }
    }

    private void UpdateWeatherLocationStatus()
    {
        if (TxtWeatherLocStatus == null) return;
        var snap = _host.CurrentSensors;
        string tempSuffix = snap.HasWeather ? $" · {Math.Round(snap.WeatherTempC):0}°C" : string.Empty;

        if (_host.Config.WeatherLatitude.HasValue && _host.Config.WeatherLongitude.HasValue)
        {
            string city = string.IsNullOrWhiteSpace(_host.Config.WeatherCity)
                ? LocalizationService.T("Str_WeatherNotDetected")
                : _host.Config.WeatherCity;
            TxtWeatherLocStatus.Text = LocalizationService.Format(
                "Str_WeatherManualModeFmt",
                city,
                _host.Config.WeatherLatitude.Value,
                _host.Config.WeatherLongitude.Value) + tempSuffix;
        }
        else
        {
            string detected = !string.IsNullOrWhiteSpace(snap.WeatherCity)
                ? snap.WeatherCity
                : LocalizationService.T("Str_WeatherNotDetected");
            TxtWeatherLocStatus.Text = LocalizationService.Format("Str_WeatherAutoModeFmt", detected) + tempSuffix;
        }
    }

    private void OnWeatherSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnWeatherSearchClick(sender, e);
        }
    }

    private async void OnWeatherSearchClick(object sender, RoutedEventArgs e)
    {
        string query = (TxtWeatherSearch.Text ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            CmbWeatherResults.Visibility = Visibility.Collapsed;
            UpdateWeatherLocationStatus();
            return;
        }

        BtnWeatherSearch.IsEnabled = false;
        TxtWeatherLocStatus.Text = LocalizationService.T("Str_WeatherSearching");
        try
        {
            string lang = string.IsNullOrWhiteSpace(_host.Config.Language) ? "es" : _host.Config.Language;
            var results = await WeatherAndNetProvider.SearchCitiesAsync(query, lang);

            _suppressWeatherCombo = true;
            try
            {
                CmbWeatherResults.ItemsSource = results;
                CmbWeatherResults.SelectedIndex = -1;
            }
            finally
            {
                _suppressWeatherCombo = false;
            }

            if (results.Count == 0)
            {
                CmbWeatherResults.Visibility = Visibility.Collapsed;
                TxtWeatherLocStatus.Text = LocalizationService.T("Str_WeatherNoResults");
            }
            else
            {
                CmbWeatherResults.Visibility = Visibility.Visible;
                CmbWeatherResults.IsDropDownOpen = true;
                UpdateWeatherLocationStatus();
            }
        }
        finally
        {
            BtnWeatherSearch.IsEnabled = true;
        }
    }

    private void OnWeatherResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWeatherCombo) return;
        if (CmbWeatherResults.SelectedItem is not WeatherLocationCandidate candidate) return;

        _host.SetWeatherLocation(candidate.DisplayName, candidate.Latitude, candidate.Longitude);
        TxtWeatherSearch.Text = candidate.DisplayName;
        CmbWeatherResults.Visibility = Visibility.Collapsed;
        UpdateWeatherLocationStatus();
    }

    private void OnWeatherAutoClick(object sender, RoutedEventArgs e)
    {
        _suppressWeatherCombo = true;
        try
        {
            CmbWeatherResults.ItemsSource = null;
            CmbWeatherResults.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _suppressWeatherCombo = false;
        }

        TxtWeatherSearch.Text = string.Empty;
        _host.SetWeatherLocation(null, null, null);
        UpdateWeatherLocationStatus();
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

    private static string GetCurrentAppVersion()
    {
        var v = typeof(MainWindow).Assembly.GetName().Version;
        return v is null ? "1.1.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        string currentVer = GetCurrentAppVersion();
        BtnCheckUpdates.IsEnabled = false;
        TxtUpdateStatus.Foreground = BrushHorizontal;
        TxtUpdateStatus.Text = "Comprobando actualizaciones en GitHub...";

        try
        {
            var result = await UpdateChecker.CheckLatestAsync(currentVer);
            if (result is null)
            {
                TxtUpdateStatus.Foreground = BrushUnknown;
                TxtUpdateStatus.Text = $"⚠️ No se pudo comprobar actualizaciones (actual: v{currentVer}).";
                return;
            }

            if (!result.UpdateAvailable)
            {
                TxtUpdateStatus.Foreground = BrushExact;
                TxtUpdateStatus.Text = $"✅ Estás al día — instalada v{currentVer} (última publicada: {result.LatestTag}).";
                return;
            }

            TxtUpdateStatus.Foreground = BrushHorizontal;
            TxtUpdateStatus.Text = $"✨ Nueva versión disponible: {result.LatestTag} (actual: v{currentVer}).";

            var answer = System.Windows.MessageBox.Show(
                $"Hay una nueva versión disponible: {result.LatestTag} (instalada: v{currentVer}).\n\n¿Deseas descargar e iniciar el instalador?",
                "Actualización disponible — FoxuMarsDisplay",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes)
                return;

            if (!string.IsNullOrWhiteSpace(result.InstallerDownloadUrl))
            {
                TxtUpdateStatus.Text = $"Descargando {result.LatestTag}...";
                string safeTag = string.Concat(result.LatestTag.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'));
                string dlPath = Path.Combine(Path.GetTempPath(), $"FoxuMarsDisplay_Setup_{safeTag}.exe");
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("FoxuMarsDisplay-Updater/1.0");
                byte[] bytes = await http.GetByteArrayAsync(result.InstallerDownloadUrl);
                await File.WriteAllBytesAsync(dlPath, bytes);

                if (!string.IsNullOrWhiteSpace(result.ExpectedSha256) &&
                    !UpdateChecker.VerifyFileSha256(dlPath, result.ExpectedSha256))
                {
                    try { File.Delete(dlPath); } catch { }
                    TxtUpdateStatus.Foreground = BrushIncompat;
                    TxtUpdateStatus.Text = "⛔ Error de verificación SHA-256 en el instalador descargado.";
                    return;
                }

                Process.Start(new ProcessStartInfo(dlPath) { UseShellExecute = true });
            }
            else if (!string.IsNullOrWhiteSpace(result.ReleaseUrl))
            {
                Process.Start(new ProcessStartInfo(result.ReleaseUrl) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Foreground = BrushIncompat;
            TxtUpdateStatus.Text = $"⚠️ Error comprobando actualización: {ex.Message}";
        }
        finally
        {
            BtnCheckUpdates.IsEnabled = true;
        }
    }

    private CloudSourceMode GetSelectedCloudMode() => CmbCloudSource.SelectedIndex switch
    {
        1 => CloudSourceMode.AutoSoeyiWithHfFallback,
        2 => CloudSourceMode.HuggingFaceOnly,
        _ => CloudSourceMode.Both
    };

    private async Task LoadCloudCatalogAsync()
    {
        _cloudLoadedOnce = true;
        _cloudCts?.Cancel();
        _cloudCts?.Dispose();
        _cloudCts = new CancellationTokenSource();
        var ct = _cloudCts.Token;

        TxtCloudStatus.Text = LocalizationService.T("Str_LoadingCatalog");
        var result = await _cloudService.FetchCatalogAsync(GetSelectedCloudMode(), ct);
        if (ct.IsCancellationRequested) return;

        _allCloudItems.Clear();
        string liveTag = LocalizationService.T("Str_MetaLiveTag");
        string artTag = LocalizationService.T("Str_MetaArtTag");
        foreach (var item in result.Items)
        {
            var (badge, _, brush) = GetOrientationMeta(item.Orientation);
            string resLabel = item.Width > 0 && item.Height > 0 ? $"{item.Width}×{item.Height}" : item.Resolution;
            string kindTag = item.HasLiveSensors ? liveTag : artTag;
            _allCloudItems.Add(new CloudItemVm
            {
                Model = item,
                OrientationBadge = badge,
                OrientationBrush = brush,
                MetaText = $"{resLabel} • {item.Format} • {kindTag}",
                ActionText = item.IsDownloaded ? LocalizationService.T("Str_ApplyNow") : LocalizationService.T("Str_DownloadAndApply")
            });
        }

        UpdateCloudFilterCounts();
        TxtCloudStatus.Text = LocalizationService.Format("Str_ThemesCountFmt", result.ActiveSourceLabel, _allCloudItems.Count);
        ApplyCloudFilter();
    }

    private void UpdateCloudFilterCounts()
    {
        int total = _allCloudItems.Count;
        int vert = _allCloudItems.Count(x => x.Model.Orientation == ThemeOrientation.Vertical);
        int horiz = _allCloudItems.Count(x => x.Model.Orientation == ThemeOrientation.Horizontal);
        int sq = _allCloudItems.Count(x => x.Model.Orientation == ThemeOrientation.Square);

        var target = GetSelectedTargetScreen();
        int compat = target is null ? 0 : _allCloudItems.Count(x =>
            ThemeLibraryScanner.EvaluateCompatibility(x.Model.Width, x.Model.Height, target.EffectiveWidth, target.EffectiveHeight) != ThemeCompatibility.Incompatible);

        ChkCloudOnlyCompatible.Content = LocalizationService.Format("Str_OnlyCompatibleFmt", compat);
        CloudFilterAll.Content = LocalizationService.Format("Str_FilterAllFmt", total);
        CloudFilterVertical.Content = LocalizationService.Format("Str_FilterVertFmt", vert);
        CloudFilterHorizontal.Content = LocalizationService.Format("Str_FilterHorizFmt", horiz);
        CloudFilterSquare.Content = LocalizationService.Format("Str_FilterSquareFmt", sq);
    }

    private async Task LoadCloudThumbnailsAsync(List<CloudItemVm> items, CancellationToken ct)
    {
        using var sem = new SemaphoreSlim(6);
        var tasks = items.Where(vm => vm.ThumbnailImage is null).Select(async vm =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (ct.IsCancellationRequested || vm.ThumbnailImage is not null) return;
                string? localThumb = await _cloudService.GetOrDownloadThumbnailAsync(vm.Model, ct).ConfigureAwait(false);
                if (localThumb is null || ct.IsCancellationRequested) return;

                vm.CachedThumbPath = localThumb;
                BitmapImage? bmp = DecodeBitmapWithSkiaFallback(localThumb, 162);
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

    private static BitmapImage? DecodeBitmapWithSkiaFallback(string path, int decodeHeight = 162)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            if (decodeHeight > 0)
                bmp.DecodePixelHeight = decodeHeight;
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
                if (decodeHeight > 0)
                    bmp.DecodePixelHeight = decodeHeight;
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
        TxtCloudSearchWatermark.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;

        var target = GetSelectedTargetScreen();
        bool onlyCompat = ChkCloudOnlyCompatible.IsChecked == true && target is not null;
        CloudExtraOrientPanel.Visibility = onlyCompat ? Visibility.Collapsed : Visibility.Visible;

        var resSource = onlyCompat
            ? _allCloudItems.Where(x => ThemeLibraryScanner.EvaluateCompatibility(x.Model.Width, x.Model.Height, target!.EffectiveWidth, target.EffectiveHeight) != ThemeCompatibility.Incompatible)
            : _allCloudItems;

        _isRefreshingUi = true;
        try
        {
            PopulateResolutionCombo(
                CmbCloudResolution,
                resSource.Select(x => (x.Model.Width, x.Model.Height)),
                target);
        }
        finally
        {
            _isRefreshingUi = false;
        }

        string resKey = (CmbCloudResolution.SelectedItem as ResolutionFilterItem)?.Key ?? "ALL";

        foreach (var item in _allCloudItems)
        {
            bool isDownloaded = item.Model.IsDownloaded || File.Exists(item.Model.LocalFilePath);
            var compat = target is null
                ? ThemeCompatibility.Incompatible
                : ThemeLibraryScanner.EvaluateCompatibility(item.Model.Width, item.Model.Height, target.EffectiveWidth, target.EffectiveHeight);

            if (target is null)
            {
                item.CompatBadgeVisibility = Visibility.Collapsed;
                item.CardOpacity = 1.0;
                item.CanClick = !isDownloaded;
                item.ActionText = isDownloaded ? LocalizationService.T("Str_DownloadedIncompat") : LocalizationService.T("Str_DownloadOnly");
                item.ActionToolTip = LocalizationService.T("Str_NoScreenBanner");
            }
            else if (compat == ThemeCompatibility.Exact)
            {
                item.CompatBadgeText = LocalizationService.T("Str_BadgeExact");
                item.CompatBadgeBrush = BrushExact;
                item.CompatBadgeVisibility = Visibility.Visible;
                item.CardOpacity = 1.0;
                item.CanClick = true;
                item.ActionText = isDownloaded
                    ? LocalizationService.Format("Str_ApplyToScreenFmt", target.Index)
                    : LocalizationService.T("Str_DownloadAndApply");
                item.ActionToolTip = null;
            }
            else if (compat == ThemeCompatibility.Proportional)
            {
                item.CompatBadgeText = LocalizationService.T("Str_BadgeAdaptable");
                item.CompatBadgeBrush = BrushHorizontal;
                item.CompatBadgeVisibility = Visibility.Visible;
                item.CardOpacity = 1.0;
                item.CanClick = true;
                item.ActionText = isDownloaded
                    ? LocalizationService.Format("Str_ApplyToScreenFmt", target.Index)
                    : LocalizationService.T("Str_DownloadAndApply");
                item.ActionToolTip = null;
            }
            else
            {
                item.CompatBadgeText = LocalizationService.T("Str_BadgeOtherOri");
                item.CompatBadgeBrush = BrushIncompat;
                item.CompatBadgeVisibility = Visibility.Visible;
                item.CardOpacity = 0.78;
                item.CanClick = true;
                item.ActionText = isDownloaded
                    ? LocalizationService.Format("Str_ApplyToScreenFmt", target.Index)
                    : LocalizationService.T("Str_DownloadAndApply");
                string themeRes = item.Model.Width > 0 && item.Model.Height > 0 ? $"{item.Model.Width}×{item.Model.Height}" : "Auto";
                item.ActionToolTip = LocalizationService.Format(
                    "Str_IncompatTooltipFmt",
                    FormatOrientationName(item.Model.Orientation),
                    themeRes,
                    $"{target.HardwareClass} {target.Index}",
                    $"{target.EffectiveWidth}×{target.EffectiveHeight}");
            }
        }

        IEnumerable<CloudItemVm> seq = _allCloudItems;
        if (onlyCompat)
        {
            seq = seq.Where(x => ThemeLibraryScanner.EvaluateCompatibility(x.Model.Width, x.Model.Height, target!.EffectiveWidth, target.EffectiveHeight) != ThemeCompatibility.Incompatible);
        }
        else if (CloudFilterVertical.IsChecked == true)
            seq = seq.Where(x => x.Model.Orientation == ThemeOrientation.Vertical);
        else if (CloudFilterHorizontal.IsChecked == true)
            seq = seq.Where(x => x.Model.Orientation == ThemeOrientation.Horizontal);
        else if (CloudFilterSquare.IsChecked == true)
            seq = seq.Where(x => x.Model.Orientation == ThemeOrientation.Square);

        if (resKey != "ALL")
            seq = seq.Where(x => $"{x.Model.Width}x{x.Model.Height}" == resKey);

        if (!string.IsNullOrEmpty(q))
            seq = seq.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.MetaText.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Source.Contains(q, StringComparison.OrdinalIgnoreCase));

        if (target is not null)
        {
            seq = seq
                .OrderByDescending(x => (int)ThemeLibraryScanner.EvaluateCompatibility(x.Model.Width, x.Model.Height, target.EffectiveWidth, target.EffectiveHeight))
                .ThenByDescending(x => x.Model.HasLiveSensors)
                .ThenBy(x => AspectDelta(x.Model.Width, x.Model.Height, target))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }

        var visible = seq.ToList();
        CloudList.ItemsSource = visible;

        if (_allCloudItems.Count > 0)
        {
            _cloudThumbCts?.Cancel();
            _cloudThumbCts?.Dispose();
            _cloudThumbCts = CancellationTokenSource.CreateLinkedTokenSource(_cloudCts?.Token ?? CancellationToken.None);

            var visibleSet = visible.ToHashSet();
            var prioritized = visible.Concat(_allCloudItems.Where(x => !visibleSet.Contains(x))).ToList();
            _ = LoadCloudThumbnailsAsync(prioritized, _cloudThumbCts.Token);
        }
    }

    private void OnCloudSearchChanged(object sender, TextChangedEventArgs e) => ApplyCloudFilter();
    private void OnCloudOnlyCompatibleChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing && !_isRefreshingUi) ApplyCloudFilter();
    }
    private void OnCloudSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && NavCloud.IsChecked == true)
            _ = LoadCloudCatalogAsync();
    }
    private void OnCloudFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing) ApplyCloudFilter();
    }
    private void OnCloudResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && !_isRefreshingUi) ApplyCloudFilter();
    }
    private void OnReloadCloudClick(object sender, RoutedEventArgs e) => _ = LoadCloudCatalogAsync();

    private async void OnDownloadAndApplyCloudClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not CloudItemVm vm)
            return;

        var target = GetSelectedTargetScreen();
        if (target is null)
        {
            System.Windows.MessageBox.Show(LocalizationService.T("Str_NoScreenBanner"), "FoxuMarsDisplay", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ThemeLibraryScanner.EvaluateCompatibility(vm.Model.Width, vm.Model.Height, target.EffectiveWidth, target.EffectiveHeight) == ThemeCompatibility.Incompatible)
        {
            string msg = LocalizationService.Format(
                "Str_CrossOriConfirmMsgFmt",
                FormatOrientationName(vm.Model.Orientation),
                vm.Model.Width > 0 && vm.Model.Height > 0 ? $"{vm.Model.Width}×{vm.Model.Height}" : "Auto",
                $"{target.HardwareClass} {target.Index}",
                FormatOrientationName(target.EffectiveOrientation),
                $"{target.EffectiveWidth}×{target.EffectiveHeight}");
            var confirm = System.Windows.MessageBox.Show(
                msg,
                LocalizationService.T("Str_ManualIncompatTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (confirm != MessageBoxResult.Yes)
                return;
        }

        vm.CanClick = false;
        try
        {
            string localPath = vm.Model.LocalFilePath;
            if (!File.Exists(localPath))
            {
                var progress = new Progress<int>(pct => vm.ActionText = LocalizationService.Format("Str_DownloadingFmt", pct));
                localPath = await _cloudService.DownloadThemeAsync(vm.Model, progress);
            }

            vm.ActionText = LocalizationService.Format("Str_ApplyToScreenFmt", target.Index);
            _host.ApplyTheme(target.SerialNumber, localPath);
            LoadGallery();
            NavScreens.IsChecked = true;
            RefreshUi();
        }
        catch (Exception ex)
        {
            vm.ActionText = LocalizationService.T("Str_Retry");
            System.Windows.MessageBox.Show($"Error al descargar '{vm.Name}':\n{ex.Message}", "FoxuMarsDisplay", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            vm.CanClick = true;
        }
    }

    private void OnPreviewImageClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;

        ImageSource? fullImg = null;
        string title = "";
        string meta = "";

        if (fe.Tag is GalleryItemVm g)
        {
            title = g.Name;
            meta = g.MetaText;
            if (!string.IsNullOrWhiteSpace(g.ThumbnailPath) && File.Exists(g.ThumbnailPath))
                fullImg = DecodeBitmapWithSkiaFallback(g.ThumbnailPath, 0);
            fullImg ??= g.ThumbnailImage;
        }
        else if (fe.Tag is CloudItemVm c)
        {
            title = c.Name;
            meta = $"{c.Source} · {c.MetaText}";
            if (!string.IsNullOrWhiteSpace(c.CachedThumbPath) && File.Exists(c.CachedThumbPath))
                fullImg = DecodeBitmapWithSkiaFallback(c.CachedThumbPath, 0);
            fullImg ??= c.ThumbnailImage;
        }
        else if (fe.Tag is ScreenItemVm s)
        {
            title = s.ScreenTitle;
            meta = $"{s.ResolutionText} · {s.ThemeSummary}";
            byte[]? png = _host.CapturePreviewPng(s.SerialNumber, 600);
            if (png is not null && png.Length > 0)
            {
                var bmp = new BitmapImage();
                using var ms = new MemoryStream(png);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                fullImg = bmp;
            }
            fullImg ??= s.PreviewImage;
        }

        if (fullImg is null) return;

        TxtLightboxTitle.Text = title;
        TxtLightboxMeta.Text = meta;
        ImgLightbox.Source = fullImg;
        LightboxOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnLightboxClick(object sender, MouseButtonEventArgs e)
    {
        CloseLightbox();
        e.Handled = true;
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && LightboxOverlay.Visibility == Visibility.Visible)
        {
            CloseLightbox();
            e.Handled = true;
        }
    }

    private void CloseLightbox()
    {
        LightboxOverlay.Visibility = Visibility.Collapsed;
        ImgLightbox.Source = null;
    }
}
