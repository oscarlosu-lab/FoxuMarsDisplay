namespace FoxuMarsDisplay.Core.Themes;

public readonly record struct ScaledWidgetBounds(float X, float Y, float Width, float Height, float FontSize);

/// <summary>
/// Proportionally scales widget coordinates and dimensions when adapting a theme canvas
/// from one display resolution to another.
/// </summary>
public static class ThemeCanvasScaler
{
    public static ScaledWidgetBounds ScaleRect(
        float x,
        float y,
        float width,
        float height,
        float fontSize,
        float oldCanvasW,
        float oldCanvasH,
        float newCanvasW,
        float newCanvasH,
        bool preserveSquareAspect)
    {
        float safeOldW = MathF.Max(1f, oldCanvasW);
        float safeOldH = MathF.Max(1f, oldCanvasH);
        float safeNewW = MathF.Max(16f, newCanvasW);
        float safeNewH = MathF.Max(16f, newCanvasH);

        float sx = safeNewW / safeOldW;
        float sy = safeNewH / safeOldH;
        float sMin = MathF.Min(sx, sy);

        float scaledW = MathF.Max(12f, width * (preserveSquareAspect ? sMin : sx));
        float scaledH = MathF.Max(8f, height * (preserveSquareAspect ? sMin : sy));
        float scaledFont = MathF.Max(8f, fontSize * sMin);

        float scaledX = Math.Clamp(x * sx, 0f, MathF.Max(0f, safeNewW - 12f));
        float scaledY = Math.Clamp(y * sy, 0f, MathF.Max(0f, safeNewH - 8f));

        return new ScaledWidgetBounds(
            MathF.Round(scaledX, 1),
            MathF.Round(scaledY, 1),
            MathF.Round(scaledW, 1),
            MathF.Round(scaledH, 1),
            MathF.Round(scaledFont, 1));
    }
}
