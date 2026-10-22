using SharpAmp.Core.Dsp;

namespace SharpAmp.Tests;

public sealed class ProcessorChainLifetimeTests
{
    [Fact]
    public void DisposalReleasesEachOwnedProcessorOnceAndRejectsFurtherUse()
    {
        var processor = new DisposableProcessor();
        var chain = new ProcessorChain(processor, processor);
        chain.Initialize(48000, 2);
        chain.Dispose();
        chain.Dispose();
        Assert.Equal(1, processor.Disposals);
        Assert.Throws<ObjectDisposedException>(() => chain.Initialize(48000, 2));
        Assert.Throws<ObjectDisposedException>(() => chain.Process(new float[2]));
        Assert.Throws<ObjectDisposedException>(chain.Reset);
    }

    [Fact]
    public void FailedInitializationReleasesAllProcessorsIncludingTheFailureAndUninitializedOnes()
    {
        var before = new DisposableProcessor();
        var failure = new DisposableProcessor { InitializationError = new InvalidOperationException("Cannot open plugin.") };
        var after = new DisposableProcessor();
        var chain = new ProcessorChain(before, failure, after);
        Assert.Same(failure.InitializationError, Assert.Throws<InvalidOperationException>(() => chain.Initialize(48000, 2)));
        chain.Dispose();
        Assert.Equal(1, before.Disposals);
        Assert.Equal(1, failure.Disposals);
        Assert.Equal(1, after.Disposals);
        Assert.Throws<ObjectDisposedException>(() => chain.Process(new float[2]));
    }

    [Fact]
    public void DisposalStillReleasesRemainingProcessorsWhenOneFails()
    {
        var failure = new DisposableProcessor { DisposalError = new InvalidOperationException("Close failed.") };
        var after = new DisposableProcessor();
        var chain = new ProcessorChain(failure, after);
        var error = Assert.Throws<AggregateException>(chain.Dispose);
        Assert.Same(failure.DisposalError, Assert.Single(error.InnerExceptions));
        Assert.Equal(1, after.Disposals);
        chain.Dispose();
    }

    private sealed class DisposableProcessor : IDigitalSignalProcessor, IDisposable
    {
        public Exception? InitializationError { get; init; }
        public Exception? DisposalError { get; init; }
        public int Disposals { get; private set; }

        public void Initialize(int sampleRate, int channels)
        {
            if (InitializationError is not null) throw InitializationError;
        }
        public void Process(Span<float> samples) { }
        public void Reset() { }
        public void Dispose()
        {
            Disposals++;
            if (DisposalError is not null) throw DisposalError;
        }
    }
}
