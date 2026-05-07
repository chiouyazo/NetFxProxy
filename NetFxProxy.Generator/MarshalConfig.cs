using System.Text.Json.Serialization;

namespace NetFxProxy.Generator;

public class MarshalConfig
{
    [JsonPropertyName("net10Type")]
    public string Net10Type { get; set; } = "";

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "direct";

    [JsonPropertyName("extractProperty")]
    public string? ExtractProperty { get; set; }
}
