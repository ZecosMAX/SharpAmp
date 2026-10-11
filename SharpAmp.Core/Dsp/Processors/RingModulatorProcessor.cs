namespace SharpAmp.Core.Dsp.Processors;

/// <summary>Multiplies the signal by a bipolar sine carrier for metallic sidebands.</summary>
public sealed class RingModulatorProcessor : IDigitalSignalProcessor
{
    private int channels;
    private double phase;
    private double phaseStep;
    public float FrequencyHz { get; }
    public float Wet { get; }

    public RingModulatorProcessor(float frequencyHz = 440, float wet = 0.5f)
    {
        if (!float.IsFinite(frequencyHz) || frequencyHz < 0 || frequencyHz > 20000) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (!float.IsFinite(wet) || wet < 0 || wet > 1) throw new ArgumentOutOfRangeException(nameof(wet));
        FrequencyHz = frequencyHz;
        Wet = wet;
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = channels;
        phaseStep = FrequencyHz / (double)sampleRate;
        Reset();
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the processor first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        for (var frame = 0; frame < samples.Length; frame += channels)
        {
            var gain = (float)(1 - Wet + Wet * Math.Sin(2 * Math.PI * phase));
            for (var channel = 0; channel < channels; channel++) samples[frame + channel] *= gain;
            phase += phaseStep;
            phase -= Math.Floor(phase);
        }
    }

    public void Reset() => phase = 0;
}
