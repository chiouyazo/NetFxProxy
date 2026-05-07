using System.Runtime.InteropServices;

namespace NetFxProxy.Runtime;

[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
public unsafe struct NetFxResult
{
    public int Type;
    public long IntVal;
    public double FloatVal;
    public fixed char StrBuf[4096];
    public int ErrorCode;

    public readonly bool IsError => ErrorCode != 0;
    public readonly bool IsNull => Type == NetFxArg.Null;
    public readonly bool IsHandle => Type == NetFxArg.TypeHandle;

    public readonly int ToInt32() => (int)IntVal;

    public readonly long ToInt64() => IntVal;

    public readonly bool ToBool() => IntVal != 0;

    public readonly double ToDouble() => FloatVal;

    public readonly float ToFloat() => (float)FloatVal;

    public readonly decimal ToDecimal() => (decimal)FloatVal;

    public readonly string? AsString()
    {
        if (Type == NetFxArg.Null)
            return null;
        fixed (char* p = StrBuf)
        {
            return new string(p);
        }
    }

    public readonly NetFxObjectHandle ToHandle()
    {
        if (Type == NetFxArg.Null)
            throw new NullReferenceException("Result is null.");
        if (Type != NetFxArg.TypeHandle)
            throw new InvalidCastException($"Result type is {Type}, not a handle.");
        return new NetFxObjectHandle((int)IntVal);
    }

    public readonly NetFxObjectHandle? ToHandleOrNull()
    {
        if (Type == NetFxArg.Null)
            return null;
        if (Type != NetFxArg.TypeHandle)
            throw new InvalidCastException($"Result type is {Type}, not a handle.");
        return new NetFxObjectHandle((int)IntVal);
    }

    public readonly string GetErrorMessage()
    {
        fixed (char* p = StrBuf)
        {
            return new string(p);
        }
    }

    public readonly void ThrowIfError()
    {
        if (IsError)
            throw new NetFxBridgeException(GetErrorMessage());
    }
}
