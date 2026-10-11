using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;

namespace SharpAmp.Cli;

internal sealed record RunOptions
{
    public int? InputIndex { get; init; }
    public int? OutputIndex { get; init; }
    public int SampleRate { get; init; } = 48000;
    public int InputChannels { get; init; } = 1;
    public int OutputChannels { get; init; } = 2;
    public int InputOffset { get; init; }
    public int OutputOffset { get; init; }
    public int LatencyMilliseconds { get; init; } = 50;
    public int BufferMilliseconds { get; init; } = 250;
    public string? ChainPath { get; init; }
    public bool Bypass { get; init; }
    public int Seconds { get; init; }

    public AudioSessionOptions CreateSessionOptions(AudioDevice input, AudioDevice output) => new(input, output)
    {
        SampleRate = SampleRate,
        InputChannels = InputChannels,
        OutputChannels = OutputChannels,
        AsioInputChannelOffset = InputOffset,
        AsioOutputChannelOffset = OutputOffset,
        LatencyMilliseconds = LatencyMilliseconds,
        BufferMilliseconds = BufferMilliseconds
    };

    public ProcessorChain CreateProcessorChain()
    {
        if (Bypass) return new ProcessorChain();
        if (string.IsNullOrWhiteSpace(ChainPath))
            throw new ArgumentException("A JSON preset path is required.", nameof(ChainPath));
        return ProcessorChain.FromJson(File.ReadAllText(ChainPath));
    }
}
