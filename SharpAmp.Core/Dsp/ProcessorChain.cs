using SharpAmp.Core.Dsp.Configuration;

namespace SharpAmp.Core.Dsp;

/// <summary>Runs a fixed sequence in declaration order; use on one audio thread at a time.</summary>
public sealed class ProcessorChain : IDigitalSignalProcessor
{
    private readonly IDigitalSignalProcessor[] processors;
    private int channels;

    /// <summary>Create a fresh, uninitialized chain from a JSON preset.</summary>
    public static ProcessorChain FromJson(string json) =>
        FromConfiguration(ProcessorChainConfiguration.FromJson(json));

    /// <summary>Snapshot a preset into independent processors; initialize before processing.</summary>
    public static ProcessorChain FromConfiguration(ProcessorChainConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Processors is null)
            throw new ArgumentException("processors must be an array.", nameof(configuration));
        return new ProcessorChain(configuration.Processors.Select(ProcessorFactory.Create).ToArray());
    }

    public ProcessorChain(params IDigitalSignalProcessor[] processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        if (processors.Any(p => p is null))
            throw new ArgumentException("Processors must not contain null.", nameof(processors));
        this.processors = (IDigitalSignalProcessor[])processors.Clone();
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = 0;
        foreach (var processor in processors)
            processor.Initialize(sampleRate, channels);
        this.channels = channels;
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the chain first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        foreach (var processor in processors)
            processor.Process(samples);
    }

    public void Reset()
    {
        foreach (var processor in processors) processor.Reset();
    }
}
