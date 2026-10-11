namespace SharpAmp.Core.Dsp.Processors;

/// <summary>Sine-shaped amplitude modulation, synchronized across channels.</summary>
public sealed class TremoloProcessor : IDigitalSignalProcessor
{
    private int channels;
    private double phase;
    private double phaseStep;
    public float RateHz { get; }
    public float Depth { get; }

    public TremoloProcessor(float rateHz = 4, float depth = 0.5f)
    {
        if (!float.IsFinite(rateHz) || rateHz < 0 || rateHz > 20) throw new ArgumentOutOfRangeException(nameof(rateHz));
        if (!float.IsFinite(depth) || depth < 0 || depth > 1) throw new ArgumentOutOfRangeException(nameof(depth));
        RateHz = rateHz;
        Depth = depth;
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = channels;
        phaseStep = RateHz / (double)sampleRate;
        Reset();
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the processor first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        for (var frame = 0; frame < samples.Length; frame += channels)
        {
            var gain = (float)(1 - Depth * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase)));
            for (var channel = 0; channel < channels; channel++) samples[frame + channel] *= gain;
            phase += phaseStep;
            phase -= Math.Floor(phase);
        }
    }

    public void Reset() => phase = 0;
}
