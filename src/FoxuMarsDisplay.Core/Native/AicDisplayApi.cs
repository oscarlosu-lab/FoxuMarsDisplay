using System.Reflection;
using System.Runtime.InteropServices;

namespace FoxuMarsDisplay.Core.Native;

/// <summary>
/// P/Invoke bindings for AicUsbDisplay.dll (x64).
/// </summary>
public static class AicDisplayApi
{
    private const string DllName = "AicUsbDisplay.dll";

    static AicDisplayApi()
    {
        NativeLibrary.SetDllImportResolver(typeof(AicDisplayApi).Assembly, ResolveNativeLibrary);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        foreach (var candidate in MsDisplayApi.GetCandidateDllPaths(DllName))
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AICDispResolution
    {
        public int Width;
        public int Height;
        public int Refresh;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AICDispPicture
    {
        public int Width;
        public int Height;
        public IntPtr Data;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AttachProcHandler(uint handle, IntPtr resolution, int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DetachProcHandler(uint handle, IntPtr resolution);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int AICDispStart();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int AICDispStop();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void AICDispRegisterCallback(IntPtr attach_func, IntPtr detach_func);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int AICDispSendPicture(uint handle, IntPtr pic);
}
