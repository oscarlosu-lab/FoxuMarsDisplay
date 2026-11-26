using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private readonly DisplayHost? _host;
    private string? _targetSerial;
    private readonly List<EditableWidget> _widgets = [];
    private string? _backgroundPath;
    private SKBitmap? _cachedBgBitmap;
    private string? _cachedBgSourcePath;
    private string? _cachedBgPreviewPath;
    private float _canvasW = 360f;
    private float _canvasH = 960f;
    private bool _suppressUiEvents;
    private bool _isDragging;
    private Point _dragStart;
    private float _origX;
    private float _origY;

    public ThemeEditorWindow(DisplayHost? host = null, string? targetSerial = null)
    {
        _host = host;
        _targetSerial = targetSerial;
        InitializeComponent();

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
                        Color = w.Color, SensorKey = w.IsStatic ? string.Empty : w.SensorKey,
                        Prefix = w.IsStatic ? string.Empty : w.PrefixOrText,
                        Unit = w.IsStatic ? string.Empty : w.Unit,
                        IsStaticText = w.IsStatic,
                        StaticContent = w.IsStatic ? w.PrefixOrText : null
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
        SelectSensorItem(w.SensorKey);
        TxtPropText.Text = w.PrefixOrText;
        TxtPropUnit.Text = w.Unit;
        TxtPropColor.Text = $"#{w.Color.Alpha:X2}{w.Color.Red:X2}{w.Color.Green:X2}{w.Color.Blue:X2}";
        TxtPropBackColor.Text = $"#{w.BackColor.Alpha:X2}{w.BackColor.Red:X2}{w.BackColor.Green:X2}{w.BackColor.Blue:X2}";
        _suppressUiEvents = false;
    }

    private void SelectSensorItem(string sensorKey)
    {
        if (CmbPropSensor == null) return;
        if (string.IsNullOrWhiteSpace(sensorKey))
        {
            CmbPropSensor.SelectedIndex = -1;
            return;
        }

        for (int i = 0; i < CmbPropSensor.Items.Count; i++)
        {
            if (CmbPropSensor.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), sensorKey, StringComparison.OrdinalIgnoreCase))
            {
                CmbPropSensor.SelectedIndex = i;
                return;
            }
        }

        var custom = new ComboBoxItem { Content = sensorKey };
        CmbPropSensor.Items.Add(custom);
        CmbPropSensor.SelectedItem = custom;
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
        if (float.TryParse(TxtPropH.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float height) && height > 4)
        {
            if (w.Kind == "Text") w.FontSize = height;
            else w.Height = height;
        }
        if (CmbPropSensor.SelectedItem is ComboBoxItem selItem && selItem.Content is string sensorName)
            w.SensorKey = sensorName.Trim();
        w.PrefixOrText = TxtPropText.Text ?? string.Empty;
        w.Unit = TxtPropUnit.Text ?? string.Empty;
        w.Color = UnifiedThemeLoader.ParseColor(TxtPropColor.Text, w.Color);
        w.BackColor = UnifiedThemeLoader.ParseColor(TxtPropBackColor.Text, w.BackColor);

        int idx = LstLayers.SelectedIndex;
        RefreshLayerList(idx);
        RenderPreview();
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
                SensorKey = src.SensorKey,
                PrefixOrText = src.PrefixOrText,
                Unit = src.Unit,
                Color = src.Color,
                BackColor = src.BackColor,
                IsStatic = src.IsStatic
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
