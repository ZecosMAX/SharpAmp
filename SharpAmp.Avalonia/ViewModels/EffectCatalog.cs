using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpAmp.Avalonia.ViewModels;

public sealed record EffectParameterDefinition(string Key, string Name, double Minimum, double Maximum,
    double Step, double Default, string Unit = "");

public sealed record EffectDescriptor(string Type, string Name, string Description, string Category,
    string Accent, IReadOnlyList<EffectParameterDefinition> Parameters)
{
    internal ProcessorViewModel Create(string? id = null, IReadOnlyDictionary<string, float>? values = null)
    {
        var parameters = Parameters.Select(definition =>
        {
            var value = values is not null && values.TryGetValue(definition.Key, out var saved)
                ? (double)saved : definition.Default;
            // DSP permits wider gain/drive ranges than are useful as initial slider bounds.
            return new ProcessorParameterViewModel(definition.Key, definition.Name,
                Math.Min(definition.Minimum, value), Math.Max(definition.Maximum, value),
                definition.Step, value, definition.Unit);
        }).ToArray();
        return new ProcessorViewModel(Type, Name, Description, Category, Accent,
            id ?? Guid.NewGuid().ToString("N"), parameters);
    }
}

public static class EffectCatalog
{
    public static IReadOnlyList<EffectDescriptor> All { get; } = Array.AsReadOnly(new EffectDescriptor[]
    {
        new("gain", "Gain", "Adjust the signal level.", "Utility", "#A4B6C8",
            [new("gain", "Level", 0, 4, 0.01, 1, "×")]),
        new("hardOverdrive", "Hard overdrive", "Punchy, hard-clipped drive.", "Drive", "#F5B775",
            [new("drive", "Drive", 0.1, 20, 0.1, 5, "×"), new("outputGain", "Output level", 0, 1, 0.01, 0.35, "%")]),
        new("distortion", "Distortion", "Smooth saturation and sustain.", "Drive", "#F09D7B",
            [new("drive", "Drive", 0.1, 20, 0.1, 3, "×"), new("outputGain", "Output level", 0, 1, 0.01, 0.35, "%"), new("wet", "Mix", 0, 1, 0.01, 1, "%")]),
        new("octaveFuzz", "Octave fuzz", "Fuzzy octave color for single notes.", "Drive", "#E397B4",
            [new("drive", "Drive", 0.1, 20, 0.1, 5, "×"), new("outputGain", "Output level", 0, 1, 0.01, 0.35, "%"), new("wet", "Mix", 0, 1, 0.01, 1, "%")]),
        new("delay", "Delay", "Clean, repeating echoes.", "Space", "#75C8C4",
            [new("timeMs", "Time", 1, 2000, 1, 300, "ms"), new("feedback", "Repeats", 0, 0.99, 0.01, 0.35, "%"), new("wet", "Mix", 0, 1, 0.01, 0.25, "%")]),
        new("tapeDelay", "Tape delay", "Warm echoes with gentle pitch drift.", "Space", "#8BC9A4",
            [new("timeMs", "Time", 1, 2000, 1, 350, "ms"), new("feedback", "Repeats", 0, 0.99, 0.01, 0.4, "%"), new("wet", "Mix", 0, 1, 0.01, 0.25, "%"),
                new("toneHz", "Tone", 20, 20000, 10, 3000, "Hz"), new("wowDepthMs", "Pitch drift", 0, 10, 0.1, 2, "ms"), new("wowRateHz", "Drift speed", 0, 10, 0.1, 0.5, "Hz")]),
        new("reverb", "Reverb", "Give your sound a sense of space.", "Space", "#98AFF1",
            [new("wet", "Mix", 0, 1, 0.01, 0.2, "%"), new("feedback", "Room decay", 0, 0.99, 0.01, 0.7, "%"), new("damping", "Damping", 0, 0.99, 0.01, 0.3, "%")]),
        new("tremolo", "Tremolo", "A gentle pulse or rhythmic movement.", "Motion", "#BD9FE8",
            [new("rateHz", "Speed", 0, 20, 0.1, 4, "Hz"), new("depth", "Depth", 0, 1, 0.01, 0.5, "%")]),
        new("bitcrusher", "Bitcrusher", "Crunchy, low-resolution texture.", "Texture", "#DAA3DC",
            [new("bits", "Bit depth", 2, 16, 1, 8, "bit"), new("downsample", "Rate reduction", 1, 64, 1, 4, "×"), new("wet", "Mix", 0, 1, 0.01, 1, "%")]),
        new("ringModulator", "Ring modulator", "Metallic tones and bell-like color.", "Texture", "#DAC483",
            [new("frequencyHz", "Frequency", 0, 20000, 1, 440, "Hz"), new("wet", "Mix", 0, 1, 0.01, 0.5, "%")])
    });

    public static EffectDescriptor Find(string type) => All.FirstOrDefault(effect => effect.Type == type)
        ?? throw new ArgumentException($"Unknown effect '{type}'.", nameof(type));
}
