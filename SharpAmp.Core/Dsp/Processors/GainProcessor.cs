namespace SharpAmp.Core.Dsp.Processors;

/// <summary>Linear amplitude gain, without clipping or preamp coloration.</summary>
public sealed class GainProcessor : IDigitalSignalProcessor
{
    private int channels;
    public float Gain { get; }

    public GainProcessor(float gain = 1)
    {
        if (!float.IsFinite(gain) || gain < 0) throw new ArgumentOutOfRangeException(nameof(gain));
        Gain = gain;
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = channels;
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the processor first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        for (var i = 0; i < samples.Length; i++) samples[i] *= Gain;
    }

    public void Reset() { }
}
