namespace SharpAmp.Core.Dsp.Processors;

/// <summary>Independent channel echoes, with feedback and a linear dry/wet mix.</summary>
public sealed class DelayProcessor : IDigitalSignalProcessor
{
    private float[] buffer = [];
    private int position;
    private int channels;
    public float TimeMs { get; }
    public float Feedback { get; }
    public float Wet { get; }

    public DelayProcessor(float timeMs = 300, float feedback = 0.35f, float wet = 0.25f)
    {
        if (!float.IsFinite(timeMs) || timeMs < 1 || timeMs > 2000) throw new ArgumentOutOfRangeException(nameof(timeMs));
        if (!float.IsFinite(feedback) || feedback < 0 || feedback >= 1) throw new ArgumentOutOfRangeException(nameof(feedback));
        if (!float.IsFinite(wet) || wet < 0 || wet > 1) throw new ArgumentOutOfRangeException(nameof(wet));
        TimeMs = timeMs;
        Feedback = feedback;
        Wet = wet;
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = 0;
        var frames = Math.Max(1, checked((int)Math.Round(sampleRate * (double)TimeMs / 1000)));
        buffer = new float[checked(frames * channels)];
        position = 0;
        this.channels = channels;
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the processor first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        for (var i = 0; i < samples.Length; i++)
        {
            var input = samples[i];
            var delayed = buffer[position];
            buffer[position] = input + delayed * Feedback;
            // Normalize repeat gain so bounded sustained input does not amplify the wet path.
            samples[i] = input * (1 - Wet) + delayed * (Wet * (1 - Feedback));
            if (++position == buffer.Length) position = 0;
        }
    }

    public void Reset()
    {
        Array.Clear(buffer);
        position = 0;
    }
}
