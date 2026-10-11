namespace SharpAmp.Core.Dsp.Processors;

/// <summary>Four parallel damped feedback combs per channel. A small algorithmic room reverb.</summary>
public sealed class ReverbProcessor : IDigitalSignalProcessor
{
    private static readonly double[] DelaySeconds = [0.0297, 0.0371, 0.0411, 0.0437];
    private float[][] delays = [];
    private int[] positions = [];
    private float[] filtered = [];
    private int channels;
    public float Wet { get; }
    public float Feedback { get; }
    public float Damping { get; }

    public ReverbProcessor(float wet = 0.2f, float feedback = 0.7f, float damping = 0.3f)
    {
        if (!float.IsFinite(wet) || wet < 0 || wet > 1) throw new ArgumentOutOfRangeException(nameof(wet));
        if (!float.IsFinite(feedback) || feedback < 0 || feedback >= 1) throw new ArgumentOutOfRangeException(nameof(feedback));
        if (!float.IsFinite(damping) || damping < 0 || damping >= 1) throw new ArgumentOutOfRangeException(nameof(damping));
        Wet = wet;
        Feedback = feedback;
        Damping = damping;
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = channels;
        delays = new float[checked(channels * DelaySeconds.Length)][];
        positions = new int[delays.Length];
        filtered = new float[delays.Length];
        for (var channel = 0; channel < channels; channel++)
            for (var comb = 0; comb < DelaySeconds.Length; comb++)
                delays[channel * DelaySeconds.Length + comb] = new float[Math.Max(1, (int)Math.Round(sampleRate * DelaySeconds[comb]))];
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the processor first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        for (var frame = 0; frame < samples.Length; frame += channels)
            for (var channel = 0; channel < channels; channel++)
            {
                var input = samples[frame + channel];
                var sum = 0f;
                for (var comb = 0; comb < DelaySeconds.Length; comb++)
                {
                    var index = channel * DelaySeconds.Length + comb;
                    var delay = delays[index];
                    var position = positions[index];
                    var delayed = delay[position];
                    filtered[index] = delayed * (1 - Damping) + filtered[index] * Damping;
                    delay[position] = input + filtered[index] * Feedback;
                    positions[index] = (position + 1) % delay.Length;
                    sum += delayed;
                }
                // Normalization keeps sustained bounded input from building up unbounded wet gain.
                samples[frame + channel] = input * (1 - Wet) + sum * (Wet * (1 - Feedback) / DelaySeconds.Length);
            }
    }

    public void Reset()
    {
        foreach (var delay in delays) Array.Clear(delay);
        Array.Clear(positions);
        Array.Clear(filtered);
    }
}
