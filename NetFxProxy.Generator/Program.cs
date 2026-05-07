using System.Reflection;
using System.Text.Json;
using NetFxProxy.Generator;

if (args.Length < 2 || args[0] != "--config")
{
    Console.Error.WriteLine("Usage: NetFxProxy.Generator --config <path-to-config.json>");
    return 1;
}

string configPath = Path.GetFullPath(args[1]);
string configDir = Path.GetDirectoryName(configPath)!;

Console.WriteLine($"Config: {configPath}");
GeneratorConfig config =
    JsonSerializer.Deserialize<GeneratorConfig>(File.ReadAllText(configPath))
    ?? throw new Exception("Failed to parse config.");

string[] searchPaths = config
    .AssemblySearchPaths.Select(p => Path.GetFullPath(Path.Combine(configDir, p)))
    .ToArray();

string outputDir = Path.GetFullPath(Path.Combine(configDir, config.OutputDirectory));
Directory.CreateDirectory(outputDir);

Console.WriteLine($"Search: {string.Join(", ", searchPaths)}");
Console.WriteLine($"Output: {outputDir}");
Console.WriteLine();

using AssemblyAnalyzer analyzer = new AssemblyAnalyzer(searchPaths);
List<AnalyzedType> allTypes = new List<AnalyzedType>();
HashSet<string> generatedTypeNames = new HashSet<string>();

foreach (AssemblyConfig asmConfig in config.Assemblies)
{
    List<AnalyzedType> types = analyzer.Analyze(asmConfig, searchPaths);
    allTypes.AddRange(types);
    foreach (AnalyzedType t in types)
        generatedTypeNames.Add(t.FullName);
}

Console.WriteLine($"Configured: {generatedTypeNames.Count} types");

HashSet<string> primitives = new HashSet<string>
{
    "System.Void",
    "System.Int32",
    "System.Int64",
    "System.Int16",
    "System.Byte",
    "System.String",
    "System.Boolean",
    "System.Double",
    "System.Single",
    "System.Decimal",
    "System.DateTime",
    "System.Object",
    "System.UInt32",
    "System.UInt64",
    "System.UInt16",
};

HashSet<string> marshaledTypeNames = new HashSet<string>(config.MarshaledTypes.Keys);
HashSet<string> unfoundTypes = new HashSet<string>();
int round = 0;

while (true)
{
    HashSet<string> discovered = new HashSet<string>();

    foreach (AnalyzedType analyzed in allTypes)
    {
        foreach (MethodInfo method in analyzed.Methods)
        {
            ScanType(
                method.ReturnType,
                generatedTypeNames,
                primitives,
                marshaledTypeNames,
                unfoundTypes,
                discovered
            );
            foreach (ParameterInfo p in method.GetParameters())
                ScanType(
                    p.ParameterType,
                    generatedTypeNames,
                    primitives,
                    marshaledTypeNames,
                    unfoundTypes,
                    discovered
                );
        }
        foreach (PropertyInfo prop in analyzed.Properties)
            ScanType(
                prop.PropertyType,
                generatedTypeNames,
                primitives,
                marshaledTypeNames,
                unfoundTypes,
                discovered
            );
        foreach (ConstructorInfo ctor in analyzed.Constructors)
        foreach (ParameterInfo p in ctor.GetParameters())
            ScanType(
                p.ParameterType,
                generatedTypeNames,
                primitives,
                marshaledTypeNames,
                unfoundTypes,
                discovered
            );
    }

    if (discovered.Count == 0)
        break;
    round++;

    Console.WriteLine($"Discovery round {round}: +{discovered.Count} types");
    List<string> unfound = new List<string>();

    foreach (string typeName in discovered)
    {
        generatedTypeNames.Add(typeName);
        List<AnalyzedType> found = analyzer.AnalyzeSingleType(typeName, searchPaths);
        if (found.Count > 0)
        {
            allTypes.AddRange(found);
            Console.WriteLine($"  + {typeName}");
        }
        else
        {
            unfound.Add(typeName);
        }
    }

    foreach (string name in unfound)
    {
        generatedTypeNames.Remove(name);
        unfoundTypes.Add(name);
    }
}

Console.WriteLine($"\nTotal: {generatedTypeNames.Count} types to generate\n");

TypeClassifier classifier = new TypeClassifier(generatedTypeNames, config.MarshaledTypes);
ProxyEmitter emitter = new ProxyEmitter(classifier);
int fileCount = 0;

foreach (AnalyzedType type in allTypes)
{
    string code = emitter.Emit(type);
    string relPath = type.FullName.Replace('.', Path.DirectorySeparatorChar) + ".g.cs";
    string filePath = Path.Combine(outputDir, relPath);
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    File.WriteAllText(filePath, code);
    Console.WriteLine($"  {relPath}");
    fileCount++;
}

Console.WriteLine($"\nGenerated {fileCount} files in {outputDir}");
return 0;

static void ScanType(
    Type type,
    HashSet<string> generated,
    HashSet<string> primitives,
    HashSet<string> marshaled,
    HashSet<string> unfound,
    HashSet<string> discovered
)
{
    if (type.IsByRef)
    {
        Type? el = type.GetElementType();
        if (el != null)
            ScanType(el, generated, primitives, marshaled, unfound, discovered);
        return;
    }
    string? fullName = type.FullName;
    if (fullName == null)
        return;
    if (
        generated.Contains(fullName)
        || primitives.Contains(fullName)
        || marshaled.Contains(fullName)
        || unfound.Contains(fullName)
        || discovered.Contains(fullName)
    )
        return;
    if (
        type.IsGenericType
        || type.IsInterface
        || type.IsEnum
        || type.IsArray
        || type.IsPointer
        || type.IsNested
    )
        return;
    if (type.IsValueType)
        return;
    if (fullName.StartsWith("System."))
        return;
    if (fullName.StartsWith("Microsoft."))
        return;
    discovered.Add(fullName);
}
