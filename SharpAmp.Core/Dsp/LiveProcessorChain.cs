namespace SharpAmp.Core.Dsp;

/// <summary>
/// Publishes fresh chains from a control thread and crossfades them on one audio thread.
/// Initialize before starting the stream. Replace takes exclusive ownership of a fresh,
/// independent chain; never reuse its processors in another chain or mutate them afterward.
/// Initialize, Reset, and Dispose require a stopped stream. Processing neither locks nor
/// allocates. Replaced chains are disposed off the audio thread after their last use.
/// </summary>
public sealed class LiveProcessorChain : IDigitalSignalProcessor, IDisposable
{
    private const int ScratchFrames = 1024;
    private const int Disposing = int.MinValue;
    private readonly object publicationGate = new();
    private readonly List<OwnedChain> owned = [];
    private OwnedChain published;
    private OwnedChain? current;
    private OwnedChain? previous;
    private Timer? cleanupTimer;
    private List<Exception>? cleanupErrors;
    private bool isDisposed;
    // The high bit closes admission; the remaining bits count callbacks already admitted.
    private int processingState;
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
        published = new OwnedChain(initialChain);
        owned.Add(published);
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
            ObjectDisposedException.ThrowIf(isDisposed, this);
            this.channels = 0;
            var oldBuffer = new float[checked(ScratchFrames * channels)];
            var dryBuffer = new float[checked(ScratchFrames * channels)];
            published.Chain.Initialize(sampleRate, channels);
            oldSamples = oldBuffer;
            drySamples = dryBuffer;
            this.sampleRate = sampleRate;
            transitionFrames = Math.Max(1, sampleRate / 100); // Ten milliseconds.
            transitionPosition = 0;
            current = published;
            previous = null;
            published.ActivateStopped();
            RetireOtherChains();
            wetPosition = IsBypassed ? 0 : transitionFrames;
            this.channels = channels;
        }
    }

    /// <summary>
    /// Initialize off the audio thread, then publish atomically. A failed initialization
    /// disposes the replacement and leaves the playing chain intact. Rapid edits coalesce
    /// while a crossfade finishes. Ownership transfers after argument and state validation.
    /// </summary>
    public void Replace(ProcessorChain replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        lock (publicationGate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (channels == 0) throw new InvalidOperationException("Initialize the live chain first.");
            if (owned.Any(entry => ReferenceEquals(replacement, entry.Chain)))
                throw new ArgumentException("Publish a fresh, independent chain.", nameof(replacement));
            replacement.Initialize(sampleRate, channels);
            var next = new OwnedChain(replacement);
            owned.Add(next);
            var superseded = Interlocked.Exchange(ref published, next);
            // A pending chain can race audio-thread adoption. Only the winner owns its next use.
            superseded.RetireIfPending();
            ScheduleCleanup();
        }
    }

    public void Process(Span<float> samples)
    {
        var state = Volatile.Read(ref processingState);
        while (true)
        {
            ObjectDisposedException.ThrowIf(state < 0, this);
            var observed = Interlocked.CompareExchange(ref processingState, state + 1, state);
            if (observed == state) break;
            state = observed;
        }
        try { ProcessCore(samples); }
        finally { Interlocked.Decrement(ref processingState); }
    }

    private void ProcessCore(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the live chain first.");
        if (samples.Length % channels != 0)
            throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        if (samples.IsEmpty) return;

        // Read once per callback. A concurrent publication cannot change this buffer's chain.
        var next = Volatile.Read(ref published);
        if (previous is null && !ReferenceEquals(next, current) && next.TryActivate())
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

            current!.Chain.Process(block);
            if (previous is not null)
            {
                var old = oldSamples.AsSpan(0, count);
                previous.Chain.Process(old);
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
                if (transitionPosition == transitionFrames)
                {
                    previous.Retire();
                    previous = null;
                }
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
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (channels == 0) throw new InvalidOperationException("Initialize the live chain first.");
            current = published;
            previous = null;
            transitionPosition = 0;
            published.ActivateStopped();
            RetireOtherChains();
            current.Chain.Reset();
            wetPosition = IsBypassed ? 0 : transitionFrames;
        }
    }

    /// <summary>
    /// Stop the audio stream before calling this method. Disposal rejects new callbacks and
    /// waits up to five seconds for an in-flight callback. If it times out, cleanup is deferred
    /// until that callback exits; an active native effect is never unloaded.
    /// </summary>
    public void Dispose()
    {
        List<Exception>? errors;
        lock (publicationGate)
        {
            isDisposed = true;
            Interlocked.Or(ref processingState, Disposing);
            if (!SpinWait.SpinUntil(() => Volatile.Read(ref processingState) == Disposing, TimeSpan.FromSeconds(5)))
            {
                ScheduleCleanup();
                throw new TimeoutException("An audio callback did not finish within five seconds. Effect cleanup will complete after it exits.");
            }
            FinishDisposal();
            errors = cleanupErrors;
            cleanupErrors = null;
        }
        if (errors is not null) throw new AggregateException("Some effect chains could not be disposed.", errors);
    }

    private void FinishDisposal()
    {
        cleanupTimer?.Dispose();
        cleanupTimer = null;
        channels = 0;
        foreach (var entry in owned) DisposeChain(entry);
        owned.Clear();
        current = previous = null;
        oldSamples = drySamples = [];
    }

    private void ScheduleCleanup()
    {
        cleanupTimer ??= new Timer(static owner => ((LiveProcessorChain)owner!).CollectRetired(),
            this, Timeout.Infinite, Timeout.Infinite);
        cleanupTimer.Change(10, 10);
    }

    private void RetireOtherChains()
    {
        foreach (var entry in owned)
            if (!ReferenceEquals(entry, published)) entry.Retire();
        CollectRetired();
    }

    private void CollectRetired()
    {
        lock (publicationGate)
        {
            if (isDisposed)
            {
                if (Volatile.Read(ref processingState) == Disposing) FinishDisposal();
                return;
            }
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                var entry = owned[i];
                if (!entry.IsRetired) continue;
                owned.RemoveAt(i);
                DisposeChain(entry);
            }
            if (owned.Count == 1) cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void DisposeChain(OwnedChain entry)
    {
        try { entry.Chain.Dispose(); }
        catch (Exception error) { (cleanupErrors ??= []).Add(error); }
    }

    private sealed class OwnedChain(ProcessorChain chain)
    {
        private const int Pending = 0, Active = 1, Retired = 2;
        private int state;

        public ProcessorChain Chain { get; } = chain;
        public bool IsRetired => Volatile.Read(ref state) == Retired;
        public bool TryActivate() => Interlocked.CompareExchange(ref state, Active, Pending) == Pending;
        public void ActivateStopped() => Volatile.Write(ref state, Active);
        public void RetireIfPending() => Interlocked.CompareExchange(ref state, Retired, Pending);
        public void Retire() => Volatile.Write(ref state, Retired);
    }
}
