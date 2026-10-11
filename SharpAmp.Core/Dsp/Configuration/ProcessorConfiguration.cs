namespace SharpAmp.Core.Dsp.Configuration;

/// <summary>A built-in effect and its numeric settings. Names are case-sensitive.</summary>
public sealed class ProcessorConfiguration
{
    public required string Type { get; init; }
    public Dictionary<string, float> Parameters { get; init; } = new(StringComparer.Ordinal);
}
