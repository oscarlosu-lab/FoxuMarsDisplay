using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FoxuMarsDisplay.Core.Native;

/// <summary>
/// P/Invoke bindings for MacroSilicon MSDISPLAYSDKWRRAPER.dll (x64).
/// </summary>
public static class MsDisplayApi
{
    private const string DllName = "MSDISPLAYSDKWRRAPER.dll";

    static MsDisplayApi()
    {
        NativeLibrary.SetDllImportResolver(typeof(MsDisplayApi).Assembly, ResolveNativeLibrary);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        foreach (var candidate in GetCandidateDllPaths(DllName))
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    public static IEnumerable<string> GetCandidateDllPaths(string dllName)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "native", "x64", dllName);
        yield return Path.Combine(AppContext.BaseDirectory, dllName);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            yield return Path.Combine(localAppData, "SOEYI", "dll", "x64", dllName);

        foreach (var root in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                string? installLoc = null;
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(root, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall != null)
                    {
                        foreach (var subName in uninstall.GetSubKeyNames())
                        {
                            using var sub = uninstall.OpenSubKey(subName);
                            var displayName = sub?.GetValue("DisplayName") as string;
                            if (!string.IsNullOrEmpty(displayName) &&
                                displayName.Contains("SOEYI", StringComparison.OrdinalIgnoreCase))
                            {
                                installLoc = sub?.GetValue("InstallLocation") as string;
                                if (!string.IsNullOrEmpty(installLoc))
                                    break;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore registry permission errors.
                }

                if (!string.IsNullOrEmpty(installLoc))
                    yield return Path.Combine(installLoc, "dll", "x64", dllName);
            }
        }

        var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(progFilesX86))
            yield return Path.Combine(progFilesX86, "SOEYI", "dll", "x64", dllName);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSDisplayPicture
    {
        public int Width;
        public int Height;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSDisplayResolution
    {
        public int Width;
        public int Height;
        public int Refresh;

        public bool IsValid => Width > 0 && Height > 0;
        public override string ToString() => $"{Width}x{Height}@{Refresh}Hz";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSDisplayTiming
    {
        public uint vic;
        public uint polarity;
        public uint htotal;
        public uint vtotal;
        public uint hactive;
        public uint vactive;
        public uint pixclk;
        public uint vfreq;
        public uint hoffset;
        public uint voffset;
        public uint hsyncwidth;
        public uint vsyncwidth;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct MSDisplayEdid
    {
        public int mode;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] edid_replace;

        public int append_count;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public MSDisplayTiming[] timing_append;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AttachProcHandler(uint handle, IntPtr resolution, int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DetachProcHandler(uint handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Wrraper_MSDisplayStart(int loglevel, IntPtr edid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Wrraper_MSDisplayStop();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Wrraper_MSDisplayRegisterCallback(IntPtr attach_fun, IntPtr detach_fun);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Wrraper_MSDisplayReadSN")]
    public static extern int MSDisplayReadSN(uint handle, IntPtr data);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Wrraper_MSDisplaySetVideoParam")]
    public static extern int MSDisplaySetVideoParam(uint handle, IntPtr resolution);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Wrraper_MSDisplaySendPicture")]
    public static extern int MSDisplaySendPicture(uint handle, IntPtr pic, [MarshalAs(UnmanagedType.I1)] bool useJpeg);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Wrraper_MSDisplayPause")]
    public static extern int MSDisplayPause();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Wrraper_MSDisplayResume")]
    public static extern int MSDisplayResume();

    public static MSDisplayEdid CreateDefaultEdid()
    {
        var timings = new MSDisplayTiming[16];
        timings[0] = MakeTiming(146, 320, 240);
        timings[1] = MakeTiming(147, 240, 320);
        timings[2] = MakeTiming(150, 480, 480);
        timings[3] = MakeTiming(159, 320, 960);
        timings[4] = MakeTiming(160, 360, 960);
        timings[5] = MakeTiming(161, 960, 360);
        timings[6] = MakeTiming(162, 462, 1920);

        return new MSDisplayEdid
        {
            mode = 2,
            edid_replace = new byte[128],
            append_count = 7,
            timing_append = timings
        };
    }

    private static MSDisplayTiming MakeTiming(uint vic, uint width, uint height)
    {
        uint htotal = width + 80;
        uint vtotal = height + 40;
        uint pixclk = Math.Max(1000, (htotal * vtotal * 60) / 10000);
        return new MSDisplayTiming
        {
            vic = vic,
            polarity = 5,
            htotal = htotal,
            vtotal = vtotal,
            hactive = width,
            vactive = height,
            pixclk = pixclk,
            vfreq = 6000,
            hoffset = 40,
            voffset = 20,
            hsyncwidth = 16,
            vsyncwidth = 4
        };
    }
}
