using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FoxuMarsDisplay.Core.Host;
using FoxuMarsDisplay.Core.Rendering;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;
using Microsoft.Win32;
using SkiaSharp;

namespace FoxuMarsDisplay;

public partial class ThemeEditorWindow : Window
{
    private sealed class EditableWidget
    {
        public string Kind { get; set; } = "Text";
        public float X { get; set; }
        public float Y { get; set; }
        public int ZIndex { get; set; } = 2;
        public float Width { get; set; } = 160f;
        public float Height { get; set; } = 26f;
        public float FontSize { get; set; } = 22f;
        public string FontFamily { get; set; } = string.Empty;
        public TextAlignMode Align { get; set; } = TextAlignMode.Left;
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsUnderline { get; set; }
        public bool IsStrikeout { get; set; }
        public bool HasShadow { get; set; }
        public string SensorKey { get; set; } = "CPUTemp";
        public string PrefixOrText { get; set; } = string.Empty;
        public string Unit { get; set; } = "°C";
        public SKColor Color { get; set; } = SKColors.White;
        public SKColor BackColor { get; set; } = new(38, 31, 51, 200);
        public bool IsStatic { get; set; }
        public bool ShowText { get; set; } = true;

        public override string ToString() =>
            IsStatic ? $"[{Kind}] \"{PrefixOrText}\" ({X:0},{Y:0})" : $"[{Kind}] {SensorKey} ({X:0},{Y:0})";
    }

    private sealed record ScreenChoice(string SerialNumber, int Width, int Height, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record FontChoice(string FamilyKey, string DisplayName, FontFamily PreviewFont)
    {
        public override string ToString() => DisplayName;
    }

    private static readonly FontFamily UiFallbackFont = new("Segoe UI");

    private readonly DisplayHost? _host;
    private string? _targetSerial;
    private readonly List<EditableWidget> _widgets = [];
    private readonly List<string> _loadedFontFiles = [];
    private readonly List<FontChoice> _fontChoices = [];
    private string? _backgroundPath;
    private SKBitmap? _cachedBgBitmap;
    private string? _cachedBgSourcePath;
    private string? _cachedBgPreviewPath;
    private float _canvasW = 360f;
    private float _canvasH = 960f;
    private bool _suppressUiEvents;
    private bool _isEditingBackColor;
    private bool _isDragging;
    private Point _dragStart;
    private float _origX;
    private float _origY;

    private static readonly string[] PaletteHexColors =
    [
        "#00000000", "#FFFFFFFF", "#FFD8D8E6", "#FF988FAD", "#FF524B66", "#FF261F33", "#FF14111D", "#FF000000",
        "#FFFF1F4B", "#FFFF4757", "#FFFF6B81", "#FFE84393", "#FFB33771", "#FF8B0018", "#FF4A0010", "#FF240008",
        "#FFFF6419", "#FFFF7F50", "#FFFF9F1A", "#FFFFB820", "#FFFFD32A", "#FFCC5500", "#FF803300", "#FF3A1C12",
        "#FF22E58B", "#FF2ED573", "#FF7BED9F", "#FF1ABC9C", "#FF00D2D3", "#FF0A7E5C", "#FF074F3A", "#FF0D281E",
        "#FF00E5FF", "#FF18DCFF", "#FF38BDF8", "#FF1E90FF", "#FF3742FA", "#FF0C4A6E", "#FF102A43", "#FF081426",
        "#FFB57BFF", "#FF9B59B6", "#FF7158E2", "#FF5F27CD", "#FFC8261F", "#B415121F", "#C80D0B14", "#DC0B0A0F"
    ];

    public ThemeEditorWindow(DisplayHost? host = null, string? targetSerial = null)
    {
        _host = host;
        _targetSerial = targetSerial;
        InitializeComponent();
        PopulateFontChoices();
        BuildColorPaletteSwatches();

        // Seed starter widgets so user has an immediate live HUD to customize
        _widgets.Add(new EditableWidget { Kind = "Card", X = 16, Y = 24, ZIndex = 1, Width = 328, Height = 96, Color = new SKColor(22, 20, 32, 200), IsStatic = true });
        _widgets.Add(new EditableWidget { Kind = "Text", X = 32, Y = 40, ZIndex = 3, FontSize = 26, SensorKey = "CPUTemp", PrefixOrText = "CPU ", Unit = "°C", Color = SKColor.Parse("#FFFF6419") });
        _widgets.Add(new EditableWidget { Kind = "Bar", X = 32, Y = 86, ZIndex = 3, Width = 296, Height = 14, SensorKey = "CpuUsage", Color = SKColor.Parse("#FFFF1F4B"), BackColor = SKColor.Parse("#FF261F33") });
        _widgets.Add(new EditableWidget { Kind = "Radial", X = 110, Y = 150, ZIndex = 3, Width = 140, Height = 140, SensorKey = "GpuUsage", Color = SKColor.Parse("#FFFFB820"), BackColor = SKColor.Parse("#FF261F33") });
        _widgets.Add(new EditableWidget { Kind = "AudioFFT", X = 24, Y = 330, ZIndex = 3, Width = 312, Height = 64, Color = SKColors.Cyan, BackColor = SKColors.Magenta });

        RefreshLayerList(0);
        PopulateTargetScreens();
        RenderPreview();
    }

    private void PopulateTargetScreens()
    {
        if (CmbTargetScreen == null) return;
        _suppressUiEvents = true;
        try
        {
            CmbTargetScreen.Items.Clear();
            var screens = _host?.GetStatuses() ?? [];
            if (screens.Count == 0)
            {
                CmbTargetScreen.Items.Add("Sin pantallas conectadas");
                CmbTargetScreen.SelectedIndex = 0;
                CmbTargetScreen.IsEnabled = false;
                return;
            }

            CmbTargetScreen.IsEnabled = true;
            int selectIdx = 0;
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                string label = $"Pantalla {i + 1} ({s.Width}x{s.Height}) — {s.SerialNumber}";
                CmbTargetScreen.Items.Add(new ScreenChoice(s.SerialNumber, s.Width, s.Height, label));
                if (!string.IsNullOrWhiteSpace(_targetSerial) &&
                    s.SerialNumber.Equals(_targetSerial, StringComparison.OrdinalIgnoreCase))
                {
                    selectIdx = i;
                }
            }

            CmbTargetScreen.SelectedIndex = selectIdx;
            if (CmbTargetScreen.SelectedItem is ScreenChoice chosen)
                _targetSerial = chosen.SerialNumber;
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private void OnTargetScreenChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || CmbTargetScreen?.SelectedItem is not ScreenChoice chosen)
            return;

        _targetSerial = chosen.SerialNumber;
        ApplyCanvasResolution(chosen.Width, chosen.Height);
        TxtEditorStatus.Text = $"Destino seleccionado: {chosen.Label}";
    }

    private void ApplyCanvasResolution(float newW, float newH)
    {
        float oldW = _canvasW;
        float oldH = _canvasH;
        _canvasW = Math.Max(64f, newW);
        _canvasH = Math.Max(64f, newH);

        if (ChkScaleWidgets?.IsChecked == true && (MathF.Abs(oldW - _canvasW) > 0.5f || MathF.Abs(oldH - _canvasH) > 0.5f))
        {
            foreach (var w in _widgets)
            {
                bool square = w.Kind is "Radial" or "AlbumArt";
                var scaled = ThemeCanvasScaler.ScaleRect(w.X, w.Y, w.Width, w.Height, w.FontSize, oldW, oldH, _canvasW, _canvasH, square);
                w.X = scaled.X;
                w.Y = scaled.Y;
                w.Width = scaled.Width;
                w.Height = scaled.Height;
                w.FontSize = scaled.FontSize;
            }
        }

        _suppressUiEvents = true;
        try
        {
            string resText = $"{Math.Round(_canvasW)}x{Math.Round(_canvasH)}";
            for (int i = 0; i < CmbResolution.Items.Count; i++)
            {
                if (CmbResolution.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Content?.ToString(), resText, StringComparison.OrdinalIgnoreCase))
                {
                    CmbResolution.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _suppressUiEvents = false;
        }

        ClampWidgetsToCanvas();
        RenderPreview();
    }

    private void ClampWidgetsToCanvas()
    {
        float maxX = Math.Max(0f, _canvasW - 12f);
        float maxY = Math.Max(0f, _canvasH - 12f);
        foreach (var w in _widgets)
        {
            w.X = Math.Clamp(w.X, 0f, maxX);
            w.Y = Math.Clamp(w.Y, 0f, maxY);
        }
        RefreshLayerList(LstLayers.SelectedIndex);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cachedBgBitmap?.Dispose();
        _cachedBgBitmap = null;
        base.OnClosed(e);
    }

    private string? EnsureCachedBackgroundPreviewPath()
    {
        if (string.IsNullOrWhiteSpace(_backgroundPath) || !File.Exists(_backgroundPath))
        {
            _cachedBgBitmap?.Dispose();
            _cachedBgBitmap = null;
            _cachedBgSourcePath = null;
            _cachedBgPreviewPath = null;
            return null;
        }

        string key = $"{_backgroundPath}|{_canvasW:0}x{_canvasH:0}";
        if (string.Equals(_cachedBgSourcePath, key, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(_cachedBgPreviewPath) &&
            File.Exists(_cachedBgPreviewPath))
        {
            return _cachedBgPreviewPath;
        }

        _cachedBgBitmap?.Dispose();
        _cachedBgBitmap = null;

        try
        {
            int w = Math.Max(64, (int)Math.Round(_canvasW));
            int h = Math.Max(64, (int)Math.Round(_canvasH));
            string prevDir = Path.Combine(Path.GetTempPath(), "FoxuStudioPrev");
            Directory.CreateDirectory(prevDir);
            string prevPng = Path.Combine(prevDir, "bg_preview.png");

            if (_backgroundPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                _backgroundPath.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                _backgroundPath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                _backgroundPath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase) ||
                _backgroundPath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
            {
                var bgOnly = new DisplayTheme { Name = "BgPrev", CanvasWidth = w, CanvasHeight = h };
                bgOnly.ImageLayers.Add(new ImageLayerSpec { FilePath = _backgroundPath, X = 0, Y = 0, ZIndex = -10, Width = w, Height = h, IsBackground = true });
                using var r = new SkiaThemeRenderer(bgOnly, w, h, targetFps: 5, customCacheDir: prevDir);
                r.RenderIfDirty(new SensorSnapshot(), 0);
                _cachedBgBitmap = r.OutputBitmap.Copy();
            }
            else
            {
                using var raw = SKBitmap.Decode(_backgroundPath);
                if (raw != null)
                    _cachedBgBitmap = raw.Resize(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.Medium) ?? raw.Copy();
            }

            if (_cachedBgBitmap != null)
            {
                using var img = SKImage.FromBitmap(_cachedBgBitmap);
                using var data = img.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(prevPng, data.ToArray());
                _cachedBgSourcePath = key;
                _cachedBgPreviewPath = prevPng;
                return prevPng;
            }
        }
        catch { }

        return _backgroundPath;
    }

    private DisplayTheme BuildDisplayTheme(bool forLivePreview = false)
    {
        string rawName = string.IsNullOrWhiteSpace(TxtThemeName.Text) ? "FoxuCustomHUD" : TxtThemeName.Text.Trim();
        foreach (char ch in Path.GetInvalidFileNameChars())
            rawName = rawName.Replace(ch, '_');
        string name = string.IsNullOrWhiteSpace(rawName) ? "FoxuCustomHUD" : rawName;
        var theme = new DisplayTheme
        {
            Name = name,
            SourceDirectory = string.IsNullOrEmpty(_backgroundPath) ? string.Empty : (Path.GetDirectoryName(_backgroundPath) ?? string.Empty),
            CanvasWidth = _canvasW,
            CanvasHeight = _canvasH
        };
        foreach (var fontFile in _loadedFontFiles)
            theme.FontFiles.Add(fontFile);

        string? bgForTheme = forLivePreview ? EnsureCachedBackgroundPreviewPath() : _backgroundPath;
        if (!string.IsNullOrWhiteSpace(bgForTheme) && File.Exists(bgForTheme))
        {
            theme.ImageLayers.Add(new ImageLayerSpec
            {
                FilePath = bgForTheme,
                X = 0,
                Y = 0,
                ZIndex = -10,
                Width = _canvasW,
                Height = _canvasH,
                IsBackground = true
            });
        }

        foreach (var w in _widgets)
        {
            switch (w.Kind)
            {
                case "Text":
                    theme.TextWidgets.Add(new TextWidgetSpec
                    {
                        X = w.X, Y = w.Y, ZIndex = w.ZIndex, FontSize = w.FontSize,
                        FontFamily = w.FontFamily,
                        Color = w.Color, SensorKey = w.IsStatic ? string.Empty : w.SensorKey,
                        Prefix = w.IsStatic ? string.Empty : w.PrefixOrText,
                        Unit = w.IsStatic ? string.Empty : w.Unit,
                        IsStaticText = w.IsStatic,
                        StaticContent = w.IsStatic ? w.PrefixOrText : null,
                        Align = w.Align,
                        IsBold = w.IsBold,
                        IsItalic = w.IsItalic,
                        IsUnderline = w.IsUnderline,
                        IsStrikeout = w.IsStrikeout,
                        HasShadow = w.HasShadow,
                        BoxWidth = w.Width,
                        BoxHeight = Math.Max(26f, w.FontSize * 1.3f),
                        Anchor = w.Align switch
                        {
                            TextAlignMode.Center => "ma",
                            TextAlignMode.Right => "ra",
                            _ => "la"
                        }
                    });
                    break;
                case "Card":
                case "Bar":
                    theme.BarWidgets.Add(new BarWidgetSpec
                    {
                        X = w.X, Y = w.Y, ZIndex = w.ZIndex, MaxWidth = w.Width, MaxHeight = w.Height,
                        CornerRadius = 6f, FillColor = w.Color, BackColor = w.BackColor,
                        SensorKey = w.Kind == "Card" ? string.Empty : w.SensorKey,
                        IsStaticFill = w.Kind == "Card"
                    });
                    break;
                case "Radial":
                    float r = Math.Max(12f, Math.Min(w.Width, w.Height) * 0.42f);
                    theme.RadialWidgets.Add(new RadialWidgetSpec
                    {
                        Cx = w.X + w.Width * 0.5f, Cy = w.Y + w.Height * 0.5f, Radius = r,
                        Thickness = 12f, FillColor = w.Color, BackColor = w.BackColor,
                        SensorKey = w.SensorKey, ZIndex = w.ZIndex, Unit = w.Unit, ShowText = w.ShowText
                    });
                    break;
                case "Graph":
                    theme.GraphWidgets.Add(new GraphWidgetSpec
                    {
                        X = w.X, Y = w.Y, Width = w.Width, Height = w.Height,
                        LineColor = w.Color, BackColor = w.BackColor, SensorKey = w.SensorKey, ZIndex = w.ZIndex
                    });
                    break;
                case "AudioFFT":
                    theme.AudioVisualizers.Add(new AudioVisualizerSpec
                    {
                        X = w.X, Y = w.Y, Width = w.Width, Height = w.Height, ZIndex = w.ZIndex,
                        PrimaryColor = w.Color, SecondaryColor = w.BackColor
                    });
                    break;
                case "AlbumArt":
                    theme.AlbumArts.Add(new AlbumArtSpec
                    {
                        X = w.X, Y = w.Y, Width = w.Width, Height = w.Height, ZIndex = w.ZIndex,
                        IsVinylSpin = true
                    });
                    break;
            }
        }

        return theme;
    }

    private void RenderPreview()
    {
        if (ImgPreviewCanvas == null) return;
        try
        {
            var theme = BuildDisplayTheme(forLivePreview: true);
            int w = Math.Max(64, (int)Math.Round(_canvasW));
            int h = Math.Max(64, (int)Math.Round(_canvasH));
            string cacheDir = Path.Combine(Path.GetTempPath(), "FoxuStudioPrev");
            using var renderer = new SkiaThemeRenderer(theme, w, h, targetFps: 10, customCacheDir: cacheDir);
            var snap = new SensorSnapshot
            {
                CpuTemp = 54, CpuUsage = 42, GpuTemp = 61, GpuUsage = 68,
                MemoryUsagePct = 58, MemoryUsedGb = 18.4, DiskCUsagePct = 55,
                NetDownKbps = 1250, PingMs = 14, HasWeather = true, WeatherDescription = "Clear",
                WeatherTempC = 19, WeatherFeelsLikeC = 18, WeatherHumidityPct = 62,
                WeatherHighC = 23, WeatherLowC = 11,
                WeatherCity = !string.IsNullOrWhiteSpace(_host?.CurrentSensors.WeatherCity) ? _host.CurrentSensors.WeatherCity : "Oviedo",
                AudioPeak = 0.7f, SongTitle = "Cyber Synth", Artist = "Foxu",
                AiClaudePct = 85, AiCodexPct = 72, AiGeminiPct = 94, Now = DateTime.Now
            };
            renderer.RenderIfDirty(snap, 250);

            using var previewBmp = renderer.OutputBitmap.Copy();
            if (LstLayers.SelectedIndex >= 0 && LstLayers.SelectedIndex < _widgets.Count)
            {
                var sel = _widgets[LstLayers.SelectedIndex];
                using var canvas = new SKCanvas(previewBmp);
                using var selPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2f,
                    Color = SKColors.Yellow,
                    PathEffect = SKPathEffect.CreateDash([6f, 4f], 0)
                };
                float sw = sel.Kind == "Text" ? Math.Max(80f, sel.Width) : sel.Width;
                float sh = sel.Kind == "Text" ? Math.Max(26f, sel.FontSize * 1.2f) : sel.Height;
                canvas.DrawRect(sel.X - 2, sel.Y - 2, sw + 4, sh + 4, selPaint);
            }

            using var img = SKImage.FromBitmap(previewBmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 95);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(data.ToArray());
            bi.EndInit();
            bi.Freeze();
            ImgPreviewCanvas.Source = bi;
        }
        catch (Exception ex)
        {
            TxtEditorStatus.Text = $"Error en vista previa: {ex.Message}";
        }
    }

    private void RefreshLayerList(int selectIndex = -1)
    {
        _suppressUiEvents = true;
        LstLayers.Items.Clear();
        foreach (var w in _widgets)
            LstLayers.Items.Add(w.ToString());
        if (selectIndex >= 0 && selectIndex < LstLayers.Items.Count)
            LstLayers.SelectedIndex = selectIndex;
        _suppressUiEvents = false;
        LoadSelectedWidgetProps();
    }

    private void LoadSelectedWidgetProps()
    {
        int idx = LstLayers.SelectedIndex;
        if (idx < 0 || idx >= _widgets.Count)
        {
            TxtSelectedKind.Text = "Ningún widget seleccionado";
            return;
        }
        var w = _widgets[idx];
        _suppressUiEvents = true;
        TxtSelectedKind.Text = $"Editando: {w.Kind}";
        TxtPropX.Text = w.X.ToString("0", CultureInfo.InvariantCulture);
        TxtPropY.Text = w.Y.ToString("0", CultureInfo.InvariantCulture);
        TxtPropW.Text = w.Width.ToString("0", CultureInfo.InvariantCulture);
        TxtPropH.Text = (w.Kind == "Text" ? w.FontSize : w.Height).ToString("0", CultureInfo.InvariantCulture);
        SelectSensorItem(w.SensorKey, w.IsStatic);
        TxtPropText.Text = w.PrefixOrText;
        TxtPropUnit.Text = w.Unit;
        TxtPropColor.Text = $"#{w.Color.Alpha:X2}{w.Color.Red:X2}{w.Color.Green:X2}{w.Color.Blue:X2}";
        TxtPropBackColor.Text = $"#{w.BackColor.Alpha:X2}{w.BackColor.Red:X2}{w.BackColor.Green:X2}{w.BackColor.Blue:X2}";
        UpdateColorSwatch(SwatchPropColor, w.Color);
        UpdateColorSwatch(SwatchPropBackColor, w.BackColor);

        bool isText = w.Kind == "Text";
        TxtPropH.IsEnabled = !isText;
        TxtPropH.Text = (isText ? MathF.Round(Math.Max(26f, w.FontSize * 1.3f)) : w.Height).ToString("0", CultureInfo.InvariantCulture);

        PnlTextFormatting.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BtnCenterWidgetCanvas.Visibility = isText ? Visibility.Collapsed : Visibility.Visible;
        if (isText)
        {
            SelectFontItem(w.FontFamily);
            TxtPropFontSize.Text = w.FontSize.ToString("0", CultureInfo.InvariantCulture);
            SelectFontSizePreset(w.FontSize);
            TglBold.IsChecked = w.IsBold;
            TglItalic.IsChecked = w.IsItalic;
            TglUnderline.IsChecked = w.IsUnderline;
            TglStrikeout.IsChecked = w.IsStrikeout;
            TglShadow.IsChecked = w.HasShadow;
            UpdateAlignButtons(w.Align);
        }
        _suppressUiEvents = false;
    }

    private void SelectSensorItem(string sensorKey, bool isStatic = false)
    {
        if (CmbPropSensor == null) return;
        if (isStatic || string.IsNullOrWhiteSpace(sensorKey))
        {
            CmbPropSensor.SelectedIndex = CmbPropSensor.Items.Count > 0 ? 0 : -1;
            return;
        }

        for (int i = 0; i < CmbPropSensor.Items.Count; i++)
        {
            if (CmbPropSensor.Items[i] is ComboBoxItem item)
            {
                string itemKey = item.Tag as string ?? item.Content?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(itemKey) &&
                    string.Equals(itemKey, sensorKey, StringComparison.OrdinalIgnoreCase))
                {
                    CmbPropSensor.SelectedIndex = i;
                    return;
                }
            }
        }

        var custom = new ComboBoxItem { Tag = sensorKey, Content = sensorKey };
        CmbPropSensor.Items.Add(custom);
        CmbPropSensor.SelectedItem = custom;
    }

    private static bool IsSymbolFontName(string name)
    {
        string[] symbolTokens =
        [
            "Wingdings", "Webdings", "Symbol", "Marlett", "MDL2",
            "Fluent Icons", "MT Extra", "Bookshelf Symbol", "HoloLens", "Segoe UI Emoji"
        ];
        foreach (string token in symbolTokens)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static FontFamily TryLoadBundledWpfFont(string fontFilePath)
    {
        try
        {
            if (File.Exists(fontFilePath))
            {
                var fams = System.Windows.Media.Fonts.GetFontFamilies(fontFilePath);
                var first = fams.FirstOrDefault();
                if (first != null) return first;
            }
        }
        catch { }
        return UiFallbackFont;
    }

    private void PopulateFontChoices()
    {
        if (CmbPropFont == null) return;
        string? currentTag = (CmbPropFont.SelectedItem as FontChoice)?.FamilyKey;

        _suppressUiEvents = true;
        try
        {
            _fontChoices.Clear();
            _fontChoices.Add(new FontChoice(string.Empty, "(Predeterminada)", UiFallbackFont));

            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fontFile in _loadedFontFiles)
            {
                string shortName = Path.GetFileNameWithoutExtension(fontFile);
                if (!string.IsNullOrWhiteSpace(shortName) && added.Add(shortName))
                    _fontChoices.Add(new FontChoice(shortName, $"★ {shortName}", TryLoadBundledWpfFont(fontFile)));
            }

            var installed = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in System.Windows.Media.Fonts.SystemFontFamilies)
            {
                if (!string.IsNullOrWhiteSpace(f.Source))
                    installed.TryAdd(f.Source, f);
            }

            string[] preferred =
            [
                "Segoe UI", "Bahnschrift", "Consolas", "Cascadia Code",
                "Arial", "Arial Black", "Calibri", "Cambria", "Courier New",
                "Franklin Gothic Medium", "Georgia", "Impact", "Tahoma",
                "Times New Roman", "Trebuchet MS", "Verdana"
            ];

            foreach (string p in preferred)
            {
                if (installed.TryGetValue(p, out var famObj) && added.Add(p))
                    _fontChoices.Add(new FontChoice(p, p, IsSymbolFontName(p) ? UiFallbackFont : famObj));
            }

            foreach (var kv in installed.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (added.Add(kv.Key))
                    _fontChoices.Add(new FontChoice(kv.Key, kv.Key, IsSymbolFontName(kv.Key) ? UiFallbackFont : kv.Value));
            }

            CmbPropFont.ItemsSource = null;
            CmbPropFont.ItemsSource = _fontChoices;

            if (!string.IsNullOrEmpty(currentTag))
                SelectFontItem(currentTag);
            else
                CmbPropFont.SelectedIndex = 0;
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private void SelectFontItem(string? family)
    {
        if (CmbPropFont == null) return;
        string target = (family ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(target))
        {
            CmbPropFont.SelectedIndex = 0;
            return;
        }

        for (int i = 0; i < _fontChoices.Count; i++)
        {
            if (string.Equals(_fontChoices[i].FamilyKey, target, StringComparison.OrdinalIgnoreCase))
            {
                CmbPropFont.SelectedIndex = i;
                return;
            }
        }

        var custom = new FontChoice(target, target, UiFallbackFont);
        _fontChoices.Add(custom);
        CmbPropFont.ItemsSource = null;
        CmbPropFont.ItemsSource = _fontChoices;
        CmbPropFont.SelectedItem = custom;
    }

    private void UpdateAlignButtons(TextAlignMode align)
    {
        TglAlignLeft.IsChecked = align == TextAlignMode.Left;
        TglAlignCenter.IsChecked = align == TextAlignMode.Center;
        TglAlignRight.IsChecked = align == TextAlignMode.Right;
    }

    private static void UpdateColorSwatch(Border swatch, SKColor color)
    {
        if (swatch == null) return;
        swatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
    }

    private void BuildColorPaletteSwatches()
    {
        if (ColorPaletteGrid == null) return;
        ColorPaletteGrid.Children.Clear();

        foreach (string hex in PaletteHexColors)
        {
            var sk = UnifiedThemeLoader.ParseColor(hex, SKColors.White);
            var btn = new Button
            {
                Width = 26,
                Height = 22,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = hex == "#00000000" ? "Transparente (#00000000)" : hex,
                Background = sk.Alpha == 0
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0x15, 0x22))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(sk.Alpha, sk.Red, sk.Green, sk.Blue)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0x42, 0x66)),
                BorderThickness = new Thickness(1),
                Content = sk.Alpha == 0
                    ? new TextBlock
                    {
                        Text = "∅",
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x64, 0x19)),
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                    : null
            };
            btn.Click += (_, _) => ApplyPickedColor(sk);
            ColorPaletteGrid.Children.Add(btn);
        }
    }

    private void OpenColorPopup(bool isBackColor, ToggleButton anchor)
    {
        if (LstLayers.SelectedIndex < 0 || LstLayers.SelectedIndex >= _widgets.Count)
        {
            anchor.IsChecked = false;
            return;
        }

        _isEditingBackColor = isBackColor;
        var w = _widgets[LstLayers.SelectedIndex];
        SKColor cur = isBackColor ? w.BackColor : w.Color;
        TxtColorPopupTitle.Text = isBackColor ? "Color de Fondo" : "Color Principal";

        _suppressUiEvents = true;
        try
        {
            SldColorR.Value = cur.Red;
            SldColorG.Value = cur.Green;
            SldColorB.Value = cur.Blue;
            SldColorA.Value = cur.Alpha;
            TxtColorR.Text = cur.Red.ToString(CultureInfo.InvariantCulture);
            TxtColorG.Text = cur.Green.ToString(CultureInfo.InvariantCulture);
            TxtColorB.Text = cur.Blue.ToString(CultureInfo.InvariantCulture);
            TxtColorA.Text = cur.Alpha.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressUiEvents = false;
        }

        ColorPickerPopup.PlacementTarget = anchor;
        ColorPickerPopup.IsOpen = true;
    }

    private void ApplyPickedColor(SKColor color)
    {
        string hex = $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        _suppressUiEvents = true;
        try
        {
            if (_isEditingBackColor)
            {
                TxtPropBackColor.Text = hex;
                UpdateColorSwatch(SwatchPropBackColor, color);
            }
            else
            {
                TxtPropColor.Text = hex;
                UpdateColorSwatch(SwatchPropColor, color);
            }
            SldColorR.Value = color.Red;
            SldColorG.Value = color.Green;
            SldColorB.Value = color.Blue;
            SldColorA.Value = color.Alpha;
            TxtColorR.Text = color.Red.ToString(CultureInfo.InvariantCulture);
            TxtColorG.Text = color.Green.ToString(CultureInfo.InvariantCulture);
            TxtColorB.Text = color.Blue.ToString(CultureInfo.InvariantCulture);
            TxtColorA.Text = color.Alpha.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressUiEvents = false;
        }

        CommitCurrentProps();
    }

    private void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents) return;
        LoadSelectedWidgetProps();
        RenderPreview();
    }

    private void OnThemeMetaChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressUiEvents && IsLoaded)
            RenderPreview();
    }

    private void OnResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || !IsLoaded) return;
        if (CmbResolution.SelectedItem is ComboBoxItem item && item.Content is string res)
        {
            var p = res.Split('x');
            if (p.Length == 2 && float.TryParse(p[0], out float w) && float.TryParse(p[1], out float h))
                ApplyCanvasResolution(w, h);
        }
    }

    private void OnPickBackgroundClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Fondos compatibles|*.png;*.jpg;*.jpeg;*.gif;*.mp4;*.webp|Todos|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _backgroundPath = dlg.FileName;
            _cachedBgSourcePath = null;
            TxtEditorStatus.Text = $"Fondo cargado: {Path.GetFileName(_backgroundPath)}";
            RenderPreview();
        }
    }

    private void OnClearBackgroundClick(object sender, RoutedEventArgs e)
    {
        _backgroundPath = null;
        _cachedBgSourcePath = null;
        _cachedBgBitmap?.Dispose();
        _cachedBgBitmap = null;
        TxtEditorStatus.Text = "Fondo eliminado.";
        RenderPreview();
    }

    private void OnOpenSmthemeClick(object sender, RoutedEventArgs e)
    {
        string customDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FoxuMarsDisplay", "custom_themes");
        var dlg = new OpenFileDialog
        {
            InitialDirectory = Directory.Exists(customDir) ? customDir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = "Temas Foxu / TURZX (*.smtheme;*.photo)|*.smtheme;*.photo|Todos|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            if (LoadThemeFromFile(dlg.FileName))
                TxtEditorStatus.Text = $"Tema cargado: {Path.GetFileName(dlg.FileName)}";
            else
                TxtEditorStatus.Text = "No se pudo cargar el tema seleccionado.";
        }
    }

    public bool LoadThemeFromFile(string themePath)
    {
        var loaded = UnifiedThemeLoader.Load(themePath);
        if (loaded == null) return false;

        _suppressUiEvents = true;
        try
        {
            TxtThemeName.Text = loaded.Name;
            _canvasW = Math.Max(64f, loaded.CanvasWidth);
            _canvasH = Math.Max(64f, loaded.CanvasHeight);
            _backgroundPath = loaded.ImageLayers.FirstOrDefault(l => l.IsBackground)?.FilePath
                              ?? loaded.ImageLayers.FirstOrDefault()?.FilePath;
            _cachedBgSourcePath = null;

            string resText = $"{Math.Round(_canvasW)}x{Math.Round(_canvasH)}";
            for (int i = 0; i < CmbResolution.Items.Count; i++)
            {
                if (CmbResolution.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Content?.ToString(), resText, StringComparison.OrdinalIgnoreCase))
                {
                    CmbResolution.SelectedIndex = i;
                    break;
                }
            }

            _loadedFontFiles.Clear();
            _loadedFontFiles.AddRange(loaded.FontFiles);
            PopulateFontChoices();

            _widgets.Clear();
            foreach (var b in loaded.BarWidgets)
            {
                _widgets.Add(new EditableWidget
                {
                    Kind = b.IsStaticFill ? "Card" : "Bar",
                    X = b.X, Y = b.Y, ZIndex = b.ZIndex,
                    Width = b.MaxWidth, Height = b.MaxHeight,
                    SensorKey = b.SensorKey,
                    Color = b.FillColor, BackColor = b.BackColor,
                    IsStatic = b.IsStaticFill
                });
            }
            foreach (var r in loaded.RadialWidgets)
            {
                float side = r.Radius * 2.2f;
                _widgets.Add(new EditableWidget
                {
                    Kind = "Radial",
                    X = r.Cx - side * 0.5f, Y = r.Cy - side * 0.5f, ZIndex = r.ZIndex,
                    Width = side, Height = side,
                    SensorKey = r.SensorKey, Unit = r.Unit,
                    Color = r.FillColor, BackColor = r.BackColor, ShowText = r.ShowText
                });
            }
            foreach (var g in loaded.GraphWidgets)
            {
                _widgets.Add(new EditableWidget
                {
                    Kind = "Graph",
                    X = g.X, Y = g.Y, ZIndex = g.ZIndex,
                    Width = g.Width, Height = g.Height,
                    SensorKey = g.SensorKey, Color = g.LineColor, BackColor = g.BackColor
                });
            }
            foreach (var av in loaded.AudioVisualizers)
            {
                _widgets.Add(new EditableWidget
                {
                    Kind = "AudioFFT",
                    X = av.X, Y = av.Y, ZIndex = av.ZIndex,
                    Width = av.Width, Height = av.Height,
                    Color = av.PrimaryColor, BackColor = av.SecondaryColor
                });
            }
            foreach (var art in loaded.AlbumArts)
            {
                _widgets.Add(new EditableWidget
                {
                    Kind = "AlbumArt",
                    X = art.X, Y = art.Y, ZIndex = art.ZIndex,
                    Width = art.Width, Height = art.Height
                });
            }
            foreach (var t in loaded.TextWidgets)
            {
                _widgets.Add(new EditableWidget
                {
                    Kind = "Text",
                    X = t.X, Y = t.Y, ZIndex = t.ZIndex,
                    Width = t.BoxWidth > 0 ? t.BoxWidth : 160f,
                    FontSize = t.FontSize,
                    FontFamily = t.FontFamily,
                    Align = t.Align,
                    IsBold = t.IsBold,
                    IsItalic = t.IsItalic,
                    IsUnderline = t.IsUnderline,
                    IsStrikeout = t.IsStrikeout,
                    HasShadow = t.HasShadow,
                    SensorKey = t.SensorKey,
                    PrefixOrText = t.IsStaticText ? (t.StaticContent ?? string.Empty) : t.Prefix,
                    Unit = t.Unit,
                    Color = t.Color,
                    IsStatic = t.IsStaticText
                });
            }
        }
        finally
        {
            _suppressUiEvents = false;
        }

        RefreshLayerList(_widgets.Count > 0 ? 0 : -1);
        RenderPreview();
        return true;
    }

    private void AddWidget(EditableWidget w)
    {
        _widgets.Add(w);
        RefreshLayerList(_widgets.Count - 1);
        RenderPreview();
    }

    private void OnAddSensorTextClick(object sender, RoutedEventArgs e) =>
        AddWidget(new EditableWidget { Kind = "Text", X = 24, Y = 120, FontSize = 24, SensorKey = "GPUTemp", PrefixOrText = "GPU ", Unit = "°C", Color = SKColors.White });

    private void OnAddStaticTextClick(object sender, RoutedEventArgs e) =>
        AddWidget(new EditableWidget { Kind = "Text", X = 24, Y = 40, FontSize = 18, PrefixOrText = "FOXU HUD", Unit = "", Color = SKColor.Parse("#FFFF6419"), IsStatic = true });

    private void OnAddBarClick(object sender, RoutedEventArgs e) =>
        AddWidget(new EditableWidget { Kind = "Bar", X = 24, Y = 200, Width = 220, Height = 16, SensorKey = "MemoryUsage", Color = SKColor.Parse("#FFB57BFF"), BackColor = SKColor.Parse("#FF261F33") });

    private void OnAddCardClick(object sender, RoutedEventArgs e) =>
        AddWidget(new EditableWidget { Kind = "Card", X = 16, Y = 16, ZIndex = 0, Width = 280, Height = 90, Color = new SKColor(20, 18, 32, 200), IsStatic = true });

    private void OnAddRadialClick(object sender, RoutedEventArgs e) =>
        AddWidget(new EditableWidget { Kind = "Radial", X = 80, Y = 240, Width = 120, Height = 120, SensorKey = "CpuUsage", Color = SKColor.Parse("#FFFF6419"), BackColor = SKColor.Parse("#FF261F33") });

    private void OnAddGraphClick(object sender, RoutedEventArgs e) =>
        AddWidget(new EditableWidget { Kind = "Graph", X = 24, Y = 380, Width = 280, Height = 70, SensorKey = "CpuUsage", Color = SKColor.Parse("#FFFFB820"), BackColor = new SKColor(20, 17, 30, 180) });

    private void OnAddAudioClick(object sender, RoutedEventArgs e) =>
        AddWidget(new EditableWidget { Kind = "AudioFFT", X = 24, Y = 470, Width = 280, Height = 60, Color = SKColors.Cyan, BackColor = SKColors.Magenta });

    private void OnAddAlbumArtClick(object sender, RoutedEventArgs e) =>
        AddWidget(new EditableWidget { Kind = "AlbumArt", X = 24, Y = 550, Width = 96, Height = 96 });

    private void OnDeleteWidgetClick(object sender, RoutedEventArgs e)
    {
        int idx = LstLayers.SelectedIndex;
        if (idx >= 0 && idx < _widgets.Count)
        {
            _widgets.RemoveAt(idx);
            RefreshLayerList(Math.Min(idx, _widgets.Count - 1));
            RenderPreview();
        }
    }

    private void OnPropEdited(object sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents || LstLayers.SelectedIndex < 0 || LstLayers.SelectedIndex >= _widgets.Count)
            return;
        CommitCurrentProps();
    }

    private void CommitCurrentProps()
    {
        if (_suppressUiEvents || LstLayers.SelectedIndex < 0 || LstLayers.SelectedIndex >= _widgets.Count)
            return;

        var w = _widgets[LstLayers.SelectedIndex];
        if (float.TryParse(TxtPropX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
            w.X = Math.Clamp(x, 0f, Math.Max(0f, _canvasW - 12f));
        if (float.TryParse(TxtPropY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            w.Y = Math.Clamp(y, 0f, Math.Max(0f, _canvasH - 12f));
        if (float.TryParse(TxtPropW.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float width) && width > 4)
            w.Width = width;
        if (w.Kind != "Text" &&
            float.TryParse(TxtPropH.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float height) &&
            height > 4)
        {
            w.Height = height;
        }
        if (CmbPropSensor.SelectedItem is ComboBoxItem selItem)
        {
            string sensorName = (selItem.Tag as string ?? selItem.Content?.ToString() ?? string.Empty).Trim();
            if (w.Kind == "Text")
            {
                if (string.IsNullOrEmpty(sensorName))
                {
                    w.SensorKey = string.Empty;
                    w.IsStatic = true;
                }
                else
                {
                    w.SensorKey = sensorName;
                    w.IsStatic = false;
                }
            }
            else if (!string.IsNullOrEmpty(sensorName))
            {
                w.SensorKey = sensorName;
            }
        }
        w.PrefixOrText = TxtPropText.Text ?? string.Empty;
        w.Unit = TxtPropUnit.Text ?? string.Empty;
        w.Color = UnifiedThemeLoader.ParseColor(TxtPropColor.Text, w.Color);
        w.BackColor = UnifiedThemeLoader.ParseColor(TxtPropBackColor.Text, w.BackColor);
        UpdateColorSwatch(SwatchPropColor, w.Color);
        UpdateColorSwatch(SwatchPropBackColor, w.BackColor);

        if (w.Kind == "Text")
        {
            w.FontFamily = (CmbPropFont.SelectedItem as FontChoice)?.FamilyKey ?? string.Empty;
            if (float.TryParse(TxtPropFontSize.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float fs) && fs >= 6f)
            {
                w.FontSize = Math.Clamp(fs, 6f, 240f);
                _suppressUiEvents = true;
                try
                {
                    TxtPropH.Text = MathF.Round(Math.Max(26f, w.FontSize * 1.3f)).ToString("0", CultureInfo.InvariantCulture);
                    SelectFontSizePreset(w.FontSize);
                }
                finally
                {
                    _suppressUiEvents = false;
                }
            }
            w.IsBold = TglBold.IsChecked == true;
            w.IsItalic = TglItalic.IsChecked == true;
            w.IsUnderline = TglUnderline.IsChecked == true;
            w.IsStrikeout = TglStrikeout.IsChecked == true;
            w.HasShadow = TglShadow.IsChecked == true;
        }

        int idx = LstLayers.SelectedIndex;
        RefreshLayerList(idx);
        RenderPreview();
    }

    private void SelectFontSizePreset(float fontSize)
    {
        if (CmbPropFontSize == null) return;
        string target = MathF.Round(fontSize).ToString("0", CultureInfo.InvariantCulture);
        for (int i = 0; i < CmbPropFontSize.Items.Count; i++)
        {
            if (CmbPropFontSize.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), target, StringComparison.Ordinal))
            {
                CmbPropFontSize.SelectedIndex = i;
                return;
            }
        }
        CmbPropFontSize.SelectedIndex = -1;
    }

    private void OnFontSizeDecClick(object sender, RoutedEventArgs e) => AdjustSelectedFontSize(-2f);
    private void OnFontSizeIncClick(object sender, RoutedEventArgs e) => AdjustSelectedFontSize(2f);

    private void AdjustSelectedFontSize(float baseDelta)
    {
        if (LstLayers.SelectedIndex < 0 || LstLayers.SelectedIndex >= _widgets.Count)
            return;
        var w = _widgets[LstLayers.SelectedIndex];
        if (w.Kind != "Text") return;

        float mult = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 3f
            : (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ? 0.5f : 1f;
        float next = Math.Clamp(w.FontSize + baseDelta * mult, 6f, 240f);

        _suppressUiEvents = true;
        try
        {
            TxtPropFontSize.Text = next.ToString("0", CultureInfo.InvariantCulture);
            SelectFontSizePreset(next);
        }
        finally
        {
            _suppressUiEvents = false;
        }
        CommitCurrentProps();
    }

    private void OnFontSizePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || CmbPropFontSize.SelectedItem is not ComboBoxItem item)
            return;
        if (float.TryParse(item.Content?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float fs))
        {
            _suppressUiEvents = true;
            try
            {
                TxtPropFontSize.Text = fs.ToString("0", CultureInfo.InvariantCulture);
            }
            finally
            {
                _suppressUiEvents = false;
            }
            CommitCurrentProps();
        }
    }

    private void OnAlignLeftClick(object sender, RoutedEventArgs e) => SetTextAlignment(TextAlignMode.Left);
    private void OnAlignCenterClick(object sender, RoutedEventArgs e) => SetTextAlignment(TextAlignMode.Center);
    private void OnAlignRightClick(object sender, RoutedEventArgs e) => SetTextAlignment(TextAlignMode.Right);

    private void SetTextAlignment(TextAlignMode align)
    {
        if (_suppressUiEvents || LstLayers.SelectedIndex < 0 || LstLayers.SelectedIndex >= _widgets.Count)
            return;
        var w = _widgets[LstLayers.SelectedIndex];
        w.Align = align;
        _suppressUiEvents = true;
        UpdateAlignButtons(align);
        _suppressUiEvents = false;
        CommitCurrentProps();
    }

    private void OnCenterOnCanvasClick(object sender, RoutedEventArgs e)
    {
        if (LstLayers.SelectedIndex < 0 || LstLayers.SelectedIndex >= _widgets.Count)
            return;
        var w = _widgets[LstLayers.SelectedIndex];
        if (w.Kind == "Text")
        {
            w.X = 0f;
            w.Width = _canvasW;
            w.Align = TextAlignMode.Center;
        }
        else
        {
            w.X = MathF.Round(Math.Max(0f, (_canvasW - w.Width) * 0.5f));
        }

        LoadSelectedWidgetProps();
        RefreshLayerList(LstLayers.SelectedIndex);
        RenderPreview();
        TxtEditorStatus.Text = "Widget centrado en la pantalla.";
    }

    private void OnOpenColorPaletteClick(object sender, RoutedEventArgs e)
    {
        BtnBackColorDropDown.IsChecked = false;
        OpenColorPopup(isBackColor: false, BtnColorDropDown);
    }

    private void OnOpenBackColorPaletteClick(object sender, RoutedEventArgs e)
    {
        BtnColorDropDown.IsChecked = false;
        OpenColorPopup(isBackColor: true, BtnBackColorDropDown);
    }

    private void OnColorPopupClosed(object? sender, EventArgs e)
    {
        BtnColorDropDown.IsChecked = false;
        BtnBackColorDropDown.IsChecked = false;
    }

    private void OnPickTransparentColorClick(object sender, RoutedEventArgs e) =>
        ApplyPickedColor(new SKColor(0, 0, 0, 0));

    private void OnColorSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressUiEvents) return;
        byte r = (byte)Math.Clamp((int)Math.Round(SldColorR.Value), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(SldColorG.Value), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(SldColorB.Value), 0, 255);
        byte a = (byte)Math.Clamp((int)Math.Round(SldColorA.Value), 0, 255);
        ApplyPickedColor(new SKColor(r, g, b, a));
    }

    private void OnColorHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUiEvents || LstLayers.SelectedIndex < 0 || LstLayers.SelectedIndex >= _widgets.Count)
            return;
        if (sender == TxtPropColor)
            UpdateColorSwatch(SwatchPropColor, UnifiedThemeLoader.ParseColor(TxtPropColor.Text, _widgets[LstLayers.SelectedIndex].Color));
        else if (sender == TxtPropBackColor)
            UpdateColorSwatch(SwatchPropBackColor, UnifiedThemeLoader.ParseColor(TxtPropBackColor.Text, _widgets[LstLayers.SelectedIndex].BackColor));
    }

    private void OnAutoContrastClick(object sender, RoutedEventArgs e)
    {
        CommitCurrentProps();
        int idx = LstLayers.SelectedIndex;
        if (idx < 0 || idx >= _widgets.Count) return;

        var w = _widgets[idx];
        EnsureCachedBackgroundPreviewPath();
        var region = new SKRect(w.X, w.Y, w.X + Math.Max(60, w.Width), w.Y + Math.Max(28, w.Height));
        w.Color = ThemeExporter.ComputeAutoContrastColor(_cachedBgBitmap, region, w.Color);
        LoadSelectedWidgetProps();
        RenderPreview();
        TxtEditorStatus.Text = "Auto-contraste WCAG aplicado.";
    }

    private void OnRefreshPreviewClick(object sender, RoutedEventArgs e)
    {
        CommitCurrentProps();
        RenderPreview();
    }

    private static SKRect GetWidgetBounds(EditableWidget w)
    {
        float sw = w.Kind == "Text" ? Math.Max(80f, w.Width) : Math.Max(20f, w.Width);
        float sh = w.Kind == "Text" ? Math.Max(26f, w.FontSize * 1.25f) : Math.Max(20f, w.Height);
        return new SKRect(w.X, w.Y, w.X + sw, w.Y + sh);
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        Point pos = e.GetPosition(ImgPreviewCanvas);
        float px = (float)pos.X;
        float py = (float)pos.Y;

        int hitIdx = -1;
        int bestZ = int.MinValue;
        bool bestIsCard = true;
        for (int i = _widgets.Count - 1; i >= 0; i--)
        {
            var bounds = GetWidgetBounds(_widgets[i]);
            if (px >= bounds.Left - 4 && px <= bounds.Right + 4 &&
                py >= bounds.Top - 4 && py <= bounds.Bottom + 4)
            {
                bool isCard = _widgets[i].Kind == "Card";
                if (hitIdx < 0 || (!isCard && bestIsCard) || (isCard == bestIsCard && _widgets[i].ZIndex >= bestZ))
                {
                    hitIdx = i;
                    bestZ = _widgets[i].ZIndex;
                    bestIsCard = isCard;
                }
            }
        }

        if (hitIdx >= 0 && hitIdx != LstLayers.SelectedIndex)
        {
            LstLayers.SelectedIndex = hitIdx;
            LoadSelectedWidgetProps();
            RenderPreview();
        }

        int idx = LstLayers.SelectedIndex;
        if (idx < 0 || idx >= _widgets.Count) return;
        _isDragging = true;
        _dragStart = pos;
        _origX = _widgets[idx].X;
        _origY = _widgets[idx].Y;
        ImgPreviewCanvas.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || LstLayers.SelectedIndex < 0 || LstLayers.SelectedIndex >= _widgets.Count)
            return;

        Point pos = e.GetPosition(ImgPreviewCanvas);
        float dx = (float)(pos.X - _dragStart.X);
        float dy = (float)(pos.Y - _dragStart.Y);
        var w = _widgets[LstLayers.SelectedIndex];
        w.X = Math.Clamp(MathF.Round((_origX + dx) / 4f) * 4f, 0f, Math.Max(0f, _canvasW - 12f));
        w.Y = Math.Clamp(MathF.Round((_origY + dy) / 4f) * 4f, 0f, Math.Max(0f, _canvasH - 12f));
        LoadSelectedWidgetProps();
        RenderPreview();
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ImgPreviewCanvas.ReleaseMouseCapture();
        RefreshLayerList(LstLayers.SelectedIndex);
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            if (e.Key == Key.Enter)
            {
                CommitCurrentProps();
                e.Handled = true;
            }
            return;
        }

        int idx = LstLayers.SelectedIndex;
        if (e.Key == Key.Escape)
        {
            if (_isDragging && idx >= 0 && idx < _widgets.Count)
            {
                _isDragging = false;
                ImgPreviewCanvas.ReleaseMouseCapture();
                _widgets[idx].X = _origX;
                _widgets[idx].Y = _origY;
                RefreshLayerList(idx);
                RenderPreview();
            }
            else
            {
                Close();
            }
            e.Handled = true;
            return;
        }

        if (idx < 0 || idx >= _widgets.Count) return;

        if (e.Key == Key.Delete)
        {
            OnDeleteWidgetClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            var src = _widgets[idx];
            AddWidget(new EditableWidget
            {
                Kind = src.Kind,
                X = Math.Clamp(src.X + 12f, 0f, Math.Max(0f, _canvasW - 12f)),
                Y = Math.Clamp(src.Y + 12f, 0f, Math.Max(0f, _canvasH - 12f)),
                ZIndex = src.ZIndex,
                Width = src.Width,
                Height = src.Height,
                FontSize = src.FontSize,
                FontFamily = src.FontFamily,
                Align = src.Align,
                IsBold = src.IsBold,
                IsItalic = src.IsItalic,
                IsUnderline = src.IsUnderline,
                IsStrikeout = src.IsStrikeout,
                HasShadow = src.HasShadow,
                SensorKey = src.SensorKey,
                PrefixOrText = src.PrefixOrText,
                Unit = src.Unit,
                Color = src.Color,
                BackColor = src.BackColor,
                IsStatic = src.IsStatic,
                ShowText = src.ShowText
            });
            TxtEditorStatus.Text = "Widget duplicado (Ctrl+D).";
            e.Handled = true;
            return;
        }

        float step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 12f
            : (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ? 1f : 4f;
        float dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0f;
        float dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0f;
        if (dx != 0f || dy != 0f)
        {
            var w = _widgets[idx];
            w.X = Math.Clamp(w.X + dx, 0f, Math.Max(0f, _canvasW - 12f));
            w.Y = Math.Clamp(w.Y + dy, 0f, Math.Max(0f, _canvasH - 12f));
            RefreshLayerList(idx);
            RenderPreview();
            e.Handled = true;
        }
    }

    private void OnExportSettingTxtClick(object sender, RoutedEventArgs e)
    {
        CommitCurrentProps();
        var dlg = new SaveFileDialog { FileName = "Setting.txt", Filter = "SOEYI Setting.txt|*.txt" };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, ThemeExporter.ExportToSettingTxt(BuildDisplayTheme()));
            TxtEditorStatus.Text = $"Exportado: {dlg.FileName}";
        }
    }

    private void OnExportSmthemeClick(object sender, RoutedEventArgs e)
    {
        CommitCurrentProps();
        var theme = BuildDisplayTheme();
        var dlg = new SaveFileDialog { FileName = $"{theme.Name}.smtheme", Filter = "Paquete .smtheme|*.smtheme" };
        if (dlg.ShowDialog() == true)
        {
            ThemeExporter.ExportToSmthemePackage(theme, dlg.FileName);
            TxtEditorStatus.Text = $"Paquete .smtheme exportado: {dlg.FileName}";
        }
    }

    private void OnApplyToScreenClick(object sender, RoutedEventArgs e)
    {
        CommitCurrentProps();
        var chosen = CmbTargetScreen?.SelectedItem as ScreenChoice;
        string? targetSn = chosen?.SerialNumber ?? _targetSerial;
        if (_host == null || string.IsNullOrWhiteSpace(targetSn))
        {
            TxtEditorStatus.Text = "No hay pantalla conectada seleccionada para aplicar en vivo.";
            return;
        }

        var theme = BuildDisplayTheme();
        string outDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FoxuMarsDisplay", "custom_themes");
        Directory.CreateDirectory(outDir);
        string smPath = Path.Combine(outDir, $"{theme.Name}.smtheme");
        ThemeExporter.ExportToSmthemePackage(theme, smPath);
        bool ok = _host.ApplyTheme(targetSn, smPath);
        string targetLabel = chosen?.Label ?? targetSn;
        TxtEditorStatus.Text = ok
            ? $"Aplicado en vivo a {targetLabel} (guardado en Biblioteca Local)"
            : "Error al aplicar tema.";
    }
}
