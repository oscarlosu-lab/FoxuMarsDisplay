using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace FoxuMarsDisplay.Core.Themes;

/// <summary>
/// Extracts preview images from remote .smtheme ZIP archives using HTTP Range requests,
/// avoiding full multi-megabyte downloads when no external thumbnail URL exists.
/// </summary>
public static class RemoteZipPreviewExtractor
{
    private const int TailFetchBytes = 98_304; // 96 KB
    private const uint EocdSignature = 0x06054B50;
    private const uint CentralDirSignature = 0x02014B50;
    private const uint MaxUncompressedPreviewBytes = 12 * 1024 * 1024;

    private static readonly string[] PreferredNames =
    [
        "preview.png",
        "demo.png",
        "background.png",
        "back.png"
    ];

    public static async Task<byte[]?> TryExtractPreviewAsync(HttpClient http, string zipUrl, CancellationToken ct = default)
    {
        if (http is null || string.IsNullOrWhiteSpace(zipUrl))
            return null;

        try
        {
            using var tailReq = new HttpRequestMessage(HttpMethod.Get, zipUrl);
            tailReq.Headers.Range = new RangeHeaderValue(null, TailFetchBytes);

            using var tailResp = await http.SendAsync(tailReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (tailResp.StatusCode != HttpStatusCode.PartialContent && tailResp.StatusCode != HttpStatusCode.OK)
                return null;

            byte[] tail = await tailResp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (tail.Length < 22)
                return null;

            long totalLength = tail.Length;
            long tailStart = 0;
            if (tailResp.StatusCode == HttpStatusCode.PartialContent)
            {
                var cr = tailResp.Content.Headers.ContentRange;
                if (cr?.Length is > 0)
                {
                    totalLength = cr.Length.Value;
                    tailStart = totalLength - tail.Length;
                }
                else if (cr?.From is >= 0)
                {
                    tailStart = cr.From.Value;
                    totalLength = tailStart + tail.Length;
                }
            }

            int eocdOffset = FindEocdOffset(tail);
            if (eocdOffset < 0 || eocdOffset + 22 > tail.Length)
                return null;

            uint cdSize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocdOffset + 12, 4));
            uint cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocdOffset + 16, 4));
            if (cdSize == 0 || cdSize > 2 * 1024 * 1024)
                return null;

            byte[]? cdBytes = null;
            if (cdOffset >= tailStart && (cdOffset - tailStart) + cdSize <= tail.Length)
            {
                int rel = (int)(cdOffset - tailStart);
                cdBytes = tail.AsSpan(rel, (int)cdSize).ToArray();
            }
            else
            {
                cdBytes = await FetchRangeAsync(http, zipUrl, cdOffset, cdOffset + cdSize - 1, ct).ConfigureAwait(false);
            }

            if (cdBytes is null || cdBytes.Length < 46)
                return null;

            var entries = ParseCentralDirectory(cdBytes);
            var selected = SelectBestPreviewEntry(entries);
            if (selected is null)
                return null;

            var entry = selected.Value;
            if (entry.UncompressedSize == 0 || entry.UncompressedSize > MaxUncompressedPreviewBytes)
                return null;
            if (entry.CompressionMethod != 0 && entry.CompressionMethod != 8)
                return null;

            byte[]? compressedData = null;

            // Fast 1-request path: local header + compressed payload already reside inside the tail buffer.
            if (entry.LocalHeaderOffset >= tailStart && entry.LocalHeaderOffset + 30 <= totalLength)
            {
                int locRel = (int)(entry.LocalHeaderOffset - tailStart);
                ushort locNameLen = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(locRel + 26, 2));
                ushort locExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(locRel + 28, 2));
                long dataStart = entry.LocalHeaderOffset + 30L + locNameLen + locExtraLen;
                if (dataStart + entry.CompressedSize <= totalLength)
                {
                    int dataRel = (int)(dataStart - tailStart);
                    compressedData = tail.AsSpan(dataRel, (int)entry.CompressedSize).ToArray();
                }
            }

            // 2-request fallback path: fetch local header + payload directly.
            if (compressedData is null)
            {
                long end = Math.Min(totalLength - 1, entry.LocalHeaderOffset + 30L + entry.NameByteLength + 512L + entry.CompressedSize);
                byte[]? blob = await FetchRangeAsync(http, zipUrl, entry.LocalHeaderOffset, end, ct).ConfigureAwait(false);
                if (blob is null || blob.Length < 30)
                    return null;

                ushort locNameLen = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(26, 2));
                ushort locExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(28, 2));
                int dataRel = 30 + locNameLen + locExtraLen;

                if (dataRel + entry.CompressedSize <= blob.Length)
                {
                    compressedData = blob.AsSpan(dataRel, (int)entry.CompressedSize).ToArray();
                }
                else
                {
                    long dataStart = entry.LocalHeaderOffset + dataRel;
                    long dataEnd = Math.Min(totalLength - 1, dataStart + entry.CompressedSize - 1);
                    compressedData = await FetchRangeAsync(http, zipUrl, dataStart, dataEnd, ct).ConfigureAwait(false);
                }
            }

            if (compressedData is null || compressedData.Length == 0)
                return null;

            byte[]? raw = Decompress(compressedData, entry.CompressionMethod, (int)entry.UncompressedSize);
            return raw is not null && IsValidImageHeader(raw) ? raw : null;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryExtractPreviewFromLocalZip(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return null;

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry? chosen = null;

            foreach (string preferred in PreferredNames)
            {
                chosen = zip.Entries.FirstOrDefault(e =>
                    !string.IsNullOrEmpty(e.Name) &&
                    e.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                if (chosen is not null)
                    break;
            }

            chosen ??= zip.Entries.FirstOrDefault(e => IsImageFileName(e.Name));
            if (chosen is null || chosen.Length <= 0 || chosen.Length > MaxUncompressedPreviewBytes)
                return null;

            using var stream = chosen.Open();
            using var ms = new MemoryStream((int)chosen.Length);
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            return IsValidImageHeader(bytes) ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> FetchRangeAsync(HttpClient http, string url, long from, long to, CancellationToken ct)
    {
        if (from < 0 || to < from)
            return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(from, to);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode != HttpStatusCode.PartialContent && resp.StatusCode != HttpStatusCode.OK)
            return null;

        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private static int FindEocdOffset(byte[] tail)
    {
        int min = Math.Max(0, tail.Length - 65_557);
        for (int i = tail.Length - 22; i >= min; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i, 4)) == EocdSignature)
                return i;
        }
        return -1;
    }

    private readonly record struct ZipEntryMeta(
        string FileName,
        ushort CompressionMethod,
        uint CompressedSize,
        uint UncompressedSize,
        ushort NameByteLength,
        uint LocalHeaderOffset);

    private static List<ZipEntryMeta> ParseCentralDirectory(byte[] cd)
    {
        var list = new List<ZipEntryMeta>();
        int pos = 0;
        while (pos + 46 <= cd.Length)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos, 4)) != CentralDirSignature)
                break;

            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 10, 2));
            uint compSize = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 20, 4));
            uint uncompSize = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 24, 4));
            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 28, 2));
            ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 30, 2));
            ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 32, 2));
            uint localOff = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 42, 4));

            if (pos + 46 + nameLen > cd.Length)
                break;

            string fullName = System.Text.Encoding.UTF8.GetString(cd, pos + 46, nameLen);
            string fileName = Path.GetFileName(fullName.Replace('\\', '/'));
            if (!string.IsNullOrWhiteSpace(fileName))
                list.Add(new ZipEntryMeta(fileName, method, compSize, uncompSize, nameLen, localOff));

            pos += 46 + nameLen + extraLen + commentLen;
        }
        return list;
    }

    private static ZipEntryMeta? SelectBestPreviewEntry(List<ZipEntryMeta> entries)
    {
        foreach (string preferred in PreferredNames)
        {
            foreach (var e in entries)
            {
                if (e.FileName.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
        }

        foreach (var e in entries)
        {
            if (IsImageFileName(e.FileName))
                return e;
        }

        return null;
    }

    private static bool IsImageFileName(string name) =>
        name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    private static byte[]? Decompress(byte[] compressedData, ushort method, int expectedSize)
    {
        if (method == 0)
            return compressedData;

        if (method == 8)
        {
            using var src = new MemoryStream(compressedData);
            using var deflate = new DeflateStream(src, CompressionMode.Decompress);
            using var dst = new MemoryStream(Math.Max(1024, expectedSize));
            deflate.CopyTo(dst);
            return dst.ToArray();
        }

        return null;
    }

    public static bool IsValidImageHeader(byte[] data)
    {
        if (data.Length < 12) return false;
        // PNG
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return true;
        // JPEG
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return true;
        // GIF
        if (data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8')
            return true;
        // WEBP (RIFF....WEBP)
        if (data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return true;
        return false;
    }
}
