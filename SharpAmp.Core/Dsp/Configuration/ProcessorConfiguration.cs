namespace SharpAmp.Core.Dsp.Configuration;

/// <summary>An effect and its numeric settings. Names are case-sensitive.</summary>
public sealed class ProcessorConfiguration
{
    public required string Type { get; init; }
    /// <summary>Absolute path to a VST2 DLL or VST3 module/bundle; only valid for vst2/vst3 effects.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginPath { get; init; }
    public Dictionary<string, float> Parameters { get; init; } = new(StringComparer.Ordinal);
}
