using System.Runtime.InteropServices;
using SkiaSharp;

namespace FoxuMarsDisplay.Core.Rendering;

/// <summary>
/// Zero-dependency Windows Media Foundation MP4/video frame decoder.
/// Decodes video frames into BGRA8888, scales/crops them to target dimensions,
/// and emits WebP-encoded frames for the .sdac animation cache.
/// </summary>
public static class MediaFoundationVideoDecoder
{
    private const uint MF_SDK_VERSION = 0x0002;
    private const uint MF_API_VERSION = 0x0070;
    private const uint MF_VERSION = (MF_SDK_VERSION << 16) | MF_API_VERSION;
    private const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
    private const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x00000002;
    private const uint MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x00000020;

    private static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");

    public static string? LastError { get; private set; }

    public static bool TryDecodeToFrames(
        string videoPath,
        int targetW,
        int targetH,
        float dstX,
        float dstY,
        float dstW,
        float dstH,
        int targetFps,
        int maxFrames,
        Action<byte[], int> onWebpFrame)
    {
        LastError = null;
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath) || targetW <= 0 || targetH <= 0)
        {
            LastError = "Invalid path or target dimensions";
            return false;
        }

        bool mfStarted = false;
        IMFAttributes? attrs = null;
        IMFSourceReader? reader = null;
        IMFMediaType? reqType = null;
        IMFMediaType? outType = null;

        try
        {
            int hr = MFStartup(MF_VERSION, 0);
            if (hr < 0)
                hr = MFStartup((0x0001u << 16) | MF_API_VERSION, 0);
            if (hr < 0)
            {
                LastError = $"MFStartup failed: 0x{hr:X8}";
                return false;
            }
            mfStarted = true;

            hr = MFCreateAttributes(out attrs, 1);
            if (hr == 0 && attrs != null)
            {
                var vidProc = MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING;
                attrs.SetUINT32(ref vidProc, 1);
            }

            hr = MFCreateSourceReaderFromURL(videoPath, attrs, out reader);
            if (hr < 0 || reader is null)
            {
                LastError = $"MFCreateSourceReaderFromURL failed: 0x{hr:X8}";
                return false;
            }

            hr = MFCreateMediaType(out reqType);
            if (hr < 0 || reqType is null)
            {
                LastError = $"MFCreateMediaType failed: 0x{hr:X8}";
                return false;
            }

            var kMajor = MF_MT_MAJOR_TYPE;
            var vMajor = MFMediaType_Video;
            hr = reqType.SetGUID(ref kMajor, ref vMajor);
            if (hr < 0) { LastError = $"SetGUID(MAJOR) failed: 0x{hr:X8}"; return false; }

            var kSub = MF_MT_SUBTYPE;
            var vSub = MFVideoFormat_RGB32;
            hr = reqType.SetGUID(ref kSub, ref vSub);
            if (hr < 0) { LastError = $"SetGUID(SUBTYPE) failed: 0x{hr:X8}"; return false; }

            hr = reader.SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, reqType);
            if (hr < 0)
            {
                LastError = $"SetCurrentMediaType failed: 0x{hr:X8}";
                return false;
            }

            if (!ReadOutputFormat(reader, out outType, out int srcW, out int srcH, out int stride))
            {
                LastError = "ReadOutputFormat failed";
                return false;
            }

            int clampedFps = Math.Clamp(targetFps, 1, 60);
            long minStepHns = (long)(10_000_000.0 / clampedFps);
            int fallbackDurMs = Math.Max(33, 1000 / clampedFps);
            int maxEmit = Math.Max(1, maxFrames);

            using var cropBmp = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var cropCanvas = new SKCanvas(cropBmp);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.Low, IsAntialias = false };
            var destRect = new SKRect(dstX, dstY, dstX + dstW, dstY + dstH);

            var srcBmp = new SKBitmap(srcW, srcH, SKColorType.Bgra8888, SKAlphaType.Opaque);
            byte[] rowBuf = new byte[srcW * 4];
            int emitted = 0;
            int rawReads = 0;
            uint lastFlags = 0;
            long lastEmitHns = long.MinValue;

            try
            {
                while (emitted < maxEmit && rawReads < 3000)
                {
                    rawReads++;
                    hr = reader.ReadSample(
                        MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                        0,
                        out _,
                        out uint flags,
                        out long timestampHns,
                        out IMFSample? sample);

                    lastFlags = flags;
                    if (hr < 0)
                        break;

                    if ((flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                    {
                        if (outType != null)
                        {
                            Marshal.ReleaseComObject(outType);
                            outType = null;
                        }
                        if (!ReadOutputFormat(reader, out outType, out int newW, out int newH, out int newStride))
                        {
                            if (sample != null) Marshal.ReleaseComObject(sample);
                            break;
                        }
                        if (newW != srcW || newH != srcH)
                        {
                            srcW = newW;
                            srcH = newH;
                            srcBmp.Dispose();
                            srcBmp = new SKBitmap(srcW, srcH, SKColorType.Bgra8888, SKAlphaType.Opaque);
                            rowBuf = new byte[srcW * 4];
                        }
                        stride = newStride;
                    }

                    if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                    {
                        if (sample != null) Marshal.ReleaseComObject(sample);
                        break;
                    }

                    if (sample is null)
                        continue;

                    try
                    {
                        if (lastEmitHns != long.MinValue && timestampHns > lastEmitHns &&
                            (timestampHns - lastEmitHns) < minStepHns)
                        {
                            continue;
                        }

                        int durMs = fallbackDurMs;
                        if (lastEmitHns != long.MinValue && timestampHns > lastEmitHns)
                            durMs = Math.Clamp((int)((timestampHns - lastEmitHns) / 10_000), 20, 500);

                        hr = sample.ConvertToContiguousBuffer(out IMFMediaBuffer? buffer);
                        if (hr < 0 || buffer is null)
                            continue;

                        try
                        {
                            hr = buffer.Lock(out IntPtr bufPtr, out _, out uint curLen);
                            if (hr < 0 || bufPtr == IntPtr.Zero || curLen == 0)
                                continue;

                            try
                            {
                                bool bottomUp = stride < 0;
                                int absStride = Math.Abs(stride);
                                if (absStride < srcW * 4)
                                    absStride = srcW * 4;

                                IntPtr dstBase = srcBmp.GetPixels();
                                int dstRowBytes = srcBmp.RowBytes;
                                int copyBytes = Math.Min(srcW * 4, absStride);

                                for (int y = 0; y < srcH; y++)
                                {
                                    int srcRow = bottomUp ? (srcH - 1 - y) : y;
                                    long srcOffset = (long)srcRow * absStride;
                                    if (srcOffset + copyBytes > curLen)
                                        break;

                                    Marshal.Copy(IntPtr.Add(bufPtr, (int)srcOffset), rowBuf, 0, copyBytes);
                                    for (int x = 3; x < copyBytes; x += 4)
                                        rowBuf[x] = 0xFF;

                                    Marshal.Copy(rowBuf, 0, IntPtr.Add(dstBase, y * dstRowBytes), copyBytes);
                                }
                            }
                            finally
                            {
                                buffer.Unlock();
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(buffer);
                        }

                        cropCanvas.Clear(SKColors.Black);
                        cropCanvas.DrawBitmap(srcBmp, destRect, paint);

                        using var img = SKImage.FromBitmap(cropBmp);
                        using var data = img.Encode(SKEncodedImageFormat.Webp, 80);
                        if (data != null && data.Size > 0)
                        {
                            onWebpFrame(data.ToArray(), durMs);
                            emitted++;
                            lastEmitHns = timestampHns;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sample);
                    }
                }
            }
            finally
            {
                srcBmp.Dispose();
            }

            if (emitted == 0 && LastError is null)
                LastError = $"ReadSample emitted 0 frames (rawReads={rawReads}, lastHr=0x{hr:X8}, lastFlags=0x{lastFlags:X8}, src={srcW}x{srcH}, stride={stride})";
            return emitted > 0;
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            return false;
        }
        finally
        {
            if (outType != null) Marshal.ReleaseComObject(outType);
            if (reqType != null) Marshal.ReleaseComObject(reqType);
            if (reader != null) Marshal.ReleaseComObject(reader);
            if (attrs != null) Marshal.ReleaseComObject(attrs);
            if (mfStarted)
            {
                try { MFShutdown(); } catch { }
            }
        }
    }

    private static bool ReadOutputFormat(
        IMFSourceReader reader,
        out IMFMediaType? outType,
        out int width,
        out int height,
        out int stride)
    {
        outType = null;
        width = 0;
        height = 0;
        stride = 0;

        int hr = reader.GetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, out outType);
        if (hr < 0 || outType is null)
            return false;

        var kFrameSize = MF_MT_FRAME_SIZE;
        hr = outType.GetUINT64(ref kFrameSize, out ulong packed);
        if (hr < 0)
            return false;

        width = (int)(packed >> 32);
        height = (int)(packed & 0xFFFFFFFF);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            return false;

        var kStride = MF_MT_DEFAULT_STRIDE;
        if (outType.GetUINT32(ref kStride, out uint rawStride) == 0)
            stride = unchecked((int)rawStride);
        if (stride == 0)
            stride = width * 4;

        return true;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSourceReaderFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszURL,
        IMFAttributes? pAttributes,
        out IMFSourceReader ppSourceReader);

    [ComImport]
    [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, int MatchType, out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);
    }

    [ComImport]
    [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, int MatchType, out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);
    }

    [ComImport]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, int MatchType, out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);
        [PreserveSig] int GetSampleFlags(out uint pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);
        [PreserveSig] int SetSampleTime(long hnsSampleTime);
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
    }

    [ComImport]
    [Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(uint cbCurrentLength);
        [PreserveSig] int GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport]
    [Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint dwStreamIndex, out bool pfSelected);
        [PreserveSig] int SetStreamSelection(uint dwStreamIndex, bool fSelected);
        [PreserveSig] int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int GetCurrentMediaType(uint dwStreamIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int SetCurrentMediaType(uint dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
        [PreserveSig] int SetCurrentPosition(ref Guid guidTimeFormat, IntPtr varPosition);
        [PreserveSig] int ReadSample(
            uint dwStreamIndex,
            uint dwControlFlags,
            out uint pdwActualStreamIndex,
            out uint pdwStreamFlags,
            out long pllTimestamp,
            out IMFSample? ppSample);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetPresentationAttribute(uint dwStreamIndex, ref Guid guidAttribute, IntPtr pvarAttribute);
    }
}
