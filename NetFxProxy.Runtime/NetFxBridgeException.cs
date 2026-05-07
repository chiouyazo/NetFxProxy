namespace NetFxProxy.Runtime;

public class NetFxBridgeException : Exception
{
    public NetFxBridgeException(string message)
        : base(message) { }

    public NetFxBridgeException(string message, Exception inner)
        : base(message, inner) { }
}
