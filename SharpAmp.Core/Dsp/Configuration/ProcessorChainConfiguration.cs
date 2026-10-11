using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAmp.Core.Dsp.Configuration;

/// <summary>A serializable preset. An empty processor list represents bypass.</summary>
public sealed class ProcessorChainConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        WriteIndented = true
    };

    public required List<ProcessorConfiguration> Processors { get; init; }

    /// <summary>Read a preset; effect names and parameter ranges are checked when building the chain.</summary>
    public static ProcessorChainConfiguration FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ProcessorChainConfiguration>(json, JsonOptions)
            ?? throw new JsonException("The chain configuration must be an object.");
    }

    /// <summary>Serialize the preset for storage; audio history is never serialized.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
