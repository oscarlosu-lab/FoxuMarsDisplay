using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using FoxuMarsDisplay.Core.Sensors;
using FoxuMarsDisplay.Core.Themes;

namespace FoxuMarsDisplay.Core.Rendering;

/// <summary>
/// Zero-allocation SkiaSharp theme renderer:
/// - Owns a single fixed BGRA8888 SKBitmap per screen (reused every frame)
/// - Pre-crops &amp; downsamples animated GIFs / PNG sequences once and persists them to a compact
///   disk-cached WebP stream (.sdac), keeping RAM under ~10 MB per animated theme
/// - Uses FNV-1a dirty-checking on static themes for 0% render CPU when unchanged
/// </summary>
public sealed class SkiaThemeRenderer : IDisposable
{
    private const uint SdacMagic = 0x43414453; // "SDAC"
    private const int SdacVersion = 1;

    private readonly DisplayTheme _theme;
    private readonly int _panelWidth;
    private readonly int _panelHeight;
    private readonly int _targetFps;
    private readonly string _animCacheRoot;
    private readonly int _hardwareRotationOffset;
    private int _brightness;
    private int _rotationOverride = -1;
    private ThemeScaleMode _scaleModeOverride;

    private readonly SKBitmap _outputBitmap;
    private readonly SKCanvas _outputCanvas;
    private SKBitmap _logicalBitmap;
    private SKCanvas _logicalCanvas;

    private readonly Dictionary<string, SKTypeface> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SKBitmap> _staticImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SKBitmap> _statusIconBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CachedAnimFrame> _animFrames = [];
    private readonly List<GraphHistoryBuffer> _graphBuffers = [];
    private SKBitmap? _animScratchBitmap;
    private SKBitmap? _albumArtBitmap;
    private ulong _lastAlbumArtHash;

    private readonly SKPaint _textPaint = new() { IsAntialias = true, SubpixelText = true };
    private readonly SKPaint _barPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _barStrokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _iconPaint = new() { FilterQuality = SKFilterQuality.Medium };
    private readonly SKPaint _radialPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _graphPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPath _graphPath = new();
    private readonly SKPaint _blitPaint = new() { FilterQuality = SKFilterQuality.Medium };
    private SKColorFilter? _brightnessFilter;
    private int _cachedFilterBrightness = 100;

    private ulong _lastRenderHash;
    private bool _hasRenderedOnce;
    private int _lastAnimIndex = -1;
    private long _totalAnimDurationMs;
    private bool _disposed;

    public SKBitmap OutputBitmap => _outputBitmap;
    public int PanelWidth => _panelWidth;
    public int PanelHeight => _panelHeight;
    public bool IsAnimated => _animFrames.Count > 1;
    public string? LastAnimCachePath { get; private set; }

    public int Brightness
    {
        get => _brightness;
        set => _brightness = Math.Clamp(value, 0, 100);
    }

    public int EffectiveRotation => _rotationOverride >= 0 ? _rotationOverride : (_theme.DisplayDirection & 3);
    public int HardwareRotationOffset => _hardwareRotationOffset;
    public int PhysicalRotation => (EffectiveRotation + _hardwareRotationOffset) & 3;
    public ThemeScaleMode ScaleMode => _scaleModeOverride;

    public SkiaThemeRenderer(
        DisplayTheme theme,
        int panelWidth,
        int panelHeight,
        int targetFps = 12,
        int brightness = 100,
        int rotationOverride = -1,
        string? customCacheDir = null,
        int hardwareRotationOffset = 0,
        ThemeScaleMode scaleModeOverride = ThemeScaleMode.Auto)
    {
        _theme = theme;
        _panelWidth = panelWidth;
        _panelHeight = panelHeight;
        _targetFps = Math.Clamp(targetFps, 1, 60);
        _brightness = Math.Clamp(brightness, 0, 100);
        _rotationOverride = rotationOverride >= 0 ? (rotationOverride & 3) : -1;
        _hardwareRotationOffset = ((hardwareRotationOffset % 4) + 4) % 4;
        _scaleModeOverride = scaleModeOverride;

        if (!string.IsNullOrWhiteSpace(customCacheDir))
        {
            _animCacheRoot = customCacheDir;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _animCacheRoot = Path.Combine(localAppData, "FoxuMarsDisplay", "cache", "anim");
        }

        _outputBitmap = new SKBitmap(panelWidth, panelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        _outputCanvas = new SKCanvas(_outputBitmap);

        var (logW, logH) = GetLogicalDimensions(PhysicalRotation);
        _logicalBitmap = new SKBitmap(logW, logH, SKColorType.Bgra8888, SKAlphaType.Premul);
        _logicalCanvas = new SKCanvas(_logicalBitmap);

        LoadFonts();
        PreloadAssets(logW, logH);
        InitGraphBuffers();
    }

    private void InitGraphBuffers()
    {
        _graphBuffers.Clear();
        foreach (var g in _theme.GraphWidgets)
            _graphBuffers.Add(new GraphHistoryBuffer(Math.Clamp(g.MaxSamples, 10, 180)));
    }

    private (float Ox, float Oy, float Sx, float Sy) ComputeTransform(int targetW, int targetH)
    {
        float cw = Math.Max(1f, _theme.CanvasWidth);
        float ch = Math.Max(1f, _theme.CanvasHeight);
        float tw = Math.Max(1f, targetW);
        float th = Math.Max(1f, targetH);

        float themeAspect = cw / ch;
        float targetAspect = tw / th;
        float ratio = Math.Max(themeAspect / targetAspect, targetAspect / themeAspect);

        var effectiveMode = _scaleModeOverride != ThemeScaleMode.Auto ? _scaleModeOverride : _theme.ScaleMode;
        bool useFit = effectiveMode == ThemeScaleMode.Fit ||
                      (effectiveMode == ThemeScaleMode.Auto && ratio > 1.22f);

        if (useFit)
        {
            float scale = Math.Min(tw / cw, th / ch);
            float ox = (tw - (cw * scale)) * 0.5f;
            float oy = (th - (ch * scale)) * 0.5f;
            return (ox, oy, scale, scale);
        }

        return (0f, 0f, tw / cw, th / ch);
    }

    public void SetScaleMode(ThemeScaleMode mode)
    {
        if (_disposed || _scaleModeOverride == mode)
            return;

        _scaleModeOverride = mode;
        foreach (var bmp in _staticImages.Values) bmp.Dispose();
        _staticImages.Clear();
        _animFrames.Clear();
        _animScratchBitmap?.Dispose();
        _animScratchBitmap = null;
        _totalAnimDurationMs = 0;

        PreloadAssets(_logicalBitmap.Width, _logicalBitmap.Height);
        _hasRenderedOnce = false;
    }

    private (int W, int H) GetLogicalDimensions(int rot)
    {
        bool swap = rot == 1 || rot == 3;
        return swap ? (_panelHeight, _panelWidth) : (_panelWidth, _panelHeight);
    }

    public void SetRotation(int direction)
    {
        int newUser = direction < 0 ? (_theme.DisplayDirection & 3) : (direction & 3);
        int oldUser = EffectiveRotation;
        int oldPhys = PhysicalRotation;
        _rotationOverride = direction < 0 ? -1 : newUser;
        int newPhys = PhysicalRotation;

        if (newUser == oldUser && newPhys == oldPhys)
            return;

        var (newW, newH) = GetLogicalDimensions(newPhys);
        if (newW != _logicalBitmap.Width || newH != _logicalBitmap.Height)
        {
            _logicalCanvas.Dispose();
            _logicalBitmap.Dispose();
            _logicalBitmap = new SKBitmap(newW, newH, SKColorType.Bgra8888, SKAlphaType.Premul);
            _logicalCanvas = new SKCanvas(_logicalBitmap);

            foreach (var bmp in _staticImages.Values) bmp.Dispose();
            _staticImages.Clear();
            _animFrames.Clear();
            _animScratchBitmap?.Dispose();
            _animScratchBitmap = null;
            _totalAnimDurationMs = 0;

            PreloadAssets(newW, newH);
        }

        _hasRenderedOnce = false;
    }

    public void RenderBlackFrame()
    {
        if (_disposed) return;
        _outputCanvas.Clear(SKColors.Black);
        _hasRenderedOnce = false;
    }

    public byte[]? CopyPreviewPngBytes(int maxHeight = 240)
    {
        if (_disposed || !_hasRenderedOnce)
            return null;

        int srcW = Math.Max(1, _logicalBitmap.Width);
        int srcH = Math.Max(1, _logicalBitmap.Height);
        float scale = Math.Min(1f, (float)maxHeight / Math.Max(srcW, srcH));
        int pw = Math.Max(1, (int)Math.Round(srcW * scale));
        int ph = Math.Max(1, (int)Math.Round(srcH * scale));

        using var previewBmp = new SKBitmap(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var previewCanvas = new SKCanvas(previewBmp);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Low, ColorFilter = _brightnessFilter };
        previewCanvas.DrawBitmap(_logicalBitmap, new SKRect(0, 0, pw, ph), paint);

        using var img = SKImage.FromBitmap(previewBmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 85);
        return data?.ToArray();
    }

    private void LoadFonts()
    {
        foreach (var fontPath in _theme.FontFiles)
        {
            if (!File.Exists(fontPath)) continue;
            try
            {
                var tf = SKTypeface.FromFile(fontPath);
                if (tf != null)
                {
                    if (!string.IsNullOrEmpty(tf.FamilyName))
                        _fonts.TryAdd(tf.FamilyName, tf);
                    _fonts.TryAdd(Path.GetFileNameWithoutExtension(fontPath), tf);
                }
            }
            catch { }
        }
    }

    private void PreloadAssets(int targetW, int targetH)
    {
        var (ox, oy, sx, sy) = ComputeTransform(targetW, targetH);

        foreach (var layer in _theme.ImageLayers)
        {
            if (!File.Exists(layer.FilePath)) continue;

            if ((layer.IsGif || layer.IsVideo) && _animFrames.Count == 0)
            {
                if (layer.IsVideo)
                    PreCropAnimatedVideo(layer, targetW, targetH, ox, oy, sx, sy);
                else
                    PreCropAnimatedGif(layer, targetW, targetH, ox, oy, sx, sy);
            }
            else if (!layer.IsVideo && !_staticImages.ContainsKey(layer.FilePath))
            {
                using var bmp = SKBitmap.Decode(layer.FilePath);
                if (bmp == null) continue;

                float drawW = (layer.Width ?? bmp.Width) * sx;
                float drawH = (layer.Height ?? bmp.Height) * sy;
                int dstW = Math.Max(1, (int)Math.Round(drawW));
                int dstH = Math.Max(1, (int)Math.Round(drawH));

                if (dstW != bmp.Width || dstH != bmp.Height)
                {
                    var resized = bmp.Resize(new SKImageInfo(dstW, dstH, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.Medium);
                    if (resized != null)
                        _staticImages[layer.FilePath] = resized;
                }
                else
                {
                    _staticImages[layer.FilePath] = bmp.Copy();
                }
            }
        }

        foreach (var icon in _theme.StatusIcons)
        {
            if (!File.Exists(icon.FilePath) || _statusIconBitmaps.ContainsKey(icon.FilePath))
                continue;
            using var bmp = SKBitmap.Decode(icon.FilePath);
            if (bmp == null) continue;

            int dstW = Math.Max(1, (int)Math.Round((icon.Width ?? bmp.Width) * sx));
            int dstH = Math.Max(1, (int)Math.Round((icon.Height ?? bmp.Height) * sy));
            if (dstW != bmp.Width || dstH != bmp.Height)
            {
                var resized = bmp.Resize(new SKImageInfo(dstW, dstH, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.Medium);
                if (resized != null)
                    _statusIconBitmaps[icon.FilePath] = resized;
            }
            else
            {
                _statusIconBitmaps[icon.FilePath] = bmp.Copy();
            }
        }

        if (_animFrames.Count == 0 && _theme.FrameSequenceFiles.Count > 1)
            PreloadFrameSequence(targetW, targetH, ox, oy, sx, sy);
    }

    private void PreCropAnimatedVideo(ImageLayerSpec layer, int targetW, int targetH, float ox, float oy, float sx, float sy)
    {
        var fi = new FileInfo(layer.FilePath);
        string cacheKey = $"VID|{layer.FilePath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{layer.X}|{layer.Y}|{layer.Width}|{layer.Height}|{targetW}x{targetH}|{_targetFps}|{ox:F1},{oy:F1}";
        string cachePath = GetSdacCachePath(cacheKey, targetW, targetH);
        LastAnimCachePath = cachePath;

        if (TryLoadSdacCache(cachePath, targetW, targetH))
            return;

        float dstX = ox + layer.X * sx;
        float dstY = oy + layer.Y * sy;
        float dstW = (layer.Width ?? _theme.CanvasWidth) * sx;
        float dstH = (layer.Height ?? _theme.CanvasHeight) * sy;

        _animScratchBitmap = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
        long cumulativeMs = 0;
        int maxFrames = Math.Min(360, _targetFps * 30);

        bool ok = MediaFoundationVideoDecoder.TryDecodeToFrames(
            layer.FilePath,
            targetW,
            targetH,
            dstX,
            dstY,
            dstW,
            dstH,
            _targetFps,
            maxFrames,
            (webpBytes, durMs) =>
            {
                _animFrames.Add(new CachedAnimFrame(webpBytes, cumulativeMs, durMs));
                cumulativeMs += durMs;
            });

        if (ok && _animFrames.Count > 0)
        {
            _totalAnimDurationMs = Math.Max(1, cumulativeMs);
            SaveSdacCache(cachePath);
        }
        else
        {
            _animScratchBitmap.Dispose();
            _animScratchBitmap = null;
        }
    }

    private void PreCropAnimatedGif(ImageLayerSpec layer, int targetW, int targetH, float ox, float oy, float sx, float sy)
    {
        var fi = new FileInfo(layer.FilePath);
        string cacheKey = $"{layer.FilePath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{layer.X}|{layer.Y}|{layer.Width}|{layer.Height}|{targetW}x{targetH}|{_targetFps}|{ox:F1},{oy:F1}";
        string cachePath = GetSdacCachePath(cacheKey, targetW, targetH);
        LastAnimCachePath = cachePath;

        if (TryLoadSdacCache(cachePath, targetW, targetH))
            return;

        using var codec = SKCodec.Create(layer.FilePath);
        if (codec == null || codec.FrameCount <= 0)
            return;

        int srcW = codec.Info.Width;
        int srcH = codec.Info.Height;
        int count = codec.FrameCount;

        float dstX = ox + layer.X * sx;
        float dstY = oy + layer.Y * sy;
        float dstW = (layer.Width ?? srcW) * sx;
        float dstH = (layer.Height ?? srcH) * sy;
        var destRect = new SKRect(dstX, dstY, dstX + dstW, dstY + dstH);

        var infos = codec.FrameInfo;
        double totalSrcMs = 0;
        for (int i = 0; i < count; i++)
            totalSrcMs += Math.Max(20, infos[i].Duration);

        double avgSrcFps = count / Math.Max(0.1, totalSrcMs / 1000.0);
        int step = Math.Max(1, (int)Math.Round(avgSrcFps / _targetFps));

        using var decodeBmp = new SKBitmap(srcW, srcH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var cropBmp = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var cropCanvas = new SKCanvas(cropBmp);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Low, IsAntialias = false };

        _animScratchBitmap = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);

        long cumulativeMs = 0;
        int accDurMs = 0;
        for (int i = 0; i < count; i++)
        {
            var opts = new SKCodecOptions(i, i > 0 ? i - 1 : -1);
            var res = codec.GetPixels(decodeBmp.Info, decodeBmp.GetPixels(), opts);
            if (res != SKCodecResult.Success && res != SKCodecResult.IncompleteInput)
                continue;

            accDurMs += Math.Max(20, infos[i].Duration);
            if (i % step != 0)
                continue;

            int durMs = accDurMs;
            accDurMs = 0;

            cropCanvas.Clear(SKColors.Black);
            cropCanvas.DrawBitmap(decodeBmp, destRect, paint);

            using var img = SKImage.FromBitmap(cropBmp);
            using var data = img.Encode(SKEncodedImageFormat.Webp, 82);
            _animFrames.Add(new CachedAnimFrame(data.ToArray(), cumulativeMs, durMs));

            cumulativeMs += durMs;
        }

        _totalAnimDurationMs = Math.Max(1, cumulativeMs);
        SaveSdacCache(cachePath);
    }

    private void PreloadFrameSequence(int targetW, int targetH, float ox, float oy, float sx, float sy)
    {
        var files = _theme.FrameSequenceFiles;
        string cacheKey = $"SEQ|{files[0]}|{files.Count}|{targetW}x{targetH}|{_targetFps}|{ox:F1},{oy:F1},{sx:F3},{sy:F3}";
        string cachePath = GetSdacCachePath(cacheKey, targetW, targetH);
        LastAnimCachePath = cachePath;

        if (TryLoadSdacCache(cachePath, targetW, targetH))
            return;

        _animScratchBitmap = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
        int durMs = Math.Max(33, 1000 / _targetFps);
        long cumulativeMs = 0;
        var destRect = new SKRect(ox, oy, ox + _theme.CanvasWidth * sx, oy + _theme.CanvasHeight * sy);

        using var cropBmp = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var cropCanvas = new SKCanvas(cropBmp);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium };

        foreach (var file in files)
        {
            using var bmp = SKBitmap.Decode(file);
            if (bmp == null) continue;

            cropCanvas.Clear(SKColors.Black);
            cropCanvas.DrawBitmap(bmp, destRect, paint);

            using var img = SKImage.FromBitmap(cropBmp);
            using var data = img.Encode(SKEncodedImageFormat.Webp, 82);
            _animFrames.Add(new CachedAnimFrame(data.ToArray(), cumulativeMs, durMs));
            cumulativeMs += durMs;
        }

        _totalAnimDurationMs = Math.Max(1, cumulativeMs);
        SaveSdacCache(cachePath);
    }

    private string GetSdacCachePath(string cacheKey, int targetW, int targetH)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))[..16];
        return Path.Combine(_animCacheRoot, $"{hash}_{targetW}x{targetH}_{_targetFps}fps.sdac");
    }

    private bool TryLoadSdacCache(string cachePath, int targetW, int targetH)
    {
        if (!File.Exists(cachePath))
            return false;

        try
        {
            using var fs = File.OpenRead(cachePath);
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);
            if (br.ReadUInt32() != SdacMagic || br.ReadInt32() != SdacVersion)
                return false;

            int frameCount = br.ReadInt32();
            if (frameCount <= 0 || frameCount > 5000)
                return false;

            _animScratchBitmap = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
            long cumulativeMs = 0;
            for (int i = 0; i < frameCount; i++)
            {
                int durMs = br.ReadInt32();
                int len = br.ReadInt32();
                if (len <= 0 || len > 16 * 1024 * 1024)
                {
                    _animFrames.Clear();
                    return false;
                }
                byte[] bytes = br.ReadBytes(len);
                _animFrames.Add(new CachedAnimFrame(bytes, cumulativeMs, durMs));
                cumulativeMs += durMs;
            }
            _totalAnimDurationMs = Math.Max(1, cumulativeMs);
            return _animFrames.Count > 0;
        }
        catch
        {
            _animFrames.Clear();
            return false;
        }
    }

    private void SaveSdacCache(string cachePath)
    {
        if (_animFrames.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            string tmp = cachePath + ".tmp";
            using (var fs = File.Create(tmp))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
            {
                bw.Write(SdacMagic);
                bw.Write(SdacVersion);
                bw.Write(_animFrames.Count);
                foreach (var f in _animFrames)
                {
                    bw.Write(f.DurationMs);
                    bw.Write(f.WebpBytes.Length);
                    bw.Write(f.WebpBytes);
                }
            }
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch { }
    }

    public bool RenderIfDirty(SensorSnapshot sensors, long elapsedMs)
    {
        if (_disposed)
            return false;

        int animIdx = SelectAnimationFrameIndex(elapsedMs);
        ulong hash = ComputeStateHash(sensors, animIdx, elapsedMs);

        if (_hasRenderedOnce && hash == _lastRenderHash)
            return false;

        _lastRenderHash = hash;
        _lastAnimIndex = animIdx;
        _hasRenderedOnce = true;

        int rot = PhysicalRotation;
        int logW = _logicalBitmap.Width;
        int logH = _logicalBitmap.Height;
        var (ox, oy, sx, sy) = ComputeTransform(logW, logH);
        float minScale = Math.Min(sx, sy);

        _logicalCanvas.Clear(SKColors.Black);

        bool drewAnim = false;
        foreach (var layer in _theme.ImageLayers)
        {
            if ((layer.IsGif || layer.IsVideo) && _animFrames.Count > 0)
            {
                if (!drewAnim)
                {
                    DrawAnimFrame(animIdx);
                    drewAnim = true;
                }
                continue;
            }

            if (_staticImages.TryGetValue(layer.FilePath, out var bmp))
                _logicalCanvas.DrawBitmap(bmp, ox + layer.X * sx, oy + layer.Y * sy);
        }

        if (!drewAnim && _animFrames.Count > 0)
            DrawAnimFrame(animIdx);

        foreach (var bar in _theme.BarWidgets)
        {
            float bx = ox + bar.X * sx;
            float by = oy + bar.Y * sy;
            float bw = bar.MaxWidth * sx;
            float bh = bar.MaxHeight * sy;
            float rad = bar.CornerRadius * minScale;

            if (bar.IsStaticFill)
            {
                if (bar.FillColor != SKColor.Empty && bar.FillColor.Alpha > 0)
                {
                    _barPaint.Color = bar.FillColor;
                    _logicalCanvas.DrawRoundRect(bx, by, bw, bh, rad, rad, _barPaint);
                }
                if (bar.BorderThickness > 0 && bar.BorderColor != SKColor.Empty && bar.BorderColor.Alpha > 0)
                {
                    _barStrokePaint.Color = bar.BorderColor;
                    _barStrokePaint.StrokeWidth = Math.Max(1f, bar.BorderThickness * minScale);
                    _logicalCanvas.DrawRoundRect(bx, by, bw, bh, rad, rad, _barStrokePaint);
                }
                continue;
            }

            if (bar.BackColor != SKColor.Empty && bar.BackColor.Alpha > 0)
            {
                _barPaint.Color = bar.BackColor;
                _logicalCanvas.DrawRoundRect(bx, by, bw, bh, rad, rad, _barPaint);
            }

            double val = sensors.GetNumericValue(bar.SensorKey);
            double range = Math.Max(1.0, bar.MaxNum - bar.MinNum);
            float ratio = (float)Math.Clamp((val - bar.MinNum) / range, 0.0, 1.0);
            if (ratio > 0.001f)
            {
                _barPaint.Color = bar.FillColor;
                if (bar.IsSegmented)
                {
                    int segments = Math.Clamp(bar.SegmentCount, 2, 50);
                    bool vertical = !bar.ForceHorizontal && bh > bw;
                    float gap = Math.Max(1f, bar.SegmentGap * minScale);
                    int active = Math.Max(1, (int)Math.Round(ratio * segments));
                    if (vertical)
                    {
                        float segH = Math.Max(2f, (bh - gap * (segments - 1)) / segments);
                        for (int s = 0; s < active; s++)
                        {
                            float syPos = by + bh - ((s + 1) * segH + s * gap);
                            _logicalCanvas.DrawRoundRect(bx, syPos, bw, segH, rad, rad, _barPaint);
                        }
                    }
                    else
                    {
                        float segW = Math.Max(2f, (bw - gap * (segments - 1)) / segments);
                        for (int s = 0; s < active; s++)
                        {
                            float sxPos = bx + s * (segW + gap);
                            _logicalCanvas.DrawRoundRect(sxPos, by, segW, bh, rad, rad, _barPaint);
                        }
                    }
                }
                else if (bh > bw)
                {
                    float fillH = bh * ratio;
                    float yPos = bar.ReverseDirection ? by : by + (bh - fillH);
                    _logicalCanvas.DrawRoundRect(bx, yPos, bw, fillH, rad, rad, _barPaint);
                }
                else
                {
                    float fillW = bw * ratio;
                    float xPos = bar.ReverseDirection ? bx + (bw - fillW) : bx;
                    _logicalCanvas.DrawRoundRect(xPos, by, fillW, bh, rad, rad, _barPaint);
                }
            }

            if (bar.BorderThickness > 0 && bar.BorderColor != SKColor.Empty && bar.BorderColor.Alpha > 0)
            {
                _barStrokePaint.Color = bar.BorderColor;
                _barStrokePaint.StrokeWidth = Math.Max(1f, bar.BorderThickness * minScale);
                _logicalCanvas.DrawRoundRect(bx, by, bw, bh, rad, rad, _barStrokePaint);
            }
        }

        foreach (var radW in _theme.RadialWidgets)
        {
            float cx = ox + radW.Cx * sx;
            float cy = oy + radW.Cy * sy;
            float r = Math.Max(4f, radW.Radius * minScale);
            float thick = Math.Max(2f, radW.Thickness * minScale);
            var rect = new SKRect(cx - r, cy - r, cx + r, cy + r);

            _radialPaint.StrokeWidth = thick;
            float rawDelta = radW.EndAngle - radW.StartAngle;
            float sweepTotal = Math.Abs(rawDelta) < 1f
                ? (radW.Clockwise ? 360f : -360f)
                : (radW.Clockwise ? Math.Abs(rawDelta) : -Math.Abs(rawDelta));

            if (radW.BackColor != SKColor.Empty && radW.BackColor.Alpha > 0)
            {
                _radialPaint.Color = radW.BackColor;
                _logicalCanvas.DrawArc(rect, radW.StartAngle, sweepTotal, false, _radialPaint);
            }

            double val = sensors.GetNumericValue(radW.SensorKey);
            float ratio = (float)Math.Clamp((val - radW.MinValue) / Math.Max(1.0, radW.MaxValue - radW.MinValue), 0.0, 1.0);
            if (ratio > 0.001f)
            {
                _radialPaint.Color = radW.FillColor;
                _logicalCanvas.DrawArc(rect, radW.StartAngle, sweepTotal * ratio, false, _radialPaint);
            }

            if (radW.ShowText)
            {
                _textPaint.Typeface = ResolveTypeface(radW.FontFamily);
                _textPaint.TextSize = Math.Max(8f, radW.FontSize * minScale);
                _textPaint.TextAlign = SKTextAlign.Center;
                _textPaint.Color = radW.TextColor;
                string label = sensors.FormatValue(radW.SensorKey, radW.Unit);
                float txCenter = cx + radW.TextOffsetX * sx;
                float tyCenter = cy + radW.TextOffsetY * sy - (_textPaint.FontMetrics.Ascent + _textPaint.FontMetrics.Descent) * 0.5f;
                _logicalCanvas.DrawText(label, txCenter, tyCenter, _textPaint);
            }
        }

        for (int gi = 0; gi < _theme.GraphWidgets.Count && gi < _graphBuffers.Count; gi++)
        {
            var g = _theme.GraphWidgets[gi];
            var buf = _graphBuffers[gi];
            double val = sensors.GetNumericValue(g.SensorKey);
            buf.PushIfDue(val, elapsedMs);

            float gx = ox + g.X * sx;
            float gy = oy + g.Y * sy;
            float gw = Math.Max(10f, g.Width * sx);
            float gh = Math.Max(8f, g.Height * sy);

            if (g.BackColor != SKColor.Empty && g.BackColor.Alpha > 0)
            {
                _barPaint.Color = g.BackColor;
                _logicalCanvas.DrawRoundRect(gx, gy, gw, gh, 4f * minScale, 4f * minScale, _barPaint);
            }

            if (buf.Count >= 1)
            {
                double span = Math.Max(1.0, g.MaxValue - g.MinValue);
                _graphPath.Reset();
                int n = buf.Count;
                float stepX = n > 1 ? gw / (n - 1) : gw;
                for (int i = 0; i < n; i++)
                {
                    float norm = (float)Math.Clamp((buf.GetSample(i) - g.MinValue) / span, 0.0, 1.0);
                    float px = gx + i * stepX;
                    float py = gy + gh - (norm * (gh - 2f));
                    if (i == 0) _graphPath.MoveTo(px, py);
                    else _graphPath.LineTo(px, py);
                }

                if (g.FillColor != SKColor.Empty && g.FillColor.Alpha > 0 && n > 1)
                {
                    using var fillPath = new SKPath(_graphPath);
                    fillPath.LineTo(gx + (n - 1) * stepX, gy + gh);
                    fillPath.LineTo(gx, gy + gh);
                    fillPath.Close();
                    _barPaint.Color = g.FillColor;
                    _logicalCanvas.DrawPath(fillPath, _barPaint);
                }

                _graphPaint.Color = g.LineColor;
                _graphPaint.StrokeWidth = Math.Max(1f, g.LineThickness * minScale);
                _logicalCanvas.DrawPath(_graphPath, _graphPaint);
            }
        }

        foreach (var icon in _theme.StatusIcons)
        {
            if (!_statusIconBitmaps.TryGetValue(icon.FilePath, out var bmp))
                continue;
            _iconPaint.Color = sensors.GetBoolState(icon.SensorKey)
                ? SKColors.White
                : new SKColor(255, 255, 255, 70);
            _logicalCanvas.DrawBitmap(bmp, ox + icon.X * sx, oy + icon.Y * sy, _iconPaint);
        }

        foreach (var av in _theme.AudioVisualizers)
        {
            float vx = ox + av.X * sx;
            float vy = oy + av.Y * sy;
            float vw = Math.Max(12f, av.Width * sx);
            float vh = Math.Max(8f, av.Height * sy);
            float bandW = Math.Max(2f, av.BandWidth * sx);
            float gap = Math.Max(1.5f, bandW * 0.35f);
            int count = Math.Clamp((int)(vw / (bandW + gap)), 4, 48);
            float peak = Math.Clamp(sensors.AudioPeak, 0f, 1f);
            double phase = peak > 0.01f ? elapsedMs * 0.008 : 0.0;
            var bands = sensors.AudioBands;

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                float level;
                if (bands is { Length: > 0 })
                {
                    int bIdx = Math.Min(bands.Length - 1, (int)(t * bands.Length));
                    level = Math.Max(0.06f, Math.Clamp(bands[bIdx], 0f, 1f));
                }
                else
                {
                    double wave = 0.5 + 0.35 * Math.Sin(phase + i * 0.65) + 0.15 * Math.Cos(phase * 1.4 - i * 1.1);
                    level = peak > 0.01f ? Math.Clamp((float)(0.08 + peak * wave * 0.92), 0.06f, 1f) : 0.06f;
                }

                float bh = Math.Max(2f, vh * level);
                float bx = vx + i * (bandW + gap);
                float by = vy + vh - bh;

                byte r = (byte)Math.Round(av.PrimaryColor.Red + (av.SecondaryColor.Red - av.PrimaryColor.Red) * t);
                byte g = (byte)Math.Round(av.PrimaryColor.Green + (av.SecondaryColor.Green - av.PrimaryColor.Green) * t);
                byte b = (byte)Math.Round(av.PrimaryColor.Blue + (av.SecondaryColor.Blue - av.PrimaryColor.Blue) * t);
                byte a = (byte)Math.Round(av.PrimaryColor.Alpha + (av.SecondaryColor.Alpha - av.PrimaryColor.Alpha) * t);
                _barPaint.Color = new SKColor(r, g, b, a);
                _logicalCanvas.DrawRoundRect(bx, by, bandW, bh, bandW * 0.35f, bandW * 0.35f, _barPaint);
            }
        }

        foreach (var art in _theme.AlbumArts)
        {
            float ax = ox + art.X * sx;
            float ay = oy + art.Y * sy;
            float aw = Math.Max(12f, art.Width * sx);
            float ah = Math.Max(12f, art.Height * sy);
            float rad = art.IsVinylSpin ? Math.Min(aw, ah) * 0.5f : art.CornerRadius * minScale;
            var rect = new SKRect(ax, ay, ax + aw, ay + ah);

            var artBmp = ResolveAlbumArtBitmap(sensors.AlbumArtBytes);
            if (artBmp != null)
            {
                _logicalCanvas.Save();
                using var clipPath = new SKPath();
                clipPath.AddRoundRect(rect, rad, rad);
                _logicalCanvas.ClipPath(clipPath, antialias: true);
                if (art.IsVinylSpin)
                {
                    float cx = ax + aw * 0.5f;
                    float cy = ay + ah * 0.5f;
                    _logicalCanvas.RotateDegrees((elapsedMs * 0.03f) % 360f, cx, cy);
                }
                _logicalCanvas.DrawBitmap(artBmp, rect, _iconPaint);
                _logicalCanvas.Restore();
            }
            else
            {
                _barPaint.Color = new SKColor(32, 28, 44, 180);
                _logicalCanvas.DrawRoundRect(rect, rad, rad, _barPaint);
            }
        }

        foreach (var txt in _theme.TextWidgets)
        {
            string value = txt.IsStaticText
                ? (txt.StaticContent ?? txt.SensorKey)
                : (string.IsNullOrEmpty(txt.Prefix)
                    ? sensors.FormatValue(txt.SensorKey, txt.Unit)
                    : txt.Prefix + sensors.FormatValue(txt.SensorKey, txt.Unit));

            _textPaint.Typeface = ResolveTypeface(txt.FontFamily, txt.IsBold);
            _textPaint.TextSize = Math.Max(6f, txt.FontSize * minScale);
            byte alpha = (byte)Math.Clamp(Math.Round(txt.Color.Alpha * txt.Opacity), 0, 255);
            _textPaint.Color = txt.Color.WithAlpha(alpha);

            string anchor = string.IsNullOrEmpty(txt.Anchor) ? "la" : txt.Anchor.ToLowerInvariant();
            char ah = anchor.Length >= 1 ? anchor[0] : 'l';
            char av = anchor.Length >= 2 ? anchor[1] : 'a';

            float tx;
            float ty;
            if (txt.BoxWidth > 0 && txt.BoxHeight > 0)
            {
                float left = ox + txt.X * sx;
                float top = oy + txt.Y * sy;
                float right = ox + (txt.X + txt.BoxWidth) * sx;
                float bottom = oy + (txt.Y + txt.BoxHeight) * sy;

                (tx, _textPaint.TextAlign) = ah switch
                {
                    'm' => ((left + right) * 0.5f, SKTextAlign.Center),
                    'r' => (right, SKTextAlign.Right),
                    _ => (left, SKTextAlign.Left)
                };

                ty = av switch
                {
                    'm' => (top + bottom) * 0.5f - (_textPaint.FontMetrics.Ascent + _textPaint.FontMetrics.Descent) * 0.5f,
                    'b' or 's' or 'd' => bottom - _textPaint.FontMetrics.Descent,
                    _ => top - _textPaint.FontMetrics.Ascent
                };
            }
            else
            {
                tx = ox + txt.X * sx;
                _textPaint.TextAlign = ah switch
                {
                    'm' => SKTextAlign.Center,
                    'r' => SKTextAlign.Right,
                    _ => txt.Align switch
                    {
                        TextAlignMode.Center => SKTextAlign.Center,
                        TextAlignMode.Right => SKTextAlign.Right,
                        _ => SKTextAlign.Left
                    }
                };

                float rawY = oy + txt.Y * sy;
                ty = av switch
                {
                    'm' => rawY - (_textPaint.FontMetrics.Ascent + _textPaint.FontMetrics.Descent) * 0.5f,
                    'b' or 's' or 'd' => rawY - _textPaint.FontMetrics.Descent,
                    _ => rawY - _textPaint.FontMetrics.Ascent
                };
            }

            _logicalCanvas.DrawText(value, tx, ty, _textPaint);
        }

        UpdateBrightnessFilter();
        _blitPaint.ColorFilter = _brightnessFilter;

        _outputCanvas.Save();
        _outputCanvas.Clear(SKColors.Black);
        switch (rot)
        {
            case 1:
                _outputCanvas.Translate(_panelWidth, 0);
                _outputCanvas.RotateDegrees(90);
                break;
            case 2:
                _outputCanvas.Translate(_panelWidth, _panelHeight);
                _outputCanvas.RotateDegrees(180);
                break;
            case 3:
                _outputCanvas.Translate(0, _panelHeight);
                _outputCanvas.RotateDegrees(270);
                break;
        }
        _outputCanvas.DrawBitmap(_logicalBitmap, 0, 0, _blitPaint);
        _outputCanvas.Restore();

        return true;
    }

    private void DrawAnimFrame(int idx)
    {
        if (idx < 0 || idx >= _animFrames.Count || _animScratchBitmap == null)
            return;

        var bytes = _animFrames[idx].WebpBytes;
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                using var data = SKData.Create((IntPtr)ptr, bytes.Length);
                using var codec = SKCodec.Create(data);
                if (codec != null)
                {
                    codec.GetPixels(_animScratchBitmap.Info, _animScratchBitmap.GetPixels());
                    _logicalCanvas.DrawBitmap(_animScratchBitmap, 0, 0);
                }
            }
        }
    }

    private int SelectAnimationFrameIndex(long elapsedMs)
    {
        if (_animFrames.Count <= 1 || _totalAnimDurationMs <= 0)
            return 0;

        long t = elapsedMs % _totalAnimDurationMs;
        int start = (_lastAnimIndex >= 0 && _lastAnimIndex < _animFrames.Count) ? _lastAnimIndex : 0;
        if (t >= _animFrames[start].StartMs && t < _animFrames[start].StartMs + _animFrames[start].DurationMs)
            return start;

        int lo = 0, hi = _animFrames.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var f = _animFrames[mid];
            if (t < f.StartMs) hi = mid - 1;
            else if (t >= f.StartMs + f.DurationMs) lo = mid + 1;
            else return mid;
        }
        return 0;
    }

    private ulong ComputeStateHash(SensorSnapshot sensors, int animIdx, long elapsedMs)
    {
        ulong h = 14695981039346656037UL;
        h = FnvAdd(h, (uint)animIdx);
        h = FnvAdd(h, (uint)_brightness);
        h = FnvAdd(h, (uint)PhysicalRotation);

        foreach (var txt in _theme.TextWidgets)
        {
            if (txt.IsStaticText) continue;
            h = FnvAddString(h, sensors.FormatValue(txt.SensorKey, txt.Unit));
        }

        foreach (var bar in _theme.BarWidgets)
        {
            if (bar.IsStaticFill) continue;
            h = FnvAdd(h, (uint)(int)Math.Round(sensors.GetNumericValue(bar.SensorKey) * 10.0));
        }

        foreach (var r in _theme.RadialWidgets)
            h = FnvAdd(h, (uint)(int)Math.Round(sensors.GetNumericValue(r.SensorKey) * 10.0));

        foreach (var g in _theme.GraphWidgets)
            h = FnvAdd(h, (uint)(int)Math.Round(sensors.GetNumericValue(g.SensorKey) * 10.0));

        foreach (var icon in _theme.StatusIcons)
            h = FnvAdd(h, sensors.GetBoolState(icon.SensorKey) ? 1u : 0u);

        if (_theme.AudioVisualizers.Count > 0)
        {
            h = FnvAdd(h, (uint)(int)Math.Round(sensors.AudioPeak * 100f));
            if (sensors.AudioBands is { Length: > 0 } bands)
            {
                for (int i = 0; i < bands.Length; i++)
                    h = FnvAdd(h, (uint)(int)Math.Round(bands[i] * 100f));
            }
            if (sensors.AudioPeak > 0.01f)
                h = FnvAdd(h, (uint)(elapsedMs / 66));
        }

        if (_theme.AlbumArts.Count > 0)
        {
            h = FnvAdd(h, (uint)ComputeBytesHash(sensors.AlbumArtBytes));
            if (sensors.AlbumArtBytes is { Length: > 0 } && _theme.AlbumArts.Any(a => a.IsVinylSpin))
                h = FnvAdd(h, (uint)(elapsedMs / 100));
        }

        return h;
    }

    private SKBitmap? ResolveAlbumArtBitmap(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            _albumArtBitmap?.Dispose();
            _albumArtBitmap = null;
            _lastAlbumArtHash = 0;
            return null;
        }

        ulong hash = ComputeBytesHash(bytes);
        if (hash == _lastAlbumArtHash && _albumArtBitmap != null)
            return _albumArtBitmap;

        _albumArtBitmap?.Dispose();
        _albumArtBitmap = SKBitmap.Decode(bytes);
        _lastAlbumArtHash = hash;
        return _albumArtBitmap;
    }

    private static ulong ComputeBytesHash(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return 0;
        ulong h = 14695981039346656037UL ^ (uint)bytes.Length;
        int step = Math.Max(1, bytes.Length / 32);
        for (int i = 0; i < bytes.Length; i += step)
        {
            h ^= bytes[i];
            h *= 1099511628211UL;
        }
        return h;
    }

    private static ulong FnvAdd(ulong h, uint v)
    {
        h ^= v;
        return h * 1099511628211UL;
    }

    private static ulong FnvAddString(ulong h, string s)
    {
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        h ^= 0xFF;
        return h * 1099511628211UL;
    }

    private SKTypeface ResolveTypeface(string family, bool isBold = false)
    {
        if (!string.IsNullOrEmpty(family))
        {
            if (_fonts.TryGetValue(family, out var tfExact))
                return tfExact;

            string shortName = Path.GetFileNameWithoutExtension(family.Replace('\\', '/'));
            if (!string.IsNullOrEmpty(shortName) && _fonts.TryGetValue(shortName, out var tfShort))
                return tfShort;

            foreach (var kv in _fonts)
            {
                if ((!string.IsNullOrEmpty(shortName) && kv.Key.Contains(shortName, StringComparison.OrdinalIgnoreCase)) ||
                    kv.Key.Contains(family, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
        }

        if (_fonts.Count > 0)
            return _fonts.Values.First();

        bool bold = isBold ||
                    family.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                    family.Contains("Black", StringComparison.OrdinalIgnoreCase);

        if (family.Contains("racespace", StringComparison.OrdinalIgnoreCase) ||
            family.Contains("geforce", StringComparison.OrdinalIgnoreCase) ||
            family.Contains("digital", StringComparison.OrdinalIgnoreCase))
        {
            return SKTypeface.FromFamilyName("Bahnschrift", bold ? SKFontStyle.Bold : SKFontStyle.Normal)
                   ?? SKTypeface.Default;
        }

        if (family.Contains("mono", StringComparison.OrdinalIgnoreCase))
        {
            return SKTypeface.FromFamilyName("Consolas", bold ? SKFontStyle.Bold : SKFontStyle.Normal)
                   ?? SKTypeface.Default;
        }

        return bold
            ? SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default
            : SKTypeface.Default;
    }

    private void UpdateBrightnessFilter()
    {
        if (_cachedFilterBrightness == _brightness) return;

        _brightnessFilter?.Dispose();
        _brightnessFilter = null;
        _cachedFilterBrightness = _brightness;

        if (_brightness < 100)
        {
            float scale = _brightness / 100f;
            float[] matrix =
            [
                scale, 0, 0, 0, 0,
                0, scale, 0, 0, 0,
                0, 0, scale, 0, 0,
                0, 0, 0, 1, 0
            ];
            _brightnessFilter = SKColorFilter.CreateColorMatrix(matrix);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _outputCanvas.Dispose();
        _outputBitmap.Dispose();
        _logicalCanvas.Dispose();
        _logicalBitmap.Dispose();
        _animScratchBitmap?.Dispose();
        _albumArtBitmap?.Dispose();
        _brightnessFilter?.Dispose();
        _textPaint.Dispose();
        _barPaint.Dispose();
        _barStrokePaint.Dispose();
        _iconPaint.Dispose();
        _radialPaint.Dispose();
        _graphPaint.Dispose();
        _graphPath.Dispose();
        _blitPaint.Dispose();

        foreach (var bmp in _staticImages.Values) bmp.Dispose();
        _staticImages.Clear();

        foreach (var bmp in _statusIconBitmaps.Values) bmp.Dispose();
        _statusIconBitmaps.Clear();

        foreach (var tf in _fonts.Values.Distinct()) tf.Dispose();
        _fonts.Clear();

        _animFrames.Clear();
    }

    private sealed record CachedAnimFrame(byte[] WebpBytes, long StartMs, int DurationMs);

    private sealed class GraphHistoryBuffer
    {
        private readonly float[] _ring;
        private int _head;
        private int _count;
        private long _lastPushMs = -10000;

        public int Count => _count;

        public GraphHistoryBuffer(int capacity)
        {
            _ring = new float[Math.Max(2, capacity)];
        }

        public void PushIfDue(double value, long elapsedMs)
        {
            if (_count > 0 && elapsedMs - _lastPushMs < 1000)
                return;
            _lastPushMs = elapsedMs;
            _ring[_head] = (float)value;
            _head = (_head + 1) % _ring.Length;
            if (_count < _ring.Length)
                _count++;
        }

        public float GetSample(int index)
        {
            int start = _count < _ring.Length ? 0 : _head;
            return _ring[(start + index) % _ring.Length];
        }
    }
}
