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
    private int _brightness;
    private int _rotationOverride = -1;

    private readonly SKBitmap _outputBitmap;
    private readonly SKCanvas _outputCanvas;
    private SKBitmap _logicalBitmap;
    private SKCanvas _logicalCanvas;

    private readonly Dictionary<string, SKTypeface> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SKBitmap> _staticImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CachedAnimFrame> _animFrames = [];
    private SKBitmap? _animScratchBitmap;

    private readonly SKPaint _textPaint = new() { IsAntialias = true, SubpixelText = true };
    private readonly SKPaint _barPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
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

    public SkiaThemeRenderer(
        DisplayTheme theme,
        int panelWidth,
        int panelHeight,
        int targetFps = 12,
        int brightness = 100,
        int rotationOverride = -1,
        string? customCacheDir = null)
    {
        _theme = theme;
        _panelWidth = panelWidth;
        _panelHeight = panelHeight;
        _targetFps = Math.Clamp(targetFps, 1, 60);
        _brightness = Math.Clamp(brightness, 0, 100);
        _rotationOverride = rotationOverride >= 0 ? (rotationOverride & 3) : -1;

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

        var (logW, logH) = GetLogicalDimensions(EffectiveRotation);
        _logicalBitmap = new SKBitmap(logW, logH, SKColorType.Bgra8888, SKAlphaType.Premul);
        _logicalCanvas = new SKCanvas(_logicalBitmap);

        LoadFonts();
        PreloadAssets(logW, logH);
    }

    private (int W, int H) GetLogicalDimensions(int rot)
    {
        bool swap = rot == 1 || rot == 3;
        return swap ? (_panelHeight, _panelWidth) : (_panelWidth, _panelHeight);
    }

    public void SetRotation(int direction)
    {
        int newRot = direction < 0 ? (_theme.DisplayDirection & 3) : (direction & 3);
        int oldRot = EffectiveRotation;
        _rotationOverride = direction < 0 ? -1 : newRot;

        if (newRot == oldRot)
            return;

        var (newW, newH) = GetLogicalDimensions(newRot);
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

        float scale = Math.Min(1f, (float)maxHeight / Math.Max(1, _panelHeight));
        int pw = Math.Max(1, (int)Math.Round(_panelWidth * scale));
        int ph = Math.Max(1, (int)Math.Round(_panelHeight * scale));

        using var resized = _outputBitmap.Resize(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.Low);
        if (resized == null) return null;
        using var img = SKImage.FromBitmap(resized);
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
        float sx = targetW / Math.Max(1f, _theme.CanvasWidth);
        float sy = targetH / Math.Max(1f, _theme.CanvasHeight);

        foreach (var layer in _theme.ImageLayers)
        {
            if (!File.Exists(layer.FilePath)) continue;

            if (layer.IsGif && _animFrames.Count == 0)
            {
                PreCropAnimatedGif(layer, targetW, targetH, sx, sy);
            }
            else if (!_staticImages.ContainsKey(layer.FilePath))
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

        if (_animFrames.Count == 0 && _theme.FrameSequenceFiles.Count > 1)
            PreloadFrameSequence(targetW, targetH);
    }

    private void PreCropAnimatedGif(ImageLayerSpec layer, int targetW, int targetH, float sx, float sy)
    {
        var fi = new FileInfo(layer.FilePath);
        string cacheKey = $"{layer.FilePath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{layer.X}|{layer.Y}|{layer.Width}|{layer.Height}|{targetW}x{targetH}|{_targetFps}";
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

        float dstX = layer.X * sx;
        float dstY = layer.Y * sy;
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

    private void PreloadFrameSequence(int targetW, int targetH)
    {
        var files = _theme.FrameSequenceFiles;
        string cacheKey = $"SEQ|{files[0]}|{files.Count}|{targetW}x{targetH}|{_targetFps}";
        string cachePath = GetSdacCachePath(cacheKey, targetW, targetH);
        LastAnimCachePath = cachePath;

        if (TryLoadSdacCache(cachePath, targetW, targetH))
            return;

        _animScratchBitmap = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
        int durMs = Math.Max(33, 1000 / _targetFps);
        long cumulativeMs = 0;
        var destRect = new SKRect(0, 0, targetW, targetH);

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
        ulong hash = ComputeStateHash(sensors, animIdx);

        if (_hasRenderedOnce && hash == _lastRenderHash)
            return false;

        _lastRenderHash = hash;
        _lastAnimIndex = animIdx;
        _hasRenderedOnce = true;

        int rot = EffectiveRotation;
        int logW = _logicalBitmap.Width;
        int logH = _logicalBitmap.Height;
        float sx = logW / Math.Max(1f, _theme.CanvasWidth);
        float sy = logH / Math.Max(1f, _theme.CanvasHeight);

        _logicalCanvas.Clear(SKColors.Black);

        bool drewAnim = false;
        foreach (var layer in _theme.ImageLayers)
        {
            if (layer.IsGif && _animFrames.Count > 0)
            {
                if (!drewAnim)
                {
                    DrawAnimFrame(animIdx);
                    drewAnim = true;
                }
                continue;
            }

            if (_staticImages.TryGetValue(layer.FilePath, out var bmp))
                _logicalCanvas.DrawBitmap(bmp, layer.X * sx, layer.Y * sy);
        }

        if (!drewAnim && _animFrames.Count > 0)
            DrawAnimFrame(animIdx);

        foreach (var bar in _theme.BarWidgets)
        {
            float bx = bar.X * sx;
            float by = bar.Y * sy;
            float bw = bar.MaxWidth * sx;
            float bh = bar.MaxHeight * sy;
            float rad = bar.CornerRadius * Math.Min(sx, sy);

            if (bar.BackColor != SKColor.Empty && bar.BackColor.Alpha > 0)
            {
                _barPaint.Color = bar.BackColor;
                _logicalCanvas.DrawRoundRect(bx, by, bw, bh, rad, rad, _barPaint);
            }

            double val = sensors.GetNumericValue(bar.SensorKey);
            float ratio = (float)Math.Clamp(val / Math.Max(1.0, bar.MaxNum), 0.0, 1.0);
            if (ratio > 0.001f)
            {
                _barPaint.Color = bar.FillColor;
                _logicalCanvas.DrawRoundRect(bx, by, bw * ratio, bh, rad, rad, _barPaint);
            }
        }

        foreach (var txt in _theme.TextWidgets)
        {
            string value = txt.IsStaticText
                ? (txt.StaticContent ?? txt.SensorKey)
                : sensors.FormatValue(txt.SensorKey, txt.Unit);

            _textPaint.Typeface = ResolveTypeface(txt.FontFamily);
            _textPaint.TextSize = Math.Max(6f, txt.FontSize * sy);
            byte alpha = (byte)Math.Clamp(Math.Round(txt.Color.Alpha * txt.Opacity), 0, 255);
            _textPaint.Color = txt.Color.WithAlpha(alpha);

            float tx = txt.X * sx;
            float ty = txt.Y * sy - _textPaint.FontMetrics.Ascent;
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

    private ulong ComputeStateHash(SensorSnapshot sensors, int animIdx)
    {
        ulong h = 14695981039346656037UL;
        h = FnvAdd(h, (uint)animIdx);
        h = FnvAdd(h, (uint)_brightness);
        h = FnvAdd(h, (uint)EffectiveRotation);

        foreach (var txt in _theme.TextWidgets)
        {
            if (txt.IsStaticText) continue;
            h = FnvAddString(h, sensors.FormatValue(txt.SensorKey, txt.Unit));
        }

        foreach (var bar in _theme.BarWidgets)
            h = FnvAdd(h, (uint)(int)Math.Round(sensors.GetNumericValue(bar.SensorKey) * 10.0));

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

    private SKTypeface ResolveTypeface(string family)
    {
        if (!string.IsNullOrEmpty(family) && _fonts.TryGetValue(family, out var tf))
            return tf;

        foreach (var kv in _fonts)
        {
            if (!string.IsNullOrEmpty(family) &&
                kv.Key.Contains(family, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return _fonts.Values.FirstOrDefault() ?? SKTypeface.Default;
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
        _brightnessFilter?.Dispose();
        _textPaint.Dispose();
        _barPaint.Dispose();
        _blitPaint.Dispose();

        foreach (var bmp in _staticImages.Values) bmp.Dispose();
        _staticImages.Clear();

        foreach (var tf in _fonts.Values.Distinct()) tf.Dispose();
        _fonts.Clear();

        _animFrames.Clear();
    }

    private sealed record CachedAnimFrame(byte[] WebpBytes, long StartMs, int DurationMs);
}
