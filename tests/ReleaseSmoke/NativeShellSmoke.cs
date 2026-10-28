using System;
using System.IO;
using System.Runtime.InteropServices;

public static class NativeShellSmoke
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FactoryDelegate(ref Guid clsid, ref Guid iid, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDelegate(IntPtr factory, IntPtr outer, ref Guid iid, out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryDelegate(IntPtr instance, ref Guid iid, out IntPtr queried);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnloadDelegate();

    public static string[] Run(string dllPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var module = NativeLibrary.Load(Path.GetFullPath(dllPath));
        try
        {
            // Resolve registration exports without changing the installed extension.
            NativeLibrary.GetExport(module, "DllRegisterServer");
            NativeLibrary.GetExport(module, "DllUnregisterServer");
            var getFactory = Marshal.GetDelegateForFunctionPointer<FactoryDelegate>(NativeLibrary.GetExport(module, "DllGetClassObject"));
            var clsid = new Guid("2a97e713-bd5f-45c4-a410-e57a3c3d86a4");
            var factoryId = new Guid("00000001-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(getFactory(ref clsid, ref factoryId, out var factory));
            try
            {
                var vtable = Marshal.ReadIntPtr(factory);
                var create = Marshal.GetDelegateForFunctionPointer<CreateDelegate>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
                var contextMenuId = new Guid("000214e4-0000-0000-C000-000000000046");
                Marshal.ThrowExceptionForHR(create(factory, IntPtr.Zero, ref contextMenuId, out var contextMenu));
                try
                {
                    var initializeId = new Guid("000214e8-0000-0000-C000-000000000046");
                    var query = Marshal.GetDelegateForFunctionPointer<QueryDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(contextMenu)));
                    Marshal.ThrowExceptionForHR(query(contextMenu, ref initializeId, out var initialize));
                    Marshal.Release(initialize);
                }
                finally { Marshal.Release(contextMenu); }
            }
            finally { Marshal.Release(factory); }
            var canUnload = Marshal.GetDelegateForFunctionPointer<UnloadDelegate>(NativeLibrary.GetExport(module, "DllCanUnloadNow"));
            if (canUnload() != 0) throw new InvalidDataException("COM objects left the shell DLL locked after release.");
            return new[] { "native DLL load", "registration exports present", "COM class factory", "IContextMenu and IShellExtInit activation", "COM references released and DLL unloadable" };
        }
        finally { NativeLibrary.Free(module); }
    }
}
