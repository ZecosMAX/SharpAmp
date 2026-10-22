using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAmp.Avalonia.ViewModels;

internal static class EditorDocuments
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        WriteIndented = true,
        MaxDepth = 16
    };
}

// Optional envelope fields allow existing SharpAmp.Core presets to open unchanged.
internal sealed class EditorPresetDocument
{
    public int? Version { get; init; }
    public string? Name { get; init; }
    public required List<EditorProcessorDocument> Processors { get; init; }
    public List<ProcessorConnection>? Connections { get; init; }
}

/// <summary>A cable between an output port and an input port on the pedalboard.</summary>
public sealed record ProcessorConnection(string SourceId, string TargetId);

internal sealed class EditorProcessorDocument
{
    public string? Id { get; init; }
    public required string Type { get; init; }
    public bool IsBypassed { get; init; }
    public Dictionary<string, float> Parameters { get; init; } = [];
}

internal sealed class EditorLayoutDocument
{
    public required int Version { get; init; }
    public required List<EditorNodePosition> Nodes { get; init; }
}

internal sealed class EditorNodePosition
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}
