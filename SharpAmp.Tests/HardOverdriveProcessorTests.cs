using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class HardOverdriveProcessorTests
{
    [Fact]
    public void AmplifiesLinearlyUntilThresholdThenHardClipsBothPolarities()
    {
        var processor = new HardOverdriveProcessor(drive: 4, outputGain: 0.5f);
        processor.Initialize(48000, 1);
        float[] samples = [-100, -0.5f, -0.25f, -0.125f, 0, 0.125f, 0.25f, 0.5f, 100];

        processor.Process(samples);

        Assert.Equal(new float[] { -0.5f, -0.5f, -0.5f, -0.25f, 0, 0.25f, 0.5f, 0.5f, 0.5f }, samples);
    }

    [Fact]
    public void ProcessesOnlyRequestedSpanAndKeepsChannelsIndependent()
    {
        var processor = new HardOverdriveProcessor(drive: 2, outputGain: 1);
        processor.Initialize(44100, 2);
        float[] samples = [9, 0.125f, -0.75f, 0, 0.25f, 9];

        processor.Process(samples.AsSpan(1, 4));

        Assert.Equal(new float[] { 9, 0.25f, -1, 0, 0.5f, 9 }, samples);
    }

    [Fact]
    public void ZeroOutputGainMutesEvenWhenDriveOverflowsFiniteInput()
    {
        var processor = new HardOverdriveProcessor(float.MaxValue, 0);
        processor.Initialize(48000, 1);
        float[] samples = [float.MaxValue, -float.MaxValue, 0];

        processor.Process(samples);

        Assert.All(samples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void CanBeComposedInAProcessorChainAndResetHasNoHistory()
    {
        var chain = new ProcessorChain(new HardOverdriveProcessor(4, 0.5f), new ReverbProcessor(wet: 0));
        chain.Initialize(48000, 2);
        float[] first = [0.125f, -0.5f];
        chain.Process(first);
        chain.Reset();
        float[] second = [0.125f, -0.5f];
        chain.Process(second);

        Assert.Equal(new float[] { 0.25f, -0.5f }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void RequiresInitializationAndWholeFrames()
    {
        var processor = new HardOverdriveProcessor();
        Assert.Throws<InvalidOperationException>(() => processor.Process(new float[2]));
        processor.Initialize(48000, 2);
        Assert.Throws<ArgumentException>(() => processor.Process(new float[3]));
        processor.Process(Span<float>.Empty);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(48000, 0)]
    [InlineData(48000, -1)]
    public void RejectsInvalidFormat(int sampleRate, int channels) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HardOverdriveProcessor().Initialize(sampleRate, channels));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void RejectsInvalidDrive(float drive) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HardOverdriveProcessor(drive));

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void RejectsInvalidOutputGain(float outputGain) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HardOverdriveProcessor(outputGain: outputGain));
}
