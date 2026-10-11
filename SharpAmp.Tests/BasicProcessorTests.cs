using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class BasicProcessorTests
{
    [Fact]
    public void GainBoostsWithoutClippingAndCanMute()
    {
        var gain = new GainProcessor(4);
        gain.Initialize(48000, 2);
        float[] samples = [0.5f, -0.75f];
        gain.Process(samples);
        Assert.Equal(new float[] { 2, -3 }, samples);
        var mute = new GainProcessor(0);
        mute.Initialize(48000, 2);
        mute.Process(samples);
        Assert.Equal(new float[2], samples);
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void DelayRepeatsAtRequestedTimeWithDecayingFeedback(int sampleRate)
    {
        var delay = new DelayProcessor(100, 0.5f, 1);
        delay.Initialize(sampleRate, 2);
        var interval = sampleRate / 10;
        var samples = new float[interval * 4 * 2];
        samples[0] = 1;
        delay.Process(samples);
        for (var frame = 0; frame < samples.Length / 2; frame++)
        {
            var expected = frame == interval ? 0.5f : frame == 2 * interval ? 0.25f : frame == 3 * interval ? 0.125f : 0;
            Assert.Equal(expected, samples[frame * 2]);
            Assert.Equal(0, samples[frame * 2 + 1]);
        }
    }

    [Fact]
    public void TapeEchoDelaysSaturatesAndLowPassFiltersTheWetSignal()
    {
        var tape = new TapeDelayProcessor(2, 0, 1, 100, 0, 0);
        tape.Initialize(1000, 1);
        float[] samples = [1, 0, 0, 0, 0];
        tape.Process(samples);
        var coefficient = (float)(1 - Math.Exp(-2 * Math.PI * 100 / 1000));
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[1]);
        Assert.Equal(MathF.Tanh(1) * coefficient, samples[2], 6);
        Assert.Equal(samples[2] * (1 - coefficient), samples[3], 6);
        Assert.True(samples[4] > 0 && samples[4] < samples[3]);
    }

    [Fact]
    public void TapeWowChangesEchoTimingAndShortDelayRemainsFinite()
    {
        var plain = new TapeDelayProcessor(10, wowDepthMs: 0);
        var wobble = new TapeDelayProcessor(10, wowDepthMs: 2, wowRateHz: 3);
        var shortDelay = new TapeDelayProcessor(1, 0.99f, 1, 20000, 10, 10);
        var input = Enumerable.Range(0, 8000).Select(i => (float)Math.Sin(i * 0.1)).ToArray();
        var first = (float[])input.Clone();
        var second = (float[])input.Clone();
        plain.Initialize(8000, 1);
        wobble.Initialize(8000, 1);
        shortDelay.Initialize(8000, 1);
        plain.Process(first);
        wobble.Process(second);
        Assert.Contains(first.Zip(second), pair => Math.Abs(pair.First - pair.Second) > 0.01);
        shortDelay.Process(input);
        Assert.All(input, value => Assert.True(float.IsFinite(value) && Math.Abs(value) <= 1));
    }

    [Fact]
    public void TremoloHasExpectedDepthAndRateAndKeepsStereoInPhase()
    {
        var tremolo = new TremoloProcessor(1, 0.75f);
        tremolo.Initialize(4, 2);
        var samples = Enumerable.Repeat(1f, 10).ToArray();
        tremolo.Process(samples);
        float[] expected = [1, 0.625f, 0.25f, 0.625f, 1];
        for (var frame = 0; frame < expected.Length; frame++)
        {
            Assert.Equal(expected[frame], samples[frame * 2], 6);
            Assert.Equal(samples[frame * 2], samples[frame * 2 + 1]);
        }
    }

    [Fact]
    public void RingModulationUsesABipolarCarrier()
    {
        var ring = new RingModulatorProcessor(1, 1);
        ring.Initialize(4, 1);
        float[] samples = [1, 1, 1, 1, 1];
        ring.Process(samples);
        float[] expected = [0, 1, 0, -1, 0];
        for (var i = 0; i < samples.Length; i++) Assert.Equal(expected[i], samples[i], 6);
    }

    [Fact]
    public void BitcrusherQuantizesAndHoldsWholeFramesWithoutChannelLeakage()
    {
        var crusher = new BitcrusherProcessor(3, 2);
        crusher.Initialize(48000, 2);
        float[] samples = [0.3f, -0.6f, 0.9f, 0.9f, 2, -2, 0, 0];
        crusher.Process(samples);
        Assert.Equal(new float[] { 0.25f, -0.5f, 0.25f, -0.5f, 0.75f, -1, 0.75f, -1 }, samples);
    }

    [Fact]
    public void OctaveFuzzEmphasizesDoubleFrequencyAndRemovesDc()
    {
        const int sampleRate = 48000;
        const int frequency = 200;
        var fuzz = new OctaveFuzzProcessor(3, 1);
        fuzz.Initialize(sampleRate, 1);
        var samples = Enumerable.Range(0, sampleRate).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / sampleRate))).ToArray();
        fuzz.Process(samples);
        var settled = samples.Skip(sampleRate / 2).ToArray();
        Assert.True(Math.Abs(settled.Average()) < 0.001);
        Assert.True(Magnitude(settled, frequency * 2, sampleRate) > 0.05);
        Assert.True(Magnitude(settled, frequency, sampleRate) < 0.001);
    }

    [Fact]
    public void DistortionBlendsDrySignalBeforeOutputGainAndPreservesDefaultBehavior()
    {
        var distortion = new DistortionProcessor(4, 0.5f, 0.25f);
        distortion.Initialize(48000, 1);
        float[] samples = [0.2f, -0.2f];
        distortion.Process(samples);
        Assert.Equal((0.2f * 0.75f + MathF.Tanh(0.8f) * 0.25f) * 0.5f, samples[0], 6);
        Assert.Equal(-samples[0], samples[1]);
    }

    [Theory]
    [InlineData("gain")]
    [InlineData("delay")]
    [InlineData("tapeDelay")]
    [InlineData("tremolo")]
    [InlineData("bitcrusher")]
    [InlineData("octaveFuzz")]
    [InlineData("ringModulator")]
    public void ProcessorsHonorStreamingLifecycleAndDoNotLeakChannels(string type)
    {
        foreach (var sampleRate in new[] { 8000, 44100, 48000 })
        {
            var whole = Create(type);
            var chunked = Create(type);
            Assert.Throws<InvalidOperationException>(() => whole.Process(new float[2]));
            Assert.Throws<ArgumentOutOfRangeException>(() => whole.Initialize(0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => whole.Initialize(sampleRate, 0));
            whole.Initialize(sampleRate, 2);
            chunked.Initialize(sampleRate, 2);
            Assert.Throws<ArgumentException>(() => whole.Process(new float[3]));
            var input = new float[sampleRate * 2];
            for (var i = 0; i < input.Length; i += 2) input[i] = (float)(0.5 * Math.Sin(i * 0.03));
            var first = (float[])input.Clone();
            var second = (float[])input.Clone();
            whole.Process(first);
            // Odd frame counts expose sample-hold and oscillator state lost at block boundaries.
            for (var offset = 0; offset < second.Length; offset += 74)
            {
                chunked.Process(Span<float>.Empty);
                chunked.Process(second.AsSpan(offset, Math.Min(74, second.Length - offset)));
            }
            Assert.Equal(first, second);
            Assert.All(first.Where((_, i) => i % 2 == 1), value => Assert.Equal(0, value));
            Assert.All(first, value => Assert.True(float.IsFinite(value)));
            chunked.Reset();
            second = (float[])input.Clone();
            chunked.Process(second);
            Assert.Equal(first, second);
            chunked.Initialize(sampleRate, 2);
            second = (float[])input.Clone();
            chunked.Process(second);
            Assert.Equal(first, second);
            chunked.Initialize(sampleRate, 1);
            var silence = new float[sampleRate];
            chunked.Process(silence);
            Assert.All(silence, value => Assert.Equal(0, value));
        }
    }

    [Fact]
    public void DryAndZeroDepthSettingsPreserveInput()
    {
        IDigitalSignalProcessor[] processors = [new DelayProcessor(wet: 0), new TapeDelayProcessor(wet: 0),
            new TremoloProcessor(depth: 0), new BitcrusherProcessor(wet: 0), new RingModulatorProcessor(wet: 0),
            new OctaveFuzzProcessor(outputGain: 1, wet: 0), new DistortionProcessor(outputGain: 1, wet: 0)];
        float[] input = [-2, -0.23f, 0, 0.8f, 2];
        foreach (var processor in processors)
        {
            processor.Initialize(48000, 1);
            var samples = (float[])input.Clone();
            processor.Process(samples);
            Assert.Equal(input, samples);
        }
    }

    private static IDigitalSignalProcessor Create(string type) =>
        ProcessorChain.FromJson($$"""{"processors":[{"type":"{{type}}"}]}""").Processors[0];

    private static double Magnitude(float[] samples, int frequency, int sampleRate)
    {
        double real = 0, imaginary = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var phase = 2 * Math.PI * frequency * i / sampleRate;
            real += samples[i] * Math.Cos(phase);
            imaginary += samples[i] * Math.Sin(phase);
        }
        return Math.Sqrt(real * real + imaginary * imaginary) / samples.Length;
    }
}
