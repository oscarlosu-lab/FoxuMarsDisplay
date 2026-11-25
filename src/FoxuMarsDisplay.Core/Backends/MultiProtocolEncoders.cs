using System.Text;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Backends;

/// <summary>
/// Clean-room wire protocol packet builders and pixel-format encoders for
/// MacroSilicon MS9132, HL-VMAX Bulk, Thermalright LY / ChiZhu, and ArtInChip HID.
/// </summary>
public static class Ms9132Protocol
{
    public const ushort Vid = 0x345F;
    public const ushort Pid = 0x9132;
    public const byte PixFmtBgr888 = 0x11;
    public const byte PixFmtUyvy = 0x22;

    public static readonly byte[] FrameFooter = [0xFF, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    public static byte[] BuildFrameHeader(int x, int y, int width, int height)
    {
        int xBlock = (x / 16) & 0xFF;
        int wBlock = (width / 16) & 0xFF;
        return
        [
            0xFF,
            0x00,
            (byte)xBlock,
            (byte)((y >> 8) & 0xFF),
            (byte)(y & 0xFF),
            (byte)wBlock,
            (byte)((height >> 8) & 0xFF),
            (byte)(height & 0xFF)
        ];
    }

    public static IReadOnlyList<byte[]> BuildInitHidReports(int width, int height, byte vic, byte pixFmt = PixFmtUyvy)
    {
        var list = new List<byte[]>
        {
            new byte[] { 0xA6, 0x01, (byte)(width >> 8), (byte)(width & 0xFF), (byte)(height >> 8), (byte)(height & 0xFF), pixFmt, 0x00 },
            new byte[] { 0xA6, 0x02, vic, 0x01, (byte)(width >> 8), (byte)(width & 0xFF), (byte)(height >> 8), (byte)(height & 0xFF) },
            new byte[] { 0xB6, 0xF0, 0x04, 0x6D, 0x00, 0x00, 0x00, 0x00 }
        };
        for (byte reg = 0x00; reg <= 0x7C; reg += 4)
            list.Add([0xB5, 0xC0, reg, 0x00, 0x00, 0x00, 0x00, 0x00]);
        return list;
    }

    public static byte[] ConvertBgraToUyvy(SKBitmap bitmap)
    {
        int w = bitmap.Width & ~1;
        int h = bitmap.Height;
        byte[] output = new byte[w * h * 2];

        unsafe
        {
            byte* srcBase = (byte*)bitmap.GetPixels().ToPointer();
            int stride = bitmap.RowBytes;
            int outIdx = 0;

            for (int y = 0; y < h; y++)
            {
                byte* row = srcBase + y * stride;
                for (int x = 0; x < w; x += 2)
                {
                    int b0 = row[x * 4 + 0], g0 = row[x * 4 + 1], r0 = row[x * 4 + 2];
                    int b1 = row[(x + 1) * 4 + 0], g1 = row[(x + 1) * 4 + 1], r1 = row[(x + 1) * 4 + 2];

                    int y0 = ((66 * r0 + 129 * g0 + 25 * b0 + 128) >> 8) + 16;
                    int y1 = ((66 * r1 + 129 * g1 + 25 * b1 + 128) >> 8) + 16;

                    int rAvg = (r0 + r1) >> 1, gAvg = (g0 + g1) >> 1, bAvg = (b0 + b1) >> 1;
                    int u = ((-38 * rAvg - 74 * gAvg + 112 * bAvg + 128) >> 8) + 128;
                    int v = ((112 * rAvg - 94 * gAvg - 18 * bAvg + 128) >> 8) + 128;

                    output[outIdx++] = (byte)Math.Clamp(u, 16, 240);
                    output[outIdx++] = (byte)Math.Clamp(y0, 16, 235);
                    output[outIdx++] = (byte)Math.Clamp(v, 16, 240);
                    output[outIdx++] = (byte)Math.Clamp(y1, 16, 235);
                }
            }
        }
        return output;
    }

    public static byte[] ConvertBgraToSanitizedBgr888(SKBitmap bitmap)
    {
        int w = bitmap.Width;
        int h = bitmap.Height;
        byte[] output = new byte[w * h * 3];

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
                    output[outIdx++] = b == 0xFF ? (byte)0xFE : b;
                    output[outIdx++] = g == 0xFF ? (byte)0xFE : g;
                    output[outIdx++] = r == 0xFF ? (byte)0xFE : r;
                }
            }
        }
        return output;
    }
}

public static class HlVmaxBulkProtocol
{
    public static byte[] Build12ByteHeader(ushort width, ushort height, ushort stride = 0, ushort flag = 1) =>
    [
        0x0A, 0x10, 0x08, 0x00,
        (byte)(width & 0xFF), (byte)(width >> 8),
        (byte)(height & 0xFF), (byte)(height >> 8),
        (byte)(stride & 0xFF), (byte)(stride >> 8),
        (byte)(flag & 0xFF), (byte)(flag >> 8)
    ];
}

public static class ThermalrightProtocol
{
    public static byte[] BuildLyHandshake()
    {
        byte[] buf = new byte[2048];
        buf[0] = 0x12;
        buf[1] = 0x34;
        buf[2] = 0x56;
        buf[3] = 0x78;
        return buf;
    }

    public static IReadOnlyList<byte[]> ChunkLyJpeg(byte[] jpeg)
    {
        var blocks = new List<byte[]>();
        for (int offset = 0; offset < jpeg.Length; offset += 496)
        {
            byte[] block = new byte[512];
            int count = Math.Min(496, jpeg.Length - offset);
            block[0] = 0x4C;
            block[1] = 0x59;
            block[2] = (byte)(count & 0xFF);
            block[3] = (byte)(count >> 8);
            Buffer.BlockCopy(jpeg, offset, block, 16, count);
            blocks.Add(block);
        }
        return blocks;
    }

    public static byte[] BuildChiZhuHandshake()
    {
        byte[] buf = new byte[64];
        buf[0] = 0xDA;
        buf[1] = 0xDB;
        buf[2] = 0xDC;
        buf[3] = 0xDD;
        return buf;
    }

    public static byte[] BuildChiZhuImageHeader(int width, int height, int jpegLen)
    {
        byte[] hdr = new byte[64];
        hdr[0] = 0xDA;
        hdr[1] = 0xDB;
        hdr[2] = 0xDC;
        hdr[3] = 0xDD;
        hdr[4] = (byte)(width & 0xFF);
        hdr[5] = (byte)(width >> 8);
        hdr[6] = (byte)(height & 0xFF);
        hdr[7] = (byte)(height >> 8);
        hdr[60] = (byte)(jpegLen & 0xFF);
        hdr[61] = (byte)((jpegLen >> 8) & 0xFF);
        hdr[62] = (byte)((jpegLen >> 16) & 0xFF);
        hdr[63] = (byte)((jpegLen >> 24) & 0xFF);
        return hdr;
    }

    public static IReadOnlyList<byte[]> ChunkChiZhuPayload(byte[] data, int chunkSize = 16384)
    {
        var chunks = new List<byte[]>();
        for (int i = 0; i < data.Length; i += chunkSize)
        {
            int len = Math.Min(chunkSize, data.Length - i);
            byte[] slice = new byte[len];
            Buffer.BlockCopy(data, i, slice, 0, len);
            chunks.Add(slice);
        }
        return chunks;
    }
}

public static class ArtInChipHidProtocol
{
    public static byte[] BuildEscapedJsonReport(string commandLine, string jsonBody)
    {
        byte[] payload = Encoding.UTF8.GetBytes($"{commandLine}\n{jsonBody}");
        int logicalLen = payload.Length + 5;
        byte lenHi = (byte)((logicalLen >> 8) & 0xFF);
        byte lenLo = (byte)(logicalLen & 0xFF);

        int sum = lenHi + lenLo;
        foreach (byte b in payload) sum += b;
        byte checksum = (byte)(sum & 0xFF);

        var frame = new List<byte>(1025) { 0x00, 0x5A };
        void WriteEscaped(byte b)
        {
            if (b == 0x5A) { frame.Add(0x5B); frame.Add(0x01); }
            else if (b == 0x5B) { frame.Add(0x5B); frame.Add(0x02); }
            else { frame.Add(b); }
        }

        WriteEscaped(lenHi);
        WriteEscaped(lenLo);
        foreach (byte b in payload) WriteEscaped(b);
        WriteEscaped(checksum);
        frame.Add(0x5A);

        byte[] report = new byte[1025];
        int copyLen = Math.Min(report.Length, frame.Count);
        frame.CopyTo(0, report, 0, copyLen);
        return report;
    }
}
