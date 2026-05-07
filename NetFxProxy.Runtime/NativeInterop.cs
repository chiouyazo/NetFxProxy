using System.Runtime.InteropServices;

namespace NetFxProxy.Runtime;

internal static class NativeInterop
{
    private const string Dll = "NetFxProxy";

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_Init(string bridgeDllPath);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_GetLastError(char[] buffer, int maxLen);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_GetClrVersion(char[] buffer, int maxLen);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    internal static extern int NetFxProxy_Ping();

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_LoadAssembly(string path, out int assemblyHandle);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_ResolveType(
        int assemblyHandle,
        string typeName,
        out int typeHandle
    );

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_CreateInstance(
        int typeHandle,
        [In] NetFxArg[] args,
        int argCount,
        out int objectHandle
    );

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_InvokeStatic(
        int typeHandle,
        string methodName,
        [In] NetFxArg[] args,
        int argCount,
        out NetFxResult result
    );

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_InvokeInstance(
        int objectHandle,
        string methodName,
        [In] NetFxArg[] args,
        int argCount,
        out NetFxResult result,
        [Out] NetFxResult[]? refResults
    );

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    internal static extern int NetFxProxy_ReleaseHandle(int handle);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_GetProperty(
        int objectHandle,
        string propertyName,
        out NetFxResult result
    );

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_SetProperty(
        int objectHandle,
        string propertyName,
        [In] ref NetFxArg value
    );

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int NetFxProxy_GetTypeName(int objectHandle, char[] buffer, int maxLen);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    internal static extern int NetFxProxy_HandleCount();
}
