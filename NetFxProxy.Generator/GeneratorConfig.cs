using System.Text.Json.Serialization;

namespace NetFxProxy.Generator;

public class GeneratorConfig
{
    [JsonPropertyName("assemblySearchPaths")]
    public string[] AssemblySearchPaths { get; set; } = [];

    [JsonPropertyName("assemblies")]
    public AssemblyConfig[] Assemblies { get; set; } = [];

    [JsonPropertyName("outputDirectory")]
    public string OutputDirectory { get; set; } = "./Generated";

    [JsonPropertyName("marshaledTypes")]
    public Dictionary<string, MarshalConfig> MarshaledTypes { get; set; } =
        new Dictionary<string, MarshalConfig>();
}
