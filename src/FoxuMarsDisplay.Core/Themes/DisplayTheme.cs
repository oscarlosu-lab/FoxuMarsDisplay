using SkiaSharp;

namespace FoxuMarsDisplay.Core.Themes;

public sealed class DisplayTheme
{
    public string Name { get; init; } = "Default";
    public string SourceDirectory { get; init; } = string.Empty;
    public float CanvasWidth { get; init; } = 360f;
    public float CanvasHeight { get; init; } = 960f;
    public int DisplayDirection { get; init; }

    public List<ImageLayerSpec> ImageLayers { get; } = [];
    public List<TextWidgetSpec> TextWidgets { get; } = [];
    public List<BarWidgetSpec> BarWidgets { get; } = [];
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
}

public sealed class BarWidgetSpec
{
    public float X { get; init; }
    public float Y { get; init; }
    public int ZIndex { get; init; }
    public float MaxWidth { get; init; } = 200f;
    public float MaxHeight { get; init; } = 20f;
    public float MaxNum { get; init; } = 100f;
    public float CornerRadius { get; init; }
    public SKColor FillColor { get; init; } = SKColors.Cyan;
    public SKColor BackColor { get; init; } = SKColor.Empty;
    public string SensorKey { get; init; } = string.Empty;
}
