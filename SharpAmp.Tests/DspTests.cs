using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class DspTests
{
    [Fact]
    public void DistortionIsOddAndBounded()
    {
        var processor = new DistortionProcessor(5, 0.4f);
        processor.Initialize(48000, 1);
        float[] samples = [-100, -0.5f, 0, 0.5f, 100];
        processor.Process(samples);
        Assert.Equal(0, samples[2]);
        Assert.Equal(-samples[0], samples[4]);
        Assert.Equal(-samples[1], samples[3]);
        Assert.All(samples, sample => Assert.InRange(sample, -0.4f, 0.4f));
        Assert.Equal(MathF.Tanh(2.5f) * 0.4f, samples[3]);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void DistortionRejectsInvalidDrive(float drive) => Assert.Throws<ArgumentOutOfRangeException>(() => new DistortionProcessor(drive));

    [Fact]
    public void ChainRunsInOrderAndResetsAllProcessors()
    {
        var add = new ProbeProcessor(x => x + 1);
        var multiply = new ProbeProcessor(x => x * 2);
        IDigitalSignalProcessor[] processors = [add, multiply];
        var chain = new ProcessorChain(processors);
        processors[0] = multiply; // The chain owns a snapshot of membership.
        chain.Initialize(44100, 2);
        float[] samples = [1, 2];
        chain.Process(samples);
        Assert.Equal(new float[] { 4, 6 }, samples);
        Assert.Equal((44100, 2), add.Format);
        Assert.Equal((44100, 2), multiply.Format);
        chain.Reset();
        Assert.Equal(1, add.Resets);
        Assert.Equal(1, multiply.Resets);
    }

    [Fact]
    public void EmptyChainIsBypassAndRequiresWholeFrames()
    {
        var chain = new ProcessorChain();
        Assert.Throws<InvalidOperationException>(() => chain.Process(new float[2]));
        chain.Initialize(48000, 2);
        float[] samples = [0.1f, -0.2f];
        chain.Process(samples);
        Assert.Equal(new float[] { 0.1f, -0.2f }, samples);
        Assert.Throws<ArgumentException>(() => chain.Process(new float[3]));
        Assert.Throws<ArgumentException>(() => new ProcessorChain([null!]));
    }

    [Fact]
    public void ReverbHasADecayingTailAndDoesNotLeakBetweenChannels()
    {
        var reverb = new ReverbProcessor(wet: 1, feedback: 0.7f, damping: 0);
        reverb.Initialize(48000, 2);
        var samples = new float[48000 * 4 * 2];
        samples[0] = 1;
        reverb.Process(samples);
        Assert.Equal(0, samples[0]);
        Assert.Contains(samples.Skip(1000).Take(6000), x => x > 0);
        Assert.All(samples.Where((_, index) => index % 2 == 1), x => Assert.Equal(0, x));
        var earlyEnergy = samples.Take(48000 * 2).Sum(x => x * x);
        var lateEnergy = samples.Skip(48000 * 2 * 3).Sum(x => x * x);
        Assert.True(earlyEnergy > 0);
        Assert.True(lateEnergy < earlyEnergy * 0.001);
    }

    [Fact]
    public void ReverbIsIndependentOfBlockSizeAndResetClearsHistory()
    {
        var whole = new ReverbProcessor();
        var chunked = new ReverbProcessor();
        whole.Initialize(44100, 2);
        chunked.Initialize(44100, 2);
        var first = new float[16000];
        first[0] = 1;
        first[9] = -0.25f;
        var second = (float[])first.Clone();
        whole.Process(first);
        for (var offset = 0; offset < second.Length; offset += 128)
            chunked.Process(second.AsSpan(offset, Math.Min(128, second.Length - offset)));
        Assert.Equal(first, second);
        chunked.Reset();
        var silence = new float[16000];
        chunked.Process(silence);
        Assert.All(silence, x => Assert.Equal(0, x));
    }

    [Fact]
    public void ReverbDryMixPreservesSamplesAndReinitializationClearsTail()
    {
        var reverb = new ReverbProcessor(wet: 0);
        reverb.Initialize(48000, 1);
        float[] samples = [1, -1, 0.25f];
        reverb.Process(samples);
        Assert.Equal(new float[] { 1, -1, 0.25f }, samples);
        reverb.Initialize(44100, 2);
        var silence = new float[8000];
        reverb.Process(silence);
        Assert.All(silence, x => Assert.Equal(0, x));
    }

    [Theory]
    [InlineData(-0.1f, 0.7f, 0.3f)]
    [InlineData(1.1f, 0.7f, 0.3f)]
    [InlineData(0.2f, 1f, 0.3f)]
    [InlineData(0.2f, 0.7f, 1f)]
    [InlineData(float.NaN, 0.7f, 0.3f)]
    public void ReverbRejectsUnstableParameters(float wet, float feedback, float damping) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReverbProcessor(wet, feedback, damping));

    [Fact]
    public void DemoIsHardOverdriveThenReverb()
    {
        var chain = DemoProcessorChain.Create(2, 0.25f);
        chain.Initialize(48000, 1);
        float[] sample = [0.75f];
        chain.Process(sample);
        Assert.Equal(0.35f * 0.75f, sample[0], 6);
    }

    private sealed class ProbeProcessor(Func<float, float> transform) : IDigitalSignalProcessor
    {
        public (int, int) Format { get; private set; }
        public int Resets { get; private set; }
        public void Initialize(int sampleRate, int channels) => Format = (sampleRate, channels);
        public void Process(Span<float> samples)
        {
            for (var i = 0; i < samples.Length; i++) samples[i] = transform(samples[i]);
        }
        public void Reset() => Resets++;
    }
}
