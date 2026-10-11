namespace SharpAmp.Core.Dsp.Processors;

/// <summary>Signed PCM quantization and sample-and-hold rate reduction per channel.</summary>
public sealed class BitcrusherProcessor : IDigitalSignalProcessor
{
    private float[] held = [];
    private int framesUntilCapture;
    private int channels;
    private readonly float scale;
    public int Bits { get; }
    public int Downsample { get; }
    public float Wet { get; }

    public BitcrusherProcessor(int bits = 8, int downsample = 4, float wet = 1)
    {
        if (bits < 2 || bits > 16) throw new ArgumentOutOfRangeException(nameof(bits));
        if (downsample < 1 || downsample > 64) throw new ArgumentOutOfRangeException(nameof(downsample));
        if (!float.IsFinite(wet) || wet < 0 || wet > 1) throw new ArgumentOutOfRangeException(nameof(wet));
        Bits = bits;
        Downsample = downsample;
        Wet = wet;
        scale = 1 << (bits - 1);
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = 0;
        held = new float[channels];
        framesUntilCapture = 0;
        this.channels = channels;
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the processor first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        for (var frame = 0; frame < samples.Length; frame += channels)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var input = samples[frame + channel];
                if (framesUntilCapture == 0)
                    held[channel] = Math.Clamp(MathF.Round(Math.Clamp(input, -1, 1) * scale), -scale, scale - 1) / scale;
                samples[frame + channel] = input * (1 - Wet) + held[channel] * Wet;
            }
            if (framesUntilCapture == 0) framesUntilCapture = Downsample;
            framesUntilCapture--;
        }
    }

    public void Reset()
    {
        Array.Clear(held);
        framesUntilCapture = 0;
    }
}
