namespace SharpAmp.Core.Dsp.Processors;

/// <summary>Amplifies samples, hard clips at ±1, then applies output gain.</summary>
public sealed class HardOverdriveProcessor : IDigitalSignalProcessor
{
    private int channels;
    public float Drive { get; }
    public float OutputGain { get; }

    public HardOverdriveProcessor(float drive = 5, float outputGain = 0.35f)
    {
        if (!float.IsFinite(drive) || drive <= 0) throw new ArgumentOutOfRangeException(nameof(drive));
        if (!float.IsFinite(outputGain) || outputGain < 0 || outputGain > 1) throw new ArgumentOutOfRangeException(nameof(outputGain));
        Drive = drive;
        OutputGain = outputGain;
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
        for (var i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * Drive, -1, 1) * OutputGain;
    }

    public void Reset() { }
}
