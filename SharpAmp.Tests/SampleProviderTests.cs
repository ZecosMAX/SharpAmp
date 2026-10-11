using NAudio.Wave;
using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;

namespace SharpAmp.Tests;

public sealed class SampleProviderTests
{
    [Fact]
    public void StereoDownmixAveragesChannelsAndHonorsOffsetAndShortRead()
    {
        var provider = new ChannelMappingSampleProvider(new ArraySource([1, -1, 0.5f, 0.25f], 2), 1);
        float[] result = [9, 9, 9, 9, 9];
        Assert.Equal(2, provider.Read(result, 1, 3));
        Assert.Equal(new[] { 9f, 0, 0.375f, 9, 9 }, result);
    }

    [Fact]
    public void ExtraOutputChannelsAreSilent()
    {
        var provider = new ChannelMappingSampleProvider(new ArraySource([0.1f, 0.2f], 2), 4);
        var result = new float[4];
        Assert.Equal(4, provider.Read(result, 0, 4));
        Assert.Equal(new[] { 0.1f, 0.2f, 0, 0 }, result);
    }

    [Fact]
    public void ProcessingSanitizesNonFiniteSamplesAndClipsPeaks()
    {
        var chain = new ProcessorChain();
        chain.Initialize(48000, 1);
        var provider = new ProcessingSampleProvider(new ArraySource([float.NaN, float.PositiveInfinity, -2, 2], 1), chain, _ => Assert.Fail("Unexpected fault"));
        float[] result = [9, 9, 9, 9, 9, 9];
        Assert.Equal(4, provider.Read(result, 1, 4));
        Assert.Equal(new[] { 9f, 0, 0, -1, 1, 9 }, result);
    }

    private sealed class ArraySource(float[] samples, int channels) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, samples.Length - position);
            samples.AsSpan(position, read).CopyTo(buffer.AsSpan(offset));
            position += read;
            return read;
        }
    }
}
