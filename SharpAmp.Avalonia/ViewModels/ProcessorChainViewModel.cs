using System.Collections.Generic;
using System.Linq;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Configuration;

namespace SharpAmp.Avalonia.ViewModels;

public sealed class ProcessorChainViewModel : ViewModelBase
{
    // Start with the library's demo chain. Every parameter is included in the session snapshot.
    public IReadOnlyList<ProcessorViewModel> Processors { get; } = new ProcessorViewModel[]
    {
        new("hardOverdrive", "Hard overdrive", "DRIVE / HARD CLIP",
            new("drive", "Drive", 0.1, 20, 0.1, 5),
            new("outputGain", "Output gain", 0, 1, 0.01, 0.35)),
        new("reverb", "Reverb", "SPACE / ROOM",
            new("wet", "Wet mix", 0, 1, 0.01, 0.2),
            new("feedback", "Feedback", 0, 0.99, 0.01, 0.7),
            new("damping", "Damping", 0, 0.99, 0.01, 0.3))
    };

    public ProcessorChain CreateChain() => ProcessorChain.FromConfiguration(new ProcessorChainConfiguration
    {
        Processors = Processors.Select(processor => processor.ToConfiguration()).ToList()
    });
}
