using System.Runtime.InteropServices;

namespace NetFxProxy.Runtime;

[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
public struct NetFxArg
{
    public int Type;
    public long IntVal;
    public double FloatVal;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string? StrVal;

    public const int Null = 0;
    public const int TypeInt32 = 1;
    public const int TypeInt64 = 2;
    public const int TypeString = 3;
    public const int TypeBool = 4;
    public const int TypeDouble = 5;
    public const int TypeHandle = 6;
    public const int TypeRefHandle = 7;
    public const int TypeFloat = 8;
    public const int TypeDecimal = 9;

    public static NetFxArg FromNull() => new NetFxArg { Type = Null };

    public static NetFxArg Int32(int value) => new NetFxArg { Type = TypeInt32, IntVal = value };

    public static NetFxArg Int64(long value) => new NetFxArg { Type = TypeInt64, IntVal = value };

    public static NetFxArg String(string? value) =>
        new NetFxArg { Type = TypeString, StrVal = value };

    public static NetFxArg Bool(bool value) =>
        new NetFxArg { Type = TypeBool, IntVal = value ? 1 : 0 };

    public static NetFxArg Double(double value) =>
        new NetFxArg { Type = TypeDouble, FloatVal = value };

    public static NetFxArg Float(float value) =>
        new NetFxArg { Type = TypeFloat, FloatVal = value };

    public static NetFxArg Decimal(decimal value) =>
        new NetFxArg { Type = TypeDecimal, FloatVal = (double)value };

    public static NetFxArg Handle(NetFxObjectHandle? handle) =>
        new NetFxArg { Type = TypeHandle, IntVal = handle?.Id ?? 0 };

    public static NetFxArg Handle(NetFxProxyBase? proxy) =>
        new NetFxArg { Type = TypeHandle, IntVal = proxy?.Handle.Id ?? 0 };

    public static NetFxArg Handle(object? obj) =>
        obj switch
        {
            null => FromNull(),
            NetFxObjectHandle h => Handle(h),
            NetFxProxyBase p => Handle(p),
            _ => throw new ArgumentException(
                $"Cannot marshal {obj.GetType().Name} as a bridge handle. Use a primitive NetFxArg factory instead."
            ),
        };

    public static NetFxArg RefHandle(NetFxObjectHandle? handle) =>
        new NetFxArg { Type = TypeRefHandle, IntVal = handle?.Id ?? 0 };

    public static NetFxArg RefHandle(NetFxProxyBase? proxy) =>
        new NetFxArg { Type = TypeRefHandle, IntVal = proxy?.Handle.Id ?? 0 };

    public static NetFxArg RefNull() => new NetFxArg { Type = TypeRefHandle, IntVal = 0 };
}
