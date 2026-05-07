namespace NetFxProxy.Runtime;

public sealed class NetFxObjectHandle : IDisposable
{
    public int Id { get; private set; }
    private bool _disposed;

    internal NetFxObjectHandle(int id)
    {
        Id = id;
    }

    public bool IsValid => Id != 0 && !_disposed;

    public NetFxResult GetProperty(string propertyName)
    {
        return NetFxBridge.Instance.GetProperty(this, propertyName);
    }

    public void SetProperty(string propertyName, NetFxArg value)
    {
        NetFxBridge.Instance.SetProperty(this, propertyName, value);
    }

    public NetFxResult Invoke(string methodName, params NetFxArg[] args)
    {
        return NetFxBridge.Instance.InvokeInstance(this, methodName, args);
    }

    public void Dispose()
    {
        if (_disposed || Id == 0)
            return;
        _disposed = true;
        NativeInterop.NetFxProxy_ReleaseHandle(Id);
        Id = 0;
    }

    ~NetFxObjectHandle()
    {
        if (!_disposed && Id != 0)
        {
            _disposed = true;
            // Throwing from a finalizer would crash the process
            try
            {
                NativeInterop.NetFxProxy_ReleaseHandle(Id);
            }
            catch
            { /* Swallowed to prevent finalizer crash */
            }
        }
    }
}
