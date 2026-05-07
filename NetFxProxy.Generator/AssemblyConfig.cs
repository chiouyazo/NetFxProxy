using System.Text.Json.Serialization;

namespace NetFxProxy.Generator;

public class AssemblyConfig
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("types")]
    public string[] Types { get; set; } = ["*"];
}
