using System.Reflection;

namespace NetFxProxy.Generator;

public record AnalyzedType(
    Type Type,
    string FullName,
    string AssemblyFileName,
    bool IsStatic,
    bool IsAbstract,
    bool IsSealed,
    bool IsException,
    ConstructorInfo[] Constructors,
    MethodInfo[] Methods,
    PropertyInfo[] Properties
);
