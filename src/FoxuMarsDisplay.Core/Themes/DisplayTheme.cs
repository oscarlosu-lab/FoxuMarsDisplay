using SkiaSharp;

namespace FoxuMarsDisplay.Core.Themes;

public enum TextAlignMode
{
    Left,
    Center,
    Right
}

public enum ThemeScaleMode
{
    Auto,
    Stretch,
    Fit
}

public sealed class DisplayTheme
{
    public string Name { get; init; } = "Default";
    public string SourceDirectory { get; init; } = string.Empty;
    public float CanvasWidth { get; init; } = 360f;
    public float CanvasHeight { get; init; } = 960f;
    public int DisplayDirection { get; init; }
    public ThemeScaleMode ScaleMode { get; init; } = ThemeScaleMode.Auto;

    public List<ImageLayerSpec> ImageLayers { get; } = [];
    public List<TextWidgetSpec> TextWidgets { get; } = [];
    public List<BarWidgetSpec> BarWidgets { get; } = [];
    public List<RadialWidgetSpec> RadialWidgets { get; } = [];
    public List<GraphWidgetSpec> GraphWidgets { get; } = [];
    public List<string> FontFiles { get; } = [];
    public List<string> FrameSequenceFiles { get; } = [];
}

public sealed class ImageLayerSpec
{
    public string FilePath { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public int ZIndex { get; init; }
    public float? Width { get; init; }
    public float? Height { get; init; }
    public bool IsBackground { get; init; }
    public bool IsGif => FilePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
}

public sealed class TextWidgetSpec
{
    public float X { get; init; }
    public float Y { get; init; }
    public int ZIndex { get; init; }
    public float FontSize { get; init; } = 24f;
    public string FontFamily { get; init; } = string.Empty;
    public SKColor Color { get; init; } = SKColors.White;
    public float Opacity { get; init; } = 1.0f;
    public string SensorKey { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public bool IsStaticText { get; init; }
    public string? StaticContent { get; init; }
    public TextAlignMode Align { get; init; } = TextAlignMode.Left;
    public bool IsBold { get; init; }
}

public sealed class BarWidgetSpec
{
    public float X { get; init; }
    public float Y { get; init; }
    public int ZIndex { get; init; }
    public float MaxWidth { get; init; } = 200f;
    public float MaxHeight { get; init; } = 20f;
    public float MinNum { get; init; }
    public float MaxNum { get; init; } = 100f;
    public float CornerRadius { get; init; }
    public SKColor FillColor { get; init; } = SKColors.Cyan;
    public SKColor BackColor { get; init; } = SKColor.Empty;
    public string SensorKey { get; init; } = string.Empty;
    public bool IsSegmented { get; init; }
}

public sealed class RadialWidgetSpec
{
    public float Cx { get; init; }
    public float Cy { get; init; }
    public float Radius { get; init; } = 48f;
    public float Thickness { get; init; } = 10f;
    public float StartAngle { get; init; } = -225f;
    public float EndAngle { get; init; } = 45f;
    public float MinValue { get; init; }
    public float MaxValue { get; init; } = 100f;
    public SKColor FillColor { get; init; } = SKColors.OrangeRed;
    public SKColor BackColor { get; init; } = new(38, 31, 51, 220);
    public bool ShowText { get; init; } = true;
    public float FontSize { get; init; } = 20f;
    public string FontFamily { get; init; } = string.Empty;
    public SKColor TextColor { get; init; } = SKColors.White;
    public string Unit { get; init; } = "%";
    public string SensorKey { get; init; } = string.Empty;
    public int ZIndex { get; init; }
}

public sealed class GraphWidgetSpec
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; } = 160f;
    public float Height { get; init; } = 60f;
    public float MinValue { get; init; }
    public float MaxValue { get; init; } = 100f;
    public SKColor LineColor { get; init; } = SKColors.OrangeRed;
    public SKColor FillColor { get; init; } = new(255, 100, 25, 70);
    public SKColor BackColor { get; init; } = new(20, 17, 30, 180);
    public float LineThickness { get; init; } = 2.5f;
    public int MaxSamples { get; init; } = 60;
    public string SensorKey { get; init; } = string.Empty;
    public int ZIndex { get; init; }
}
