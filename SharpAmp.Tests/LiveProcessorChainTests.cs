using SharpAmp.Core.Dsp;

namespace SharpAmp.Tests;

public sealed class LiveProcessorChainTests
{
    [Fact]
    public void ReplacementsUseTheStreamFormatAndCrossfadeWholeFrames()
    {
        var initial = new GainProcessor(1);
        var next = new GainProcessor(3);
        using var live = new LiveProcessorChain(new ProcessorChain(initial));
        live.Initialize(1000, 2);
        live.Replace(new ProcessorChain(next));
        Assert.Equal((1000, 2), next.Format);

        var first = Enumerable.Repeat(1f, 8).ToArray();
        live.Process(first);
        for (var frame = 0; frame < 4; frame++)
        {
            Assert.Equal(1 + (frame + 1) * .2f, first[frame * 2], 5);
            Assert.Equal(first[frame * 2], first[frame * 2 + 1]);
        }
        var second = Enumerable.Repeat(1f, 16).ToArray();
        live.Process(second);
        Assert.Equal(2f, second[0], 5);
        Assert.Equal(3f, second[10]);
        Assert.Equal(3f, second[^1]);
        Assert.Equal(10, initial.ProcessedFrames);
        Assert.Equal(12, next.ProcessedFrames);
    }

    [Fact]
    public async Task PublishingDuringACallbackOnlyAffectsTheFollowingBuffer()
    {
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var initial = new BlockingProcessor(started, resume);
        using var live = new LiveProcessorChain(new ProcessorChain(initial));
        live.Initialize(1000, 1);
        // Larger than the bounded scratch buffer, so publication must also stay pinned
        // across the wrapper's internal chunks within this single callback.
        var first = Enumerable.Repeat(1f, 2048).ToArray();
        var callback = Task.Run(() => live.Process(first));
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            live.Replace(new ProcessorChain(new GainProcessor(2)));
        }
        finally
        {
            resume.Set();
        }
        await callback.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(first, sample => Assert.Equal(1, sample));
        var second = Enumerable.Repeat(1f, 20).ToArray();
        live.Process(second);
        Assert.Equal(1.1f, second[0], 5);
        Assert.Equal(2, second[^1]);
    }

    [Fact]
    public void RapidEditsFinishTheCurrentFadeThenUseTheNewestChain()
    {
        var middle = new GainProcessor(3);
        var skipped = new GainProcessor(4);
        var latest = new GainProcessor(5);
        using var live = new LiveProcessorChain(new ProcessorChain(new GainProcessor(1)));
        live.Initialize(1000, 1);
        live.Replace(new ProcessorChain(middle));
        live.Process(Enumerable.Repeat(1f, 5).ToArray());
        live.Replace(new ProcessorChain(skipped));
        live.Replace(new ProcessorChain(latest));
        var finish = Enumerable.Repeat(1f, 5).ToArray();
        live.Process(finish);
        Assert.Equal(3f, finish[^1]);
        var next = Enumerable.Repeat(1f, 10).ToArray();
        live.Process(next);
        Assert.Equal(3.2f, next[0], 5);
        Assert.Equal(5f, next[^1]);
        Assert.Equal(0, skipped.ProcessedFrames);
        Assert.Equal(10, latest.ProcessedFrames);
    }

    [Fact]
    public void BypassRampsToDryAndKeepsTheEffectsRunning()
    {
        var gain = new GainProcessor(3);
        using var live = new LiveProcessorChain(new ProcessorChain(gain));
        live.Initialize(1000, 1);
        live.IsBypassed = true;
        var bypass = Enumerable.Repeat(1f, 12).ToArray();
        live.Process(bypass);
        Assert.Equal(2.8f, bypass[0], 5);
        Assert.Equal(1f, bypass[9]);
        Assert.Equal(1f, bypass[^1]);
        Assert.Equal(12, gain.ProcessedFrames);
        live.IsBypassed = false;
        var enabled = Enumerable.Repeat(1f, 10).ToArray();
        live.Process(enabled);
        Assert.Equal(1.2f, enabled[0], 5);
        Assert.Equal(3f, enabled[^1]);
    }

    [Fact]
    public void FailedReplacementKeepsThePreviousChainPlaying()
    {
        using var live = new LiveProcessorChain(new ProcessorChain(new GainProcessor(2)));
        Assert.Throws<InvalidOperationException>(() => live.Replace(new ProcessorChain()));
        live.Initialize(1000, 1);
        Assert.Throws<ArgumentException>(() => live.Replace(new ProcessorChain(new FailingProcessor())));
        float[] samples = [1, -1];
        live.Process(samples);
        Assert.Equal(new float[] { 2, -2 }, samples);
    }

    [Fact]
    public void InvalidFormatsAndFramesAreRejectedAndResetClearsTransition()
    {
        var first = new GainProcessor(2);
        using var live = new LiveProcessorChain(new ProcessorChain(first));
        Assert.Throws<InvalidOperationException>(() => live.Process(new float[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => live.Initialize(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => live.Initialize(48000, 0));
        live.Initialize(1000, 2);
        Assert.Throws<ArgumentException>(() => live.Process(new float[3]));
        var next = new GainProcessor(4);
        live.Replace(new ProcessorChain(next));
        live.Process(new float[2]);
        live.IsBypassed = true;
        live.Reset();
        Assert.Equal(1, next.Resets);
        float[] samples = [1, -1];
        live.Process(samples);
        Assert.Equal(new float[] { 1, -1 }, samples);
    }

    [Fact]
    public void ProcessingLargeBuffersAndTransitionsDoesNotAllocate()
    {
        using var live = new LiveProcessorChain(new ProcessorChain(new GainProcessor(2)));
        live.Initialize(48000, 2);
        var samples = new float[8192];
        live.Process(samples);
        live.Replace(new ProcessorChain(new GainProcessor(3)));
        live.IsBypassed = true;
        // Warm both paths before measuring steady and crossfade callbacks.
        live.Process(samples);
        live.Replace(new ProcessorChain(new GainProcessor(4)));
        live.IsBypassed = false;
        var before = GC.GetAllocatedBytesForCurrentThread();
        live.Process(samples);
        live.Process(samples);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReplacedEffectsAreDisposedOnlyAfterTheirFadeAndOffTheAudioThread()
    {
        var initial = new DisposableProcessor();
        var next = new DisposableProcessor();
        using var live = new LiveProcessorChain(new ProcessorChain(initial));
        live.Initialize(1000, 1);
        live.Replace(new ProcessorChain(next));
        live.Process(new float[5]);
        Assert.Equal(0, initial.Disposals);
        var audioThread = Environment.CurrentManagedThreadId;
        live.Process(new float[5]);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref initial.Disposals) == 1, TimeSpan.FromSeconds(5)));
        Assert.NotEqual(audioThread, initial.DisposalThread);
        Assert.Equal(0, next.Disposals);
        live.Dispose();
        live.Dispose();
        Assert.Equal(1, next.Disposals);
        Assert.Throws<ObjectDisposedException>(() => live.Process(new float[1]));
        Assert.Throws<ObjectDisposedException>(() => live.Initialize(1000, 1));
        Assert.Throws<ObjectDisposedException>(live.Reset);
    }

    [Fact]
    public void CoalescedAndFailedReplacementsAreReleased()
    {
        var initial = new DisposableProcessor();
        var skipped = new DisposableProcessor();
        var latest = new DisposableProcessor();
        var failure = new DisposableProcessor { InitializationError = new ArgumentException("Unsupported plugin.") };
        using var live = new LiveProcessorChain(new ProcessorChain(initial));
        live.Initialize(1000, 1);
        live.Replace(new ProcessorChain(skipped));
        live.Replace(new ProcessorChain(latest));
        Assert.Throws<ArgumentException>(() => live.Replace(new ProcessorChain(failure)));
        Assert.Equal(1, failure.Disposals);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref skipped.Disposals) == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, initial.Disposals);
        Assert.Equal(0, latest.Disposals);
    }

    [Fact]
    public async Task ReplacementCannotDisposeAnEffectInsideAnActiveCallback()
    {
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var initial = new DisposableProcessor { Started = started, Resume = resume };
        using var live = new LiveProcessorChain(new ProcessorChain(initial));
        live.Initialize(1000, 1);
        var callback = Task.Run(() => live.Process(new float[2048]));
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            for (var i = 0; i < 30; i++) live.Replace(new ProcessorChain(new DisposableProcessor()));
            await Task.Delay(50);
            Assert.Equal(0, initial.Disposals);
        }
        finally { resume.Set(); }
        await callback.WaitAsync(TimeSpan.FromSeconds(5));
        live.Process(new float[10]);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref initial.Disposals) == 1, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ResetReleasesOldEffectsAndRejectsRepublishingAnOwnedChain()
    {
        var initial = new DisposableProcessor();
        var initialChain = new ProcessorChain(initial);
        using var live = new LiveProcessorChain(initialChain);
        live.Initialize(1000, 1);
        var next = new DisposableProcessor();
        live.Replace(new ProcessorChain(next));
        Assert.Throws<ArgumentException>(() => live.Replace(initialChain));
        live.Process(new float[1]);
        live.Reset();
        Assert.Equal(1, initial.Disposals);
        Assert.Equal(0, next.Disposals);
    }

    [Fact]
    public async Task ConcurrentPublicationNeverProcessesAReleasedEffect()
    {
        using var stop = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var effects = new List<DisposableProcessor> { new() };
        using var live = new LiveProcessorChain(new ProcessorChain(effects[0]));
        live.Initialize(1000, 1);
        var callback = Task.Run(() =>
        {
            var samples = new float[1];
            started.Set();
            while (!stop.IsCancellationRequested) live.Process(samples);
        });
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            for (var i = 0; i < 500; i++)
            {
                var effect = new DisposableProcessor();
                effects.Add(effect);
                live.Replace(new ProcessorChain(effect));
                Thread.Yield();
            }
        }
        finally { stop.Cancel(); }
        await callback.WaitAsync(TimeSpan.FromSeconds(5));
        live.Dispose();
        Assert.All(effects, effect => Assert.Equal(1, effect.Disposals));
    }

    [Fact]
    public async Task DisposalWaitsForTheActiveCallbackAndRejectsNewCallbacks()
    {
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var initial = new DisposableProcessor { Started = started, Resume = resume };
        using var live = new LiveProcessorChain(new ProcessorChain(initial));
        live.Initialize(1000, 1);
        var callback = Task.Run(() => live.Process(new float[2048]));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        var disposal = Task.Run(live.Dispose);
        try
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                try { live.Process(Span<float>.Empty); return false; }
                catch (ObjectDisposedException) { return true; }
            }, TimeSpan.FromSeconds(5)));
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, initial.Disposals);
        }
        finally { resume.Set(); }
        await callback.WaitAsync(TimeSpan.FromSeconds(5));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, initial.Disposals);
    }

    [Fact]
    public async Task DisposalTimeoutDefersNativeCleanupUntilTheCallbackExits()
    {
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var initial = new DisposableProcessor { Started = started, Resume = resume };
        using var live = new LiveProcessorChain(new ProcessorChain(initial));
        live.Initialize(1000, 1);
        var callback = Task.Run(() => live.Process(new float[2048]));
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.Throws<TimeoutException>(live.Dispose);
            Assert.Equal(0, initial.Disposals);
            Assert.Throws<ObjectDisposedException>(() => live.Process(new float[1]));
        }
        finally { resume.Set(); }
        await callback.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref initial.Disposals) == 1, TimeSpan.FromSeconds(5)));
        live.Dispose();
        Assert.Equal(1, initial.Disposals);
    }

    private sealed class GainProcessor(float gain) : IDigitalSignalProcessor
    {
        public (int Rate, int Channels) Format { get; private set; }
        public int ProcessedFrames { get; private set; }
        public int Resets { get; private set; }
        public void Initialize(int sampleRate, int channels) => Format = (sampleRate, channels);
        public void Process(Span<float> samples)
        {
            ProcessedFrames += samples.Length / Format.Channels;
            for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
        }
        public void Reset() => Resets++;
    }

    private sealed class BlockingProcessor(ManualResetEventSlim started, ManualResetEventSlim resume) : IDigitalSignalProcessor
    {
        public void Initialize(int sampleRate, int channels) { }
        public void Process(Span<float> samples)
        {
            started.Set();
            if (!resume.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test callback was not released.");
        }
        public void Reset() { }
    }

    private sealed class FailingProcessor : IDigitalSignalProcessor
    {
        public void Initialize(int sampleRate, int channels) => throw new ArgumentException("Unsupported effect format.");
        public void Process(Span<float> samples) { }
        public void Reset() { }
    }

    private sealed class DisposableProcessor : IDigitalSignalProcessor, IDisposable
    {
        public int Disposals;
        public int DisposalThread;
        public Exception? InitializationError { get; init; }
        public ManualResetEventSlim? Started { get; init; }
        public ManualResetEventSlim? Resume { get; init; }

        public void Initialize(int sampleRate, int channels)
        {
            if (InitializationError is not null) throw InitializationError;
        }
        public void Process(Span<float> samples)
        {
            Assert.Equal(0, Volatile.Read(ref Disposals));
            Started?.Set();
            if (Resume is not null && !Resume.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Test callback was not released.");
            Assert.Equal(0, Volatile.Read(ref Disposals));
        }
        public void Reset() { }
        public void Dispose()
        {
            DisposalThread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref Disposals);
        }
    }
}
