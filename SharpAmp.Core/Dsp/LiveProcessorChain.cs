namespace SharpAmp.Core.Dsp;

/// <summary>
/// Publishes fresh chains from a control thread and crossfades them on one audio thread.
/// Initialize before starting the stream. Replace takes exclusive ownership of a fresh,
/// independent chain; never reuse its processors in another chain or mutate them afterward.
/// Initialize and Reset require a stopped stream. Processing neither locks nor allocates.
/// </summary>
public sealed class LiveProcessorChain : IDigitalSignalProcessor
{
    private const int ScratchFrames = 1024;
    private readonly object publicationGate = new();
    private ProcessorChain published;
    private ProcessorChain? current;
    private ProcessorChain? previous;
    private float[] oldSamples = [];
    private float[] drySamples = [];
    private int sampleRate;
    private int channels;
    private int transitionFrames;
    private int transitionPosition;
    private bool isBypassed;
    private int wetPosition;

    public LiveProcessorChain(ProcessorChain initialChain, bool isBypassed = false)
    {
        ArgumentNullException.ThrowIfNull(initialChain);
        published = initialChain;
        this.isBypassed = isBypassed;
    }

    /// <summary>Crossfades to the dry input; effects continue running to preserve their history.</summary>
    public bool IsBypassed
    {
        get => Volatile.Read(ref isBypassed);
        set => Volatile.Write(ref isBypassed, value);
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        lock (publicationGate)
        {
            this.channels = 0;
            var oldBuffer = new float[checked(ScratchFrames * channels)];
            var dryBuffer = new float[checked(ScratchFrames * channels)];
            published.Initialize(sampleRate, channels);
            oldSamples = oldBuffer;
            drySamples = dryBuffer;
            this.sampleRate = sampleRate;
            transitionFrames = Math.Max(1, sampleRate / 100); // Ten milliseconds.
            transitionPosition = 0;
            current = published;
            previous = null;
            wetPosition = IsBypassed ? 0 : transitionFrames;
            this.channels = channels;
        }
    }

    /// <summary>
    /// Initialize off the audio thread, then publish atomically. A failed initialization
    /// leaves the playing chain intact. Rapid edits coalesce while a crossfade finishes.
    /// </summary>
    public void Replace(ProcessorChain replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        lock (publicationGate)
        {
            if (channels == 0) throw new InvalidOperationException("Initialize the live chain first.");
            if (ReferenceEquals(replacement, published))
                throw new ArgumentException("Publish a fresh, independent chain.", nameof(replacement));
            replacement.Initialize(sampleRate, channels);
            Volatile.Write(ref published, replacement);
        }
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the live chain first.");
        if (samples.Length % channels != 0)
            throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        if (samples.IsEmpty) return;

        // Read once per callback. A concurrent publication cannot change this buffer's chain.
        var next = Volatile.Read(ref published);
        if (previous is null && !ReferenceEquals(next, current))
        {
            previous = current;
            current = next;
            transitionPosition = 0;
        }
        var targetWet = IsBypassed ? 0 : transitionFrames;

        while (!samples.IsEmpty)
        {
            var frames = Math.Min(samples.Length / channels, ScratchFrames);
            if (previous is not null)
                frames = Math.Min(frames, transitionFrames - transitionPosition);
            var count = frames * channels;
            var block = samples[..count];
            var needsDry = wetPosition != transitionFrames || targetWet != transitionFrames;
            if (needsDry) block.CopyTo(drySamples);
            if (previous is not null) block.CopyTo(oldSamples);

            current!.Process(block);
            if (previous is not null)
            {
                var old = oldSamples.AsSpan(0, count);
                previous.Process(old);
                for (var frame = 0; frame < frames; frame++)
                {
                    var amount = (float)(transitionPosition + frame + 1) / transitionFrames;
                    for (var channel = 0; channel < channels; channel++)
                    {
                        var i = frame * channels + channel;
                        block[i] = old[i] + (block[i] - old[i]) * amount;
                    }
                }
                transitionPosition += frames;
                if (transitionPosition == transitionFrames) previous = null;
            }

            if (needsDry)
            {
                for (var frame = 0; frame < frames; frame++)
                {
                    wetPosition += Math.Sign(targetWet - wetPosition);
                    var wetMix = (float)wetPosition / transitionFrames;
                    for (var channel = 0; channel < channels; channel++)
                    {
                        var i = frame * channels + channel;
                        block[i] = drySamples[i] + (block[i] - drySamples[i]) * wetMix;
                    }
                }
            }
            samples = samples[count..];
        }
    }

    public void Reset()
    {
        lock (publicationGate)
        {
            if (channels == 0) throw new InvalidOperationException("Initialize the live chain first.");
            current = published;
            previous = null;
            transitionPosition = 0;
            current.Reset();
            wetPosition = IsBypassed ? 0 : transitionFrames;
        }
    }
}
