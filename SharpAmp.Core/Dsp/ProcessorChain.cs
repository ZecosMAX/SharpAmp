using SharpAmp.Core.Dsp.Configuration;

namespace SharpAmp.Core.Dsp;

/// <summary>
/// Owns and runs a fixed sequence in declaration order; use on one audio thread at a time.
/// Stop processing before disposal. Do not share owned processors with another chain.
/// </summary>
public sealed class ProcessorChain : IDigitalSignalProcessor, IDisposable
{
    private readonly IDigitalSignalProcessor[] processors;
    private int channels;
    private bool isDisposed;

    /// <summary>The processors in execution order. Chain membership cannot be changed.</summary>
    public IReadOnlyList<IDigitalSignalProcessor> Processors { get; }

    /// <summary>Create a fresh, uninitialized chain from a JSON preset.</summary>
    public static ProcessorChain FromJson(string json) =>
        FromConfiguration(ProcessorChainConfiguration.FromJson(json));

    /// <summary>Snapshot a preset into independent processors; initialize before processing.</summary>
    public static ProcessorChain FromConfiguration(ProcessorChainConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Processors is null)
            throw new ArgumentException("processors must be an array.", nameof(configuration));
        var created = new List<IDigitalSignalProcessor>();
        try
        {
            foreach (var configurationItem in configuration.Processors)
                created.Add(ProcessorFactory.Create(configurationItem, created.Count));
            return new ProcessorChain(created.ToArray());
        }
        catch
        {
            // Preserve the construction error, after attempting cleanup of every processor.
            DisposeProcessors(created);
            throw;
        }
    }

    public ProcessorChain(params IDigitalSignalProcessor[] processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        if (processors.Any(p => p is null))
            throw new ArgumentException("Processors must not contain null.", nameof(processors));
        this.processors = (IDigitalSignalProcessor[])processors.Clone();
        Processors = Array.AsReadOnly(this.processors);
    }

    public void Initialize(int sampleRate, int channels)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = 0;
        try
        {
            foreach (var processor in processors)
                processor.Initialize(sampleRate, channels);
        }
        catch
        {
            isDisposed = true;
            DisposeProcessors(processors);
            throw;
        }
        this.channels = channels;
    }

    public void Process(Span<float> samples)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (channels == 0) throw new InvalidOperationException("Initialize the chain first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        foreach (var processor in processors)
            processor.Process(samples);
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        foreach (var processor in processors) processor.Reset();
    }

    /// <summary>Release owned disposable processors. The caller must stop processing first.</summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        channels = 0;
        var errors = DisposeProcessors(processors);
        if (errors is not null) throw new AggregateException("Some processors could not be disposed.", errors);
    }

    private static List<Exception>? DisposeProcessors(IEnumerable<IDigitalSignalProcessor> processors)
    {
        List<Exception>? errors = null;
        var disposed = new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        foreach (var processor in processors)
        {
            if (processor is not IDisposable disposable || !disposed.Add(disposable)) continue;
            try { disposable.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        return errors;
    }
}
