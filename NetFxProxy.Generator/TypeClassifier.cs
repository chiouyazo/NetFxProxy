using System.Reflection;

namespace NetFxProxy.Generator;

public class TypeClassifier
{
    private readonly HashSet<string> _generatedTypes;
    private readonly Dictionary<string, MarshalConfig> _marshaledTypes;

    private static readonly HashSet<string> PrimitiveTypes = new HashSet<string>
    {
        "System.Int32",
        "System.Int64",
        "System.Int16",
        "System.Byte",
        "System.String",
        "System.Boolean",
        "System.Double",
        "System.Single",
        "System.Decimal",
        "System.UInt32",
        "System.UInt64",
        "System.UInt16",
    };

    public TypeClassifier(
        HashSet<string> generatedTypes,
        Dictionary<string, MarshalConfig> marshaledTypes
    )
    {
        _generatedTypes = generatedTypes;
        _marshaledTypes = marshaledTypes;
    }

    public TypeKind Classify(Type type)
    {
        string fullName = type.FullName ?? type.Name;
        if (fullName == "System.Void")
            return TypeKind.Void;
        if (type.IsByRef)
            return Classify(type.GetElementType()!);
        if (PrimitiveTypes.Contains(fullName))
            return TypeKind.Primitive;
        if (type.IsEnum)
            return TypeKind.Enum;
        if (_marshaledTypes.ContainsKey(fullName))
            return TypeKind.Marshaled;
        if (_generatedTypes.Contains(fullName))
            return TypeKind.Generated;
        return TypeKind.Opaque;
    }

    public bool IsExceptionType(Type type)
    {
        Type? current = type.BaseType;
        while (current != null)
        {
            if (current.FullName == "System.Exception")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    public MarshalConfig? GetMarshalConfig(Type type)
    {
        string fullName = type.IsByRef ? type.GetElementType()!.FullName! : type.FullName!;
        _marshaledTypes.TryGetValue(fullName, out MarshalConfig? config);
        return config;
    }

    public string GetCSharpType(Type type)
    {
        if (type.IsByRef)
            return GetCSharpType(type.GetElementType()!);

        string fullName = type.FullName ?? type.Name;
        return fullName switch
        {
            "System.Void" => "void",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Int16" => "short",
            "System.Byte" => "byte",
            "System.UInt32" => "uint",
            "System.UInt64" => "ulong",
            "System.UInt16" => "ushort",
            "System.String" => "string",
            "System.Boolean" => "bool",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Decimal" => "decimal",
            _ when Classify(type) == TypeKind.Generated => fullName,
            _ when Classify(type) == TypeKind.Marshaled => _marshaledTypes[fullName].Net10Type,
            _ => "NetFxProxy.Runtime.NetFxObjectHandle",
        };
    }

    public string GetResultExtractor(Type type)
    {
        string fullName = type.IsByRef ? type.GetElementType()!.FullName! : type.FullName!;
        return fullName switch
        {
            "System.Int32" => ".ToInt32()",
            "System.Int16" => ".ToInt32()",
            "System.Byte" => ".ToInt32()",
            "System.UInt16" => ".ToInt32()",
            "System.Int64" or "System.UInt32" or "System.UInt64" => ".ToInt64()",
            "System.String" => ".AsString()!",
            "System.Boolean" => ".ToBool()",
            "System.Double" => ".ToDouble()",
            "System.Single" => ".ToFloat()",
            "System.Decimal" => ".ToDecimal()",
            _ => ".ToHandle()",
        };
    }

    public string GetNarrowingCast(Type type)
    {
        string fullName = type.IsByRef ? type.GetElementType()!.FullName! : type.FullName!;
        return fullName switch
        {
            "System.Byte" => "(byte)",
            "System.Int16" => "(short)",
            "System.UInt16" => "(ushort)",
            _ => "",
        };
    }

    public string GetArgFactory(Type type)
    {
        string fullName = type.IsByRef ? type.GetElementType()!.FullName! : type.FullName!;
        return fullName switch
        {
            "System.Int32" or "System.Int16" or "System.Byte" or "System.UInt16" =>
                "NetFxArg.Int32",
            "System.Int64" or "System.UInt32" or "System.UInt64" => "NetFxArg.Int64",
            "System.String" => "NetFxArg.String",
            "System.Boolean" => "NetFxArg.Bool",
            "System.Double" => "NetFxArg.Double",
            "System.Single" => "NetFxArg.Float",
            "System.Decimal" => "NetFxArg.Decimal",
            _ => "NetFxArg.Handle",
        };
    }

    public bool CanMarshalParameter(Type type)
    {
        TypeKind kind = Classify(type);
        return kind
            is TypeKind.Primitive
                or TypeKind.Generated
                or TypeKind.Opaque
                or TypeKind.Enum
                or TypeKind.Marshaled;
    }
}
