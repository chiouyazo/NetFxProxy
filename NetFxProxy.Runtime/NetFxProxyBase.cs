namespace NetFxProxy.Runtime;

public abstract class NetFxProxyBase : IDisposable
{
    public NetFxObjectHandle Handle { get; }

    protected NetFxProxyBase(NetFxObjectHandle handle)
    {
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
    }

    public void Dispose()
    {
        Handle.Dispose();
        GC.SuppressFinalize(this);
    }

    ~NetFxProxyBase()
    {
        Handle.Dispose();
    }
}
