namespace SharpAmp.Core.Dsp.Processors;

/// <summary>Full-wave rectified soft clipping with a 20 Hz DC blocker. Best on single notes.</summary>
public sealed class OctaveFuzzProcessor : IDigitalSignalProcessor
{
    private float[] previousInput = [];
    private float[] previousOutput = [];
    private float pole;
    private int channels;
    public float Drive { get; }
    public float OutputGain { get; }
    public float Wet { get; }

    public OctaveFuzzProcessor(float drive = 5, float outputGain = 0.35f, float wet = 1)
    {
        if (!float.IsFinite(drive) || drive <= 0) throw new ArgumentOutOfRangeException(nameof(drive));
        if (!float.IsFinite(outputGain) || outputGain < 0 || outputGain > 1) throw new ArgumentOutOfRangeException(nameof(outputGain));
        if (!float.IsFinite(wet) || wet < 0 || wet > 1) throw new ArgumentOutOfRangeException(nameof(wet));
        Drive = drive;
        OutputGain = outputGain;
        Wet = wet;
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = 0;
        previousInput = new float[channels];
        previousOutput = new float[channels];
        pole = (float)Math.Exp(-2 * Math.PI * 20 / sampleRate);
        this.channels = channels;
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the processor first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        for (var frame = 0; frame < samples.Length; frame += channels)
            for (var channel = 0; channel < channels; channel++)
            {
                var input = samples[frame + channel];
                var rectified = MathF.Abs(MathF.Tanh(input * Drive));
                var blocked = rectified - previousInput[channel] + pole * previousOutput[channel];
                previousInput[channel] = rectified;
                previousOutput[channel] = blocked;
                samples[frame + channel] = (input * (1 - Wet) + blocked * Wet) * OutputGain;
            }
    }

    public void Reset()
    {
        Array.Clear(previousInput);
        Array.Clear(previousOutput);
    }
}
