# NetFxProxy

Call .NET Framework 4.8 assemblies from .NET 10 without IPC, separate processes, or modifying the original DLLs.

NetFxProxy hosts the .NET Framework 4.8 CLR alongside .NET 10 CoreCLR in the same process using a C++/CLI bridge. A code generator reads .NET 4.8 assembly metadata and produces .NET 10 replacement types with identical namespaces - so your code looks the same as if the assemblies loaded natively.

## How it works

```
  .NET 10 (CoreCLR)                       .NET Framework 4.8 (CLR)
  ----------------                        ------------------------
  Your code                               Original .NET 4.8 DLLs
      |                                       ^
      v                                       |
  Generated types                             |
  (same namespaces)                           |
      |                                       |
      v                                       |
  NetFxProxy.Runtime                          |
      |                                       |
      v                                       |
  P/Invoke ---------> NetFxProxy.dll ---------+
                      (C++/CLI bridge)
```

Both CLRs run in the same process. The C++/CLI bridge DLL loads the .NET 4.8 CLR automatically and forwards calls via reflection. The generated types handle all marshaling transparently.

## Packages

| Package | Description |
|---|---|
| `NetFxProxy.Runtime` | Runtime bridge library. Reference this in your .NET 10 project. Includes the native C++/CLI DLL for win-x64. |
| `NetFxProxy.Generator` | Code generation tool. Reads .NET 4.8 DLL metadata and generates .NET 10 replacement types. Install as a dotnet tool. |

## Quick start

### 1. Install the generator

```bash
dotnet tool install --global NetFxProxy.Generator
```

### 2. Create a config file

Create `proxygen.json` pointing at your .NET 4.8 assemblies:

```json
{
  "assemblySearchPaths": [
    "./lib/net48"
  ],
  "assemblies": [
    {
      "path": "MyLegacyLib.dll",
      "types": ["*"]
    }
  ],
  "outputDirectory": "./Generated",
  "marshaledTypes": {
    "System.Data.SqlClient.SqlConnection": {
      "net10Type": "System.Data.SqlClient.SqlConnection",
      "strategy": "connectionString",
      "extractProperty": "ConnectionString"
    }
  }
}
```

### 3. Generate the types

```bash
netfxproxy-gen --config proxygen.json
```

This reads the .NET 4.8 DLL metadata and generates `.g.cs` files with the same namespaces and type names as the originals. Referenced types from the same assemblies are auto-discovered recursively.

### 4. Use in your project

Add the NuGet package and the generated files to your .NET 10 project:

```xml
<PackageReference Include="NetFxProxy.Runtime" Version="0.1.0" />
```

Initialize the bridge once at startup:

```csharp
using NetFxProxy.Runtime;

// Path to the native DLL (next to the .NET 4.8 assemblies)
NetFxBridge.Initialize("path/to/NetFxProxy.dll");
```

Then use the generated types like normal code:

```csharp
using MyLegacyLib;

MyService service = new MyService();
string result = service.DoSomething("input");
```

No bridge calls, no reflection, no `.GetProperty()`. The generated types forward everything through the bridge automatically.

## Config reference

### `proxygen.json`

| Field | Description |
|---|---|
| `assemblySearchPaths` | Directories containing the .NET 4.8 DLLs |
| `assemblies` | Which assemblies and types to generate. Use `["*"]` for all public types in an assembly. |
| `outputDirectory` | Where to write the generated `.g.cs` files |
| `marshaledTypes` | Optional. Maps .NET 4.8 types to .NET 10 equivalents with a conversion strategy. |

### Marshaled types

For types that exist natively in .NET 10 (like `SqlConnection`), you can configure automatic conversion instead of generating a proxy:

```json
{
  "marshaledTypes": {
    "System.Data.SqlClient.SqlConnection": {
      "net10Type": "System.Data.SqlClient.SqlConnection",
      "strategy": "connectionString",
      "extractProperty": "ConnectionString"
    }
  }
}
```

The generated `Connect()` method will call the .NET 4.8 method, extract the connection string, and return a real .NET 10 `SqlConnection`.

Types not configured here and not in the generated set are returned as `NetFxObjectHandle` - an opaque handle you can read properties from using `.GetProperty("Name").AsString()`.

## Type discovery

The generator automatically discovers types referenced by the ones you configure. If `MyService.GetConnection()` returns `MyConnection`, and `MyConnection` is in the same assembly search path, it gets generated too. This continues recursively until no new types are found.

Types from `System.*` and `Microsoft.*` namespaces are not auto-discovered (they'd pull in the entire BCL). Configure them in `marshaledTypes` if needed, or they'll be `NetFxObjectHandle`.

## Requirements

- .NET 10 or later
- Windows x64 (the C++/CLI bridge is a native Windows DLL)
- .NET Framework 4.8 must be installed on the machine (it hosts the CLR)

## Runtime requirements

The original .NET 4.8 assemblies must be present at runtime. The generated types are thin wrappers that forward calls into the .NET 4.8 CLR where the real code runs. Deploy them alongside your application or in a known path that you pass to `NetFxBridge.Initialize()`.

## Building the native DLL

The C++/CLI bridge (`NetFxProxy.dll`) is compiled from `NetFxProxy.Native/NetFxProxy.cpp` during the CI pipeline and bundled into the `NetFxProxy.Runtime` NuGet package. Consumers of the NuGet package do not need any C++ tools.

To build locally:

```bash
cd NetFxProxy.Native
build.bat
```

Requires Visual Studio with C++ and C++/CLI support.

## License

Apache-2.0
