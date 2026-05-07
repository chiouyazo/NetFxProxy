namespace NetFxProxy.Runtime;

public sealed class NetFxBridge : IDisposable
{
    private static NetFxBridge? _instance;
    public static NetFxBridge Instance =>
        _instance
        ?? throw new InvalidOperationException(
            "NetFxBridge not initialized. Call NetFxBridge.Initialize() first."
        );

    private readonly Dictionary<string, int> _assemblyHandles = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _typeHandles = new Dictionary<string, int>();
    private bool _disposed;

    private NetFxBridge() { }

    public static NetFxBridge Initialize(string nativeDllPath, string? assemblySearchPath = null)
    {
        if (_instance != null)
            return _instance;

        string resolvedPath = Path.GetFullPath(nativeDllPath);
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(
            typeof(NativeInterop).Assembly,
            (name, assembly, searchPath) =>
            {
                if (name == "NetFxProxy")
                    return System.Runtime.InteropServices.NativeLibrary.Load(resolvedPath);
                return IntPtr.Zero;
            }
        );

        try
        {
            int rc = NativeInterop.NetFxProxy_Init(nativeDllPath);
            if (rc != 0)
                throw new NetFxBridgeException($"Failed to initialize bridge: {GetLastError()}");
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_Init failed: {ex.Message}",
                ex
            );
        }

        _instance = new NetFxBridge();
        return _instance;
    }

    public string GetClrVersion()
    {
        try
        {
            char[] buf = new char[256];
            NativeInterop.NetFxProxy_GetClrVersion(buf, buf.Length);
            return new string(buf).TrimEnd('\0');
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_GetClrVersion failed: {ex.Message}",
                ex
            );
        }
    }

    public int LoadAssembly(string path)
    {
        if (_assemblyHandles.TryGetValue(path, out int cached))
            return cached;

        try
        {
            int rc = NativeInterop.NetFxProxy_LoadAssembly(path, out int handle);
            if (rc != 0)
                throw new NetFxBridgeException(
                    $"Failed to load assembly '{path}': {GetLastError()}"
                );

            _assemblyHandles[path] = handle;
            return handle;
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_LoadAssembly failed for '{path}': {ex.Message}",
                ex
            );
        }
    }

    public int ResolveType(string assemblyPath, string fullTypeName)
    {
        string key = $"{assemblyPath}::{fullTypeName}";
        if (_typeHandles.TryGetValue(key, out int cached))
            return cached;

        try
        {
            int asmHandle = LoadAssembly(assemblyPath);
            int rc = NativeInterop.NetFxProxy_ResolveType(
                asmHandle,
                fullTypeName,
                out int typeHandle
            );
            if (rc != 0)
                throw new NetFxBridgeException(
                    $"Failed to resolve type '{fullTypeName}': {GetLastError()}"
                );

            _typeHandles[key] = typeHandle;
            return typeHandle;
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_ResolveType failed for '{fullTypeName}': {ex.Message}",
                ex
            );
        }
    }

    public NetFxObjectHandle CreateInstance(int typeHandle, params NetFxArg[] args)
    {
        try
        {
            int rc = NativeInterop.NetFxProxy_CreateInstance(
                typeHandle,
                args,
                args.Length,
                out int objHandle
            );
            if (rc != 0)
                throw new NetFxBridgeException($"Failed to create instance: {GetLastError()}");
            return new NetFxObjectHandle(objHandle);
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_CreateInstance failed: {ex.Message}",
                ex
            );
        }
    }

    public NetFxResult InvokeStatic(int typeHandle, string methodName, params NetFxArg[] args)
    {
        try
        {
            int rc = NativeInterop.NetFxProxy_InvokeStatic(
                typeHandle,
                methodName,
                args,
                args.Length,
                out NetFxResult result
            );
            if (rc != 0 || result.IsError)
            {
                string msg = result.IsError ? result.GetErrorMessage() : GetLastError();
                throw new NetFxBridgeException($"Static method '{methodName}' failed: {msg}");
            }
            return result;
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_InvokeStatic failed for '{methodName}': {ex.Message}",
                ex
            );
        }
    }

    public NetFxResult InvokeInstance(
        NetFxObjectHandle handle,
        string methodName,
        params NetFxArg[] args
    )
    {
        try
        {
            NetFxResult[] refResults = new NetFxResult[args.Length];

            int rc = NativeInterop.NetFxProxy_InvokeInstance(
                handle.Id,
                methodName,
                args,
                args.Length,
                out NetFxResult result,
                refResults
            );

            if (rc != 0 || result.IsError)
            {
                string msg = result.IsError ? result.GetErrorMessage() : GetLastError();
                throw new NetFxBridgeException($"Instance method '{methodName}' failed: {msg}");
            }
            return result;
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_InvokeInstance failed for '{methodName}': {ex.Message}",
                ex
            );
        }
    }

    public NetFxResult InvokeInstanceWithRefs(
        NetFxObjectHandle handle,
        string methodName,
        NetFxArg[] args,
        out NetFxResult[] refResults
    )
    {
        refResults = new NetFxResult[args.Length];
        try
        {
            int rc = NativeInterop.NetFxProxy_InvokeInstance(
                handle.Id,
                methodName,
                args,
                args.Length,
                out NetFxResult result,
                refResults
            );

            if (rc != 0 || result.IsError)
            {
                string msg = result.IsError ? result.GetErrorMessage() : GetLastError();
                throw new NetFxBridgeException($"Instance method '{methodName}' failed: {msg}");
            }
            return result;
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_InvokeInstance failed for '{methodName}': {ex.Message}",
                ex
            );
        }
    }

    public NetFxResult GetProperty(NetFxObjectHandle handle, string propertyName)
    {
        try
        {
            int rc = NativeInterop.NetFxProxy_GetProperty(
                handle.Id,
                propertyName,
                out NetFxResult result
            );
            if (rc != 0 || result.IsError)
            {
                string msg = result.IsError ? result.GetErrorMessage() : GetLastError();
                throw new NetFxBridgeException($"Get property '{propertyName}' failed: {msg}");
            }
            return result;
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_GetProperty failed for '{propertyName}': {ex.Message}",
                ex
            );
        }
    }

    public void SetProperty(NetFxObjectHandle handle, string propertyName, NetFxArg value)
    {
        try
        {
            int rc = NativeInterop.NetFxProxy_SetProperty(handle.Id, propertyName, ref value);
            if (rc != 0)
                throw new NetFxBridgeException(
                    $"Set property '{propertyName}' failed: {GetLastError()}"
                );
        }
        catch (NetFxBridgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetFxBridgeException(
                $"P/Invoke call NetFxProxy_SetProperty failed for '{propertyName}': {ex.Message}",
                ex
            );
        }
    }

    public int HandleCount
    {
        get
        {
            try
            {
                return NativeInterop.NetFxProxy_HandleCount();
            }
            catch (NetFxBridgeException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new NetFxBridgeException(
                    $"P/Invoke call NetFxProxy_HandleCount failed: {ex.Message}",
                    ex
                );
            }
        }
    }

    private static string GetLastError()
    {
        char[] buf = new char[4096];
        NativeInterop.NetFxProxy_GetLastError(buf, buf.Length);
        return new string(buf).TrimEnd('\0');
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _instance = null;
    }
}
