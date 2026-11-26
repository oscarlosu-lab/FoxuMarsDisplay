using SkiaSharp;

namespace FoxuMarsDisplay.Core.Backends;

public readonly record struct DirtyTileRect(int X, int Y, int Width, int Height);

/// <summary>
/// Clean-room tile-based framebuffer differ for bandwidth-constrained serial RGB565 displays.
/// Tracks the last transmitted RGB565 framebuffer and emits minimal coalesced dirty rectangles
/// within a per-tick byte budget so partial HUD updates run at high frame rates.
/// </summary>
public sealed class SerialTileDiffEngine
{
    public int Width { get; }
    public int Height { get; }
    public int TileWidth { get; }
    public int TileHeight { get; }

    private readonly int _cols;
    private readonly int _rows;
    private readonly ushort[] _lastFrame;
    private readonly bool[] _unknownTiles;
    private readonly bool[] _dirtyTiles;

    public SerialTileDiffEngine(int width, int height, int tileWidth = 16, int tileHeight = 16)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (tileWidth <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidth));
        if (tileHeight <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeight));

        Width = width;
        Height = height;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        _cols = (width + tileWidth - 1) / tileWidth;
        _rows = (height + tileHeight - 1) / tileHeight;
        _lastFrame = new ushort[width * height];
        _unknownTiles = new bool[_cols * _rows];
        _dirtyTiles = new bool[_cols * _rows];
        Array.Fill(_unknownTiles, true);
    }

    /// <summary>
    /// Marks the entire framebuffer as unsynchronized so the next tick retransmits everything.
    /// </summary>
    public void Invalidate()
    {
        Array.Fill(_unknownTiles, true);
    }

    public IReadOnlyList<DirtyTileRect> ComputeDirtyRects(
        ReadOnlySpan<ushort> nextRgb565,
        int maxBytesBudget,
        out bool hasPending)
    {
        if (nextRgb565.Length != Width * Height)
            throw new ArgumentException($"Expected {Width * Height} RGB565 pixels.", nameof(nextRgb565));

        hasPending = false;
        int budget = Math.Max(512, maxBytesBudget);

        int dirtyCount = MarkDirtyTiles(nextRgb565);
        if (dirtyCount == 0)
            return [];

        var merged = BuildCoalescedRects(nextRgb565);
        if (merged.Count == 0)
            return [];

        // Prioritize smaller regions first so clocks/metrics stay responsive even during large updates.
        merged.Sort((a, b) => (a.Width * a.Height).CompareTo(b.Width * b.Height));

        var emitted = new List<DirtyTileRect>(merged.Count);
        int bytesSent = 0;

        for (int i = 0; i < merged.Count; i++)
        {
            var r = merged[i];
            int rowBytes = r.Width * 2;
            int totalBytes = rowBytes * r.Height;

            if (bytesSent + totalBytes > budget)
            {
                int remainingBudget = budget - bytesSent;
                int fitRows = remainingBudget / rowBytes;
                if (emitted.Count == 0 && fitRows < 1)
                    fitRows = 1;

                if (fitRows <= 0)
                {
                    hasPending = true;
                    break;
                }

                var partial = new DirtyTileRect(r.X, r.Y, r.Width, Math.Min(r.Height, fitRows));
                emitted.Add(partial);
                CommitRectToLastFrame(nextRgb565, partial);
                hasPending = true;
                break;
            }

            emitted.Add(r);
            CommitRectToLastFrame(nextRgb565, r);
            bytesSent += totalBytes;
        }

        return emitted;
    }

    private int MarkDirtyTiles(ReadOnlySpan<ushort> next)
    {
        int count = 0;
        for (int ty = 0; ty < _rows; ty++)
        {
            int yStart = ty * TileHeight;
            int yEnd = Math.Min(Height, yStart + TileHeight);

            for (int tx = 0; tx < _cols; tx++)
            {
                int idx = ty * _cols + tx;
                if (_unknownTiles[idx])
                {
                    _dirtyTiles[idx] = true;
                    count++;
                    continue;
                }

                int xStart = tx * TileWidth;
                int spanLen = Math.Min(TileWidth, Width - xStart);
                bool tileDirty = false;

                for (int y = yStart; y < yEnd; y++)
                {
                    int offset = y * Width + xStart;
                    if (!next.Slice(offset, spanLen).SequenceEqual(_lastFrame.AsSpan(offset, spanLen)))
                    {
                        tileDirty = true;
                        break;
                    }
                }

                _dirtyTiles[idx] = tileDirty;
                if (tileDirty) count++;
            }
        }
        return count;
    }

    private List<DirtyTileRect> BuildCoalescedRects(ReadOnlySpan<ushort> next)
    {
        var rects = new List<DirtyTileRect>();
        var rectHasUnknown = new List<bool>();
        var openRuns = new Dictionary<(int ColStart, int ColEnd), int>();
        var currentRuns = new Dictionary<(int ColStart, int ColEnd), int>();

        for (int ty = 0; ty < _rows; ty++)
        {
            currentRuns.Clear();
            int y0 = ty * TileHeight;
            int y1 = Math.Min(Height, y0 + TileHeight);
            int tx = 0;

            while (tx < _cols)
            {
                if (!_dirtyTiles[ty * _cols + tx])
                {
                    tx++;
                    continue;
                }

                int colStart = tx;
                bool runUnknown = false;
                while (tx < _cols && _dirtyTiles[ty * _cols + tx])
                {
                    runUnknown |= _unknownTiles[ty * _cols + tx];
                    tx++;
                }
                int colEnd = tx;

                var key = (colStart, colEnd);
                if (openRuns.TryGetValue(key, out int existingIdx))
                {
                    var prev = rects[existingIdx];
                    rects[existingIdx] = new DirtyTileRect(prev.X, prev.Y, prev.Width, y1 - prev.Y);
                    rectHasUnknown[existingIdx] |= runUnknown;
                    currentRuns[key] = existingIdx;
                }
                else
                {
                    int x0 = colStart * TileWidth;
                    int x1 = Math.Min(Width, colEnd * TileWidth);
                    rects.Add(new DirtyTileRect(x0, y0, x1 - x0, y1 - y0));
                    rectHasUnknown.Add(runUnknown);
                    currentRuns[key] = rects.Count - 1;
                }
            }

            (openRuns, currentRuns) = (currentRuns, openRuns);
        }

        // Trim known rectangles to the exact bounding box of changed pixels.
        for (int i = rects.Count - 1; i >= 0; i--)
        {
            if (rectHasUnknown[i])
                continue;

            var r = rects[i];
            int minX = r.X + r.Width;
            int maxX = r.X - 1;
            int minY = r.Y + r.Height;
            int maxY = r.Y - 1;

            for (int y = r.Y; y < r.Y + r.Height; y++)
            {
                int rowOffset = y * Width + r.X;
                var a = next.Slice(rowOffset, r.Width);
                var b = _lastFrame.AsSpan(rowOffset, r.Width);
                int prefix = a.CommonPrefixLength(b);
                if (prefix == r.Width)
                    continue;

                int xEnd = r.Width - 1;
                while (xEnd > prefix && a[xEnd] == b[xEnd])
                    xEnd--;

                if (r.X + prefix < minX) minX = r.X + prefix;
                if (r.X + xEnd > maxX) maxX = r.X + xEnd;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            if (maxX < minX || maxY < minY)
                rects.RemoveAt(i);
            else
                rects[i] = new DirtyTileRect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        }

        return rects;
    }

    private void CommitRectToLastFrame(ReadOnlySpan<ushort> next, DirtyTileRect rect)
    {
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            int offset = y * Width + rect.X;
            next.Slice(offset, rect.Width).CopyTo(_lastFrame.AsSpan(offset, rect.Width));
        }

        int txStart = rect.X / TileWidth;
        int txEnd = (rect.X + rect.Width - 1) / TileWidth;
        int tyStart = rect.Y / TileHeight;
        int tyEnd = (rect.Y + rect.Height - 1) / TileHeight;

        for (int ty = tyStart; ty <= tyEnd; ty++)
        {
            int tileY0 = ty * TileHeight;
            int tileY1 = Math.Min(Height, tileY0 + TileHeight);
            if (rect.Y > tileY0 || rect.Y + rect.Height < tileY1)
                continue;

            for (int tx = txStart; tx <= txEnd; tx++)
            {
                int tileX0 = tx * TileWidth;
                int tileX1 = Math.Min(Width, tileX0 + TileWidth);
                if (rect.X <= tileX0 && rect.X + rect.Width >= tileX1)
                    _unknownTiles[ty * _cols + tx] = false;
            }
        }
    }

    public static byte[] ExtractRectRgb565Le(ReadOnlySpan<ushort> frame, int frameWidth, DirtyTileRect rect)
    {
        byte[] bytes = new byte[rect.Width * rect.Height * 2];
        int outIdx = 0;
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            int rowStart = y * frameWidth + rect.X;
            for (int x = 0; x < rect.Width; x++)
            {
                ushort px = frame[rowStart + x];
                bytes[outIdx++] = (byte)(px & 0xFF);
                bytes[outIdx++] = (byte)(px >> 8);
            }
        }
        return bytes;
    }

    public static ushort[] ConvertBgraToRgb565Array(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888)
            throw new ArgumentException("Bitmap must be Bgra8888", nameof(bitmap));

        int w = bitmap.Width;
        int h = bitmap.Height;
        ushort[] output = new ushort[w * h];

        unsafe
        {
            byte* srcBase = (byte*)bitmap.GetPixels().ToPointer();
            int stride = bitmap.RowBytes;
            int outIdx = 0;

            for (int y = 0; y < h; y++)
            {
                byte* row = srcBase + y * stride;
                for (int x = 0; x < w; x++)
                {
                    byte b = row[x * 4 + 0];
                    byte g = row[x * 4 + 1];
                    byte r = row[x * 4 + 2];
                    output[outIdx++] = (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
                }
            }
        }
        return output;
    }
}
