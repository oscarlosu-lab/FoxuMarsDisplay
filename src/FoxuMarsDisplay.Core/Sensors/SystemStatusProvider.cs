using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace FoxuMarsDisplay.Core.Sensors;

/// <summary>
/// Lightweight, zero-dependency Windows system status &amp; audio/media provider:
/// - Master Volume, Mute &amp; Audio Peak via Windows CoreAudio COM
/// - NumLock, CapsLock &amp; Network connectivity via Win32 / .NET
/// - Active Media Title / Artist from open player windows (throttled)
/// </summary>
public sealed class SystemStatusProvider : IDisposable
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioEndpointVolume? _endpointVolume;
    private IAudioMeterInformation? _meterInfo;
    private bool _audioInitAttempted;

    private long _lastMediaTickMs = -10_000;
    private string _cachedSongTitle = string.Empty;
    private string _cachedArtist = string.Empty;
    private bool _disposed;

    public void Sample(
        out double volumePct,
        out bool isMuted,
        out float audioPeak,
        out bool numLockOn,
        out bool capsLockOn,
        out bool networkConnected,
        out string songTitle,
        out string artist)
    {
        volumePct = 0;
        isMuted = false;
        audioPeak = 0f;

        if (!_audioInitAttempted)
        {
            _audioInitAttempted = true;
            TryInitAudio();
        }

        if (_endpointVolume != null)
        {
            try
            {
                if (_endpointVolume.GetMasterVolumeLevelScalar(out float lvl) == 0)
                    volumePct = Math.Clamp(lvl * 100.0, 0.0, 100.0);
                if (_endpointVolume.GetMute(out bool muted) == 0)
                    isMuted = muted;
            }
            catch
            {
                ResetAudio();
            }
        }

        if (_meterInfo != null)
        {
            try
            {
                if (_meterInfo.GetPeakValue(out float peak) == 0)
                    audioPeak = Math.Clamp(peak, 0f, 1f);
            }
            catch
            {
                // Ignore transient peak errors.
            }
        }

        numLockOn = (GetKeyState(0x90) & 1) != 0;
        capsLockOn = (GetKeyState(0x14) & 1) != 0;
        try { networkConnected = NetworkInterface.GetIsNetworkAvailable(); }
        catch { networkConnected = false; }

        long now = Environment.TickCount64;
        if (now - _lastMediaTickMs >= 3000)
        {
            _lastMediaTickMs = now;
            SampleMediaWindowTitle(out _cachedSongTitle, out _cachedArtist);
        }

        songTitle = _cachedSongTitle;
        artist = _cachedArtist;
    }

    private void TryInitAudio()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            if (type == null) return;
            _enumerator = Activator.CreateInstance(type) as IMMDeviceEnumerator;
            if (_enumerator == null) return;

            if (_enumerator.GetDefaultAudioEndpoint(0, 1, out _device) != 0 || _device == null)
                return;

            var iidVol = IID_IAudioEndpointVolume;
            if (_device.Activate(ref iidVol, 23, IntPtr.Zero, out object volObj) == 0)
                _endpointVolume = volObj as IAudioEndpointVolume;

            var iidMeter = IID_IAudioMeterInformation;
            if (_device.Activate(ref iidMeter, 23, IntPtr.Zero, out object meterObj) == 0)
                _meterInfo = meterObj as IAudioMeterInformation;
        }
        catch
        {
            ResetAudio();
        }
    }

    private void ResetAudio()
    {
        try { if (_meterInfo != null) Marshal.ReleaseComObject(_meterInfo); } catch { }
        try { if (_endpointVolume != null) Marshal.ReleaseComObject(_endpointVolume); } catch { }
        try { if (_device != null) Marshal.ReleaseComObject(_device); } catch { }
        try { if (_enumerator != null) Marshal.ReleaseComObject(_enumerator); } catch { }
        _meterInfo = null;
        _endpointVolume = null;
        _device = null;
        _enumerator = null;
    }

    private static void SampleMediaWindowTitle(out string songTitle, out string artist)
    {
        songTitle = string.Empty;
        artist = string.Empty;
        string[] players = ["Spotify", "YouTube Music", "AppleMusic", "vlc", "foobar2000", "MusicBee", "AIMP"];
        try
        {
            foreach (var pname in players)
            {
                var procs = Process.GetProcessesByName(pname);
                foreach (var p in procs)
                {
                    try
                    {
                        string title = (p.MainWindowTitle ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        if (title.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
                            title.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase) ||
                            title.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase) ||
                            title.Equals("VLC media player", StringComparison.OrdinalIgnoreCase))
                            continue;

                        int sep = title.IndexOf(" - ", StringComparison.Ordinal);
                        if (sep > 0)
                        {
                            artist = title[..sep].Trim();
                            songTitle = title[(sep + 3)..].Trim();
                        }
                        else
                        {
                            songTitle = title;
                        }
                        return;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetAudio();
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, uint dwStateMask, out IntPtr ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float pfPeak);
    }
}
