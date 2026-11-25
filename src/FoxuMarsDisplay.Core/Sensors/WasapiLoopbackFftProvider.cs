using System.Runtime.InteropServices;

namespace FoxuMarsDisplay.Core.Sensors;

/// <summary>
/// Zero-dependency Windows WASAPI loopback audio capture and 16-band FFT spectrum analyzer.
/// </summary>
public sealed class WasapiLoopbackFftProvider : IDisposable
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const int FftSize = 256;
    private const int BandCount = 16;

    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _smoothedBands = new float[BandCount];
    private readonly object _lock = new();
    private int _ringPos;
    private float _currentPeak;
    private long _lastSignalTickMs;

    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _started;
    private bool _disposed;

    public void Start()
    {
        if (_disposed || _started) return;
        _started = true;
        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoop(_cts.Token), _cts.Token);
    }

    public float[]? GetBandsCopy(out float peak)
    {
        lock (_lock)
        {
            peak = _currentPeak;
            if (Environment.TickCount64 - _lastSignalTickMs > 1200 || peak <= 0.005f)
                return null;
            return (float[])_smoothedBands.Clone();
        }
    }

    private void CaptureLoop(CancellationToken ct)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        IntPtr waveFormatPtr = IntPtr.Zero;

        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            if (type == null) return;
            enumerator = Activator.CreateInstance(type) as IMMDeviceEnumerator;
            if (enumerator == null || enumerator.GetDefaultAudioEndpoint(0, 0, out device) != 0 || device == null)
                return;

            var iidClient = IID_IAudioClient;
            if (device.Activate(ref iidClient, 23, IntPtr.Zero, out object clientObj) != 0)
                return;
            audioClient = clientObj as IAudioClient;
            if (audioClient == null) return;

            if (audioClient.GetMixFormat(out waveFormatPtr) != 0 || waveFormatPtr == IntPtr.Zero)
                return;

            ushort channels = (ushort)Marshal.ReadInt16(waveFormatPtr, 2);
            ushort bitsPerSample = (ushort)Marshal.ReadInt16(waveFormatPtr, 14);
            ushort formatTag = (ushort)Marshal.ReadInt16(waveFormatPtr, 0);
            bool isFloat = bitsPerSample == 32 && (formatTag == 3 || formatTag == 0xFFFE);
            if (channels == 0 || !isFloat)
                return;

            long hnsBufferDuration = 1_000_000; // 100ms
            if (audioClient.Initialize(0, AUDCLNT_STREAMFLAGS_LOOPBACK, hnsBufferDuration, 0, waveFormatPtr, IntPtr.Zero) != 0)
                return;

            var iidCap = IID_IAudioCaptureClient;
            if (audioClient.GetService(ref iidCap, out object capObj) != 0)
                return;
            captureClient = capObj as IAudioCaptureClient;
            if (captureClient == null) return;

            if (audioClient.Start() != 0)
                return;

            Span<float> temp = stackalloc float[FftSize];
            while (!ct.IsCancellationRequested)
            {
                Thread.Sleep(25);
                if (captureClient.GetNextPacketSize(out uint packetFrames) != 0)
                    break;

                bool updated = false;
                while (packetFrames > 0 && !ct.IsCancellationRequested)
                {
                    if (captureClient.GetBuffer(out IntPtr dataPtr, out uint framesAvail, out uint flags, out _, out _) != 0)
                        break;

                    try
                    {
                        bool silent = (flags & 0x2) != 0 || dataPtr == IntPtr.Zero;
                        int count = (int)framesAvail;
                        unsafe
                        {
                            float* fPtr = (float*)dataPtr.ToPointer();
                            for (int i = 0; i < count; i++)
                            {
                                float mono = 0f;
                                if (!silent)
                                {
                                    for (int ch = 0; ch < channels; ch++)
                                        mono += fPtr[i * channels + ch];
                                    mono /= channels;
                                }
                                _ring[_ringPos] = mono;
                                _ringPos = (_ringPos + 1) & (FftSize - 1);
                            }
                        }
                        updated = true;
                    }
                    finally
                    {
                        captureClient.ReleaseBuffer(framesAvail);
                    }

                    if (captureClient.GetNextPacketSize(out packetFrames) != 0)
                        break;
                }

                if (updated)
                {
                    for (int i = 0; i < FftSize; i++)
                        temp[i] = _ring[(_ringPos + i) & (FftSize - 1)];

                    float rms = 0f;
                    for (int i = 0; i < FftSize; i++) rms += temp[i] * temp[i];
                    rms = MathF.Sqrt(rms / FftSize);

                    float[] rawBands = ComputeFftBands(temp, BandCount);
                    lock (_lock)
                    {
                        _currentPeak = Math.Clamp(rms * 3.5f, 0f, 1f);
                        if (_currentPeak > 0.005f)
                            _lastSignalTickMs = Environment.TickCount64;
                        for (int b = 0; b < BandCount; b++)
                            _smoothedBands[b] = _smoothedBands[b] * 0.45f + rawBands[b] * 0.55f;
                    }
                }
            }

            try { audioClient.Stop(); } catch { }
        }
        catch
        {
            // Ignore audio endpoint transitions / headless CI without audio device.
        }
        finally
        {
            if (waveFormatPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(waveFormatPtr);
            if (captureClient != null) Marshal.ReleaseComObject(captureClient);
            if (audioClient != null) Marshal.ReleaseComObject(audioClient);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    public static float[] ComputeFftBands(ReadOnlySpan<float> monoSamples, int bandCount = 16)
    {
        float[] bands = new float[Math.Max(1, bandCount)];
        int n = FftSize;
        Span<float> re = stackalloc float[n];
        Span<float> im = stackalloc float[n];

        int copy = Math.Min(n, monoSamples.Length);
        for (int i = 0; i < copy; i++)
        {
            float hann = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (n - 1)));
            re[i] = monoSamples[i] * hann;
        }

        // Bit-reversal permutation
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
            int m = n >> 1;
            while (m >= 1 && j >= m)
            {
                j -= m;
                m >>= 1;
            }
            j += m;
        }

        // Iterative Radix-2 Cooley-Tukey FFT
        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * MathF.PI / len;
            float wLenRe = MathF.Cos(ang);
            float wLenIm = MathF.Sin(ang);
            int half = len >> 1;

            for (int i = 0; i < n; i += len)
            {
                float wRe = 1f, wIm = 0f;
                for (int k = 0; k < half; k++)
                {
                    int even = i + k;
                    int odd = even + half;
                    float tRe = wRe * re[odd] - wIm * im[odd];
                    float tIm = wRe * im[odd] + wIm * re[odd];

                    re[odd] = re[even] - tRe;
                    im[odd] = im[even] - tIm;
                    re[even] += tRe;
                    im[even] += tIm;

                    float nextRe = wRe * wLenRe - wIm * wLenIm;
                    wIm = wRe * wLenIm + wIm * wLenRe;
                    wRe = nextRe;
                }
            }
        }

        int halfBins = n / 2;
        for (int b = 0; b < bands.Length; b++)
        {
            int startBin = Math.Max(1, (int)MathF.Pow(halfBins, b / (float)bands.Length));
            int endBin = Math.Clamp((int)MathF.Pow(halfBins, (b + 1) / (float)bands.Length), startBin + 1, halfBins);
            float sum = 0f;
            for (int bin = startBin; bin < endBin; bin++)
                sum += MathF.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]);

            float avg = sum / Math.Max(1, endBin - startBin);
            bands[b] = Math.Clamp(MathF.Log10(1f + avg * 2.5f) * 0.85f, 0f, 1f);
        }
        return bands;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); } catch { }
        try { _captureTask?.Wait(400); } catch { }
        _cts?.Dispose();
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetStreamLatency(out long phnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }
}
