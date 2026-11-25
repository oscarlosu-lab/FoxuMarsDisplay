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
        // Ensure MsDisplayApi's single assembly-level DllImportResolver is initialized.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MsDisplayApi).TypeHandle);
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
