using System.Reflection;

namespace NetFxProxy.Generator;

public class AssemblyAnalyzer : IDisposable
{
    private readonly MetadataLoadContext _mlc;
    private readonly Dictionary<string, Assembly> _loadedAssemblies =
        new Dictionary<string, Assembly>();

    public AssemblyAnalyzer(string[] searchPaths)
    {
        PathAssemblyResolver resolver = new PathAssemblyResolver(GetAllDlls(searchPaths));
        _mlc = new MetadataLoadContext(resolver);
    }

    private static IEnumerable<string> GetAllDlls(string[] searchPaths)
    {
        foreach (string dir in searchPaths)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (string dll in Directory.GetFiles(dir, "*.dll"))
                yield return dll;
        }

        string? net48RefDir = FindNet48ReferenceAssemblies();
        if (net48RefDir != null)
        {
            foreach (string dll in Directory.GetFiles(net48RefDir, "*.dll"))
                yield return dll;

            string facadesDir = Path.Combine(net48RefDir, "Facades");
            if (Directory.Exists(facadesDir))
                foreach (string dll in Directory.GetFiles(facadesDir, "*.dll"))
                    yield return dll;
        }
        else
        {
            throw new InvalidOperationException(
                ".NET Framework 4.8 reference assemblies not found. "
                    + "Install the Microsoft.NETFramework.ReferenceAssemblies.net48 NuGet package "
                    + "or the .NET Framework 4.8 targeting pack."
            );
        }
    }

    private static string? FindNet48ReferenceAssemblies()
    {
        string windowsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
        );
        if (Directory.Exists(windowsPath))
            return windowsPath;

        string nugetCache =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages"
            );

        string nugetPath = Path.Combine(
            nugetCache,
            "microsoft.netframework.referenceassemblies.net48",
            "1.0.3",
            "build",
            ".NETFramework",
            "v4.8"
        );
        if (Directory.Exists(nugetPath))
            return nugetPath;

        return null;
    }

    public List<AnalyzedType> Analyze(AssemblyConfig config, string[] searchPaths)
    {
        string? asmPath = null;
        foreach (string dir in searchPaths)
        {
            string candidate = Path.Combine(dir, config.Path);
            if (File.Exists(candidate))
            {
                asmPath = candidate;
                break;
            }
        }
        if (asmPath == null)
            throw new FileNotFoundException($"Assembly not found: {config.Path}");

        Assembly assembly = _mlc.LoadFromAssemblyPath(asmPath);
        string asmFileName = Path.GetFileName(config.Path);
        bool allTypes = config.Types.Length == 1 && config.Types[0] == "*";
        HashSet<string> typeFilter = new HashSet<string>(config.Types);

        List<AnalyzedType> results = new List<AnalyzedType>();

        foreach (Type type in assembly.GetExportedTypes())
        {
            try
            {
                if (!allTypes && !typeFilter.Contains(type.FullName!))
                    continue;

                if (type.IsGenericType || type.IsInterface)
                    continue;

                try
                {
                    if (type.BaseType?.FullName == "System.MulticastDelegate")
                        continue;
                }
                catch
                { /* MetadataLoadContext may fail to resolve the base type */
                }

                bool isStatic = type.IsAbstract && type.IsSealed;
                bool isException = false;
                try
                {
                    isException = InheritsFrom(type, "System.Exception");
                }
                catch
                { /* MetadataLoadContext may fail to walk the type hierarchy */
                }

                ConstructorInfo[] ctors = Array.Empty<ConstructorInfo>();
                if (!isStatic)
                {
                    try
                    {
                        ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    }
                    catch
                    { /* MetadataLoadContext may fail to resolve constructor parameter types */
                    }
                }

                MethodInfo[] methods = Array.Empty<MethodInfo>();
                try
                {
                    methods = type.GetMethods(
                            BindingFlags.Public
                                | (
                                    isStatic
                                        ? BindingFlags.Static
                                        : BindingFlags.Instance | BindingFlags.Static
                                )
                        )
                        .Where(m => !m.IsSpecialName)
                        .Where(m =>
                        {
                            try
                            {
                                string? declType = m.DeclaringType?.FullName;
                                if (declType == "System.Object")
                                    return false;
                                if (
                                    m.Name
                                    is "Equals"
                                        or "GetHashCode"
                                        or "ToString"
                                        or "GetType"
                                        or "Dispose"
                                )
                                    return false;
                                if (m.Name == type.Name)
                                    return false;
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        })
                        .Where(m =>
                        {
                            try
                            {
                                if (m.IsGenericMethod)
                                    return false;
                                _ = m.GetParameters();
                                _ = m.ReturnType;
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        })
                        .ToArray();
                }
                catch
                { /* MetadataLoadContext may fail to resolve method signatures */
                }

                PropertyInfo[] props = Array.Empty<PropertyInfo>();
                try
                {
                    props = type.GetProperties(
                            BindingFlags.Public
                                | (isStatic ? BindingFlags.Static : BindingFlags.Instance)
                        )
                        .Where(p => p.DeclaringType == type)
                        .Where(p =>
                        {
                            try
                            {
                                _ = p.PropertyType;
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        })
                        .ToArray();
                }
                catch
                { /* MetadataLoadContext may fail to resolve property types */
                }

                results.Add(
                    new AnalyzedType(
                        type,
                        type.FullName!,
                        asmFileName,
                        isStatic,
                        type.IsAbstract,
                        type.IsSealed,
                        isException,
                        ctors,
                        methods,
                        props
                    )
                );
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: skipping type {type.FullName}: {ex.Message}");
            }
        }

        return results;
    }

    public List<AnalyzedType> AnalyzeSingleType(string fullTypeName, string[] searchPaths)
    {
        List<string> allSearchPaths = new List<string>(searchPaths);
        string? net48RefDir = FindNet48ReferenceAssemblies();
        if (net48RefDir != null)
        {
            allSearchPaths.Add(net48RefDir);
            string facadesDir = Path.Combine(net48RefDir, "Facades");
            if (Directory.Exists(facadesDir))
                allSearchPaths.Add(facadesDir);
        }

        try
        {
            foreach (Assembly loadedAsm in _mlc.GetAssemblies())
            {
                try
                {
                    Type? type = loadedAsm.GetType(fullTypeName, throwOnError: false);
                    if (type == null)
                        continue;

                    string asmLocation = loadedAsm.Location ?? "";
                    string asmFileName = Path.GetFileName(asmLocation);
                    if (string.IsNullOrEmpty(asmFileName))
                        asmFileName = loadedAsm.GetName().Name + ".dll";

                    return AnalyzeSpecificType(type, asmFileName, searchPaths);
                }
                catch
                { /* MetadataLoadContext may fail on some assemblies */
                }
            }
        }
        catch
        { /* GetAssemblies() can fail if assembly metadata is corrupt */
        }

        foreach (string dir in allSearchPaths)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (string dllPath in Directory.GetFiles(dir, "*.dll"))
            {
                try
                {
                    Assembly assembly = _mlc.LoadFromAssemblyPath(dllPath);
                    Type? type = assembly.GetType(fullTypeName, throwOnError: false);
                    if (type == null)
                        continue;

                    string asmFileName = Path.GetFileName(dllPath);
                    return AnalyzeSpecificType(type, asmFileName, searchPaths);
                }
                catch
                { /* Skip DLLs that can't be loaded by MetadataLoadContext */
                }
            }
        }

        Console.Error.WriteLine($"  Warning: could not find type {fullTypeName} in any assembly");
        return new List<AnalyzedType>();
    }

    private List<AnalyzedType> AnalyzeSpecificType(
        Type type,
        string asmFileName,
        string[] searchPaths
    )
    {
        List<AnalyzedType> results = new List<AnalyzedType>();
        string fullTypeName = type.FullName!;

        try
        {
            if (type.IsGenericType || type.IsInterface)
                return results;
            try
            {
                if (type.BaseType?.FullName == "System.MulticastDelegate")
                    return results;
            }
            catch
            { /* MetadataLoadContext may fail to resolve the base type */
            }

            bool isStatic = type.IsAbstract && type.IsSealed;
            bool isException = false;
            try
            {
                Type? current = type.BaseType;
                while (current != null)
                {
                    if (current.FullName == "System.Exception")
                    {
                        isException = true;
                        break;
                    }
                    current = current.BaseType;
                }
            }
            catch
            { /* MetadataLoadContext may fail to walk the type hierarchy */
            }

            ConstructorInfo[] ctors = Array.Empty<ConstructorInfo>();
            if (!isStatic)
            {
                try
                {
                    ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                }
                catch
                { /* MetadataLoadContext may fail to resolve constructor parameter types */
                }
            }

            MethodInfo[] methods = Array.Empty<MethodInfo>();
            try
            {
                methods = type.GetMethods(
                        BindingFlags.Public
                            | (
                                isStatic
                                    ? BindingFlags.Static
                                    : BindingFlags.Instance | BindingFlags.Static
                            )
                    )
                    .Where(m => !m.IsSpecialName)
                    .Where(m =>
                    {
                        try
                        {
                            string? declType = m.DeclaringType?.FullName;
                            if (declType == "System.Object")
                                return false;
                            if (m.Name is "Equals" or "GetHashCode" or "ToString" or "GetType")
                                return false;
                            if (m.Name == type.Name)
                                return false;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .Where(m =>
                    {
                        try
                        {
                            _ = m.GetParameters();
                            _ = m.ReturnType;
                            return !m.IsGenericMethod;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ToArray();
            }
            catch
            { /* MetadataLoadContext may fail to resolve method signatures */
            }

            PropertyInfo[] props = Array.Empty<PropertyInfo>();
            try
            {
                props = type.GetProperties(
                        BindingFlags.Public
                            | (isStatic ? BindingFlags.Static : BindingFlags.Instance)
                    )
                    .Where(p => p.DeclaringType == type)
                    .Where(p =>
                    {
                        try
                        {
                            _ = p.PropertyType;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ToArray();
            }
            catch
            { /* MetadataLoadContext may fail to resolve property types */
            }

            results.Add(
                new AnalyzedType(
                    type,
                    fullTypeName,
                    asmFileName,
                    isStatic,
                    type.IsAbstract,
                    type.IsSealed,
                    isException,
                    ctors,
                    methods,
                    props
                )
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: failed to analyze {fullTypeName}: {ex.Message}");
        }

        return results;
    }

    private static bool InheritsFrom(Type type, string baseTypeName)
    {
        Type? current = type.BaseType;
        while (current != null)
        {
            if (current.FullName == baseTypeName)
                return true;
            current = current.BaseType;
        }
        return false;
    }

    public void Dispose() => _mlc.Dispose();
}
