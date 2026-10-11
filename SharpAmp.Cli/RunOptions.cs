using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;

namespace SharpAmp.Cli;

internal sealed record RunOptions
{
    public const float DefaultDrive = 7;
    public const float DefaultWet = 0.2f;

    public int? InputIndex { get; init; }
    public int? OutputIndex { get; init; }
    public int SampleRate { get; init; } = 48000;
    public int InputChannels { get; init; } = 1;
    public int OutputChannels { get; init; } = 2;
    public int InputOffset { get; init; }
    public int OutputOffset { get; init; }
    public int LatencyMilliseconds { get; init; } = 50;
    public int BufferMilliseconds { get; init; } = 250;
    public float Drive { get; init; } = DefaultDrive;
    public float Wet { get; init; } = DefaultWet;
    public bool Bypass { get; init; }
    public string? ChainPath { get; init; }
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

    public ProcessorChain CreateProcessorChain() =>
        Bypass ? new ProcessorChain() : ChainPath is not null
            ? ProcessorChain.FromJson(File.ReadAllText(ChainPath))
            : DemoProcessorChain.Create(Drive, Wet);
}
