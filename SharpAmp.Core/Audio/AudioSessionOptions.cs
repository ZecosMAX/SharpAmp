namespace SharpAmp.Core.Audio;

public sealed record AudioSessionOptions(AudioDevice Input, AudioDevice Output)
{
    public int SampleRate { get; init; } = 48000;
    public int InputChannels { get; init; } = 1;
    public int OutputChannels { get; init; } = 2;
    public int AsioInputChannelOffset { get; init; }
    public int AsioOutputChannelOffset { get; init; }
    public int LatencyMilliseconds { get; init; } = 50;
    public int BufferMilliseconds { get; init; } = 250;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Input);
        ArgumentNullException.ThrowIfNull(Output);
        if (Input.Direction != AudioDirection.Input || Output.Direction != AudioDirection.Output)
            throw new ArgumentException("Choose an input device and an output device in that order.");
        if (!Enum.IsDefined(Input.Backend) || !Enum.IsDefined(Output.Backend)) throw new ArgumentException("Unknown audio backend.");
        if (string.IsNullOrWhiteSpace(Input.Id) || string.IsNullOrWhiteSpace(Output.Id)) throw new ArgumentException("Device IDs are required.");
        if (SampleRate is < 8000 or > 192000) throw new ArgumentOutOfRangeException(nameof(SampleRate));
        if (InputChannels is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(InputChannels));
        if (OutputChannels is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(OutputChannels));
        if (LatencyMilliseconds is < 5 or > 1000) throw new ArgumentOutOfRangeException(nameof(LatencyMilliseconds));
        if (BufferMilliseconds < LatencyMilliseconds * 2 || BufferMilliseconds > 5000) throw new ArgumentOutOfRangeException(nameof(BufferMilliseconds), "Buffer must be at least twice the latency and at most 5000 ms.");
        ArgumentOutOfRangeException.ThrowIfNegative(AsioInputChannelOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(AsioOutputChannelOffset);
        if (Input.Backend != AudioBackend.Asio && AsioInputChannelOffset != 0) throw new ArgumentException("Input offset requires ASIO.");
        if (Output.Backend != AudioBackend.Asio && AsioOutputChannelOffset != 0) throw new ArgumentException("Output offset requires ASIO.");
    }
}
