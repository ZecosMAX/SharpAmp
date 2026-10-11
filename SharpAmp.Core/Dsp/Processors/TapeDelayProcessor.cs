namespace SharpAmp.Core.Dsp.Processors;

/// <summary>A tape-inspired echo with soft saturation, darkening repeats, and sinusoidal wow.</summary>
public sealed class TapeDelayProcessor : IDigitalSignalProcessor
{
    private float[] buffer = [];
    private float[] filtered = [];
    private int channels;
    private int bufferFrames;
    private int writeFrame;
    private double delayFrames;
    private double wowFrames;
    private double phase;
    private double phaseStep;
    private float filterAmount;
    public float TimeMs { get; }
    public float Feedback { get; }
    public float Wet { get; }
    public float ToneHz { get; }
    public float WowDepthMs { get; }
    public float WowRateHz { get; }

    public TapeDelayProcessor(float timeMs = 350, float feedback = 0.4f, float wet = 0.25f,
        float toneHz = 3000, float wowDepthMs = 2, float wowRateHz = 0.5f)
    {
        if (!float.IsFinite(timeMs) || timeMs < 1 || timeMs > 2000) throw new ArgumentOutOfRangeException(nameof(timeMs));
        if (!float.IsFinite(feedback) || feedback < 0 || feedback >= 1) throw new ArgumentOutOfRangeException(nameof(feedback));
        if (!float.IsFinite(wet) || wet < 0 || wet > 1) throw new ArgumentOutOfRangeException(nameof(wet));
        if (!float.IsFinite(toneHz) || toneHz < 20 || toneHz > 20000) throw new ArgumentOutOfRangeException(nameof(toneHz));
        if (!float.IsFinite(wowDepthMs) || wowDepthMs < 0 || wowDepthMs > 10) throw new ArgumentOutOfRangeException(nameof(wowDepthMs));
        if (!float.IsFinite(wowRateHz) || wowRateHz < 0 || wowRateHz > 10) throw new ArgumentOutOfRangeException(nameof(wowRateHz));
        TimeMs = timeMs;
        Feedback = feedback;
        Wet = wet;
        ToneHz = toneHz;
        WowDepthMs = wowDepthMs;
        WowRateHz = wowRateHz;
    }

    public void Initialize(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        this.channels = 0;
        delayFrames = sampleRate * (double)TimeMs / 1000;
        wowFrames = sampleRate * (double)WowDepthMs / 1000;
        bufferFrames = checked((int)Math.Ceiling(Math.Max(1, delayFrames + wowFrames)) + 2);
        buffer = new float[checked(bufferFrames * channels)];
        filtered = new float[channels];
        filterAmount = (float)(1 - Math.Exp(-2 * Math.PI * Math.Min(ToneHz, sampleRate * 0.45) / sampleRate));
        phaseStep = WowRateHz / (double)sampleRate;
        Reset();
        this.channels = channels;
    }

    public void Process(Span<float> samples)
    {
        if (channels == 0) throw new InvalidOperationException("Initialize the processor first.");
        if (samples.Length % channels != 0) throw new ArgumentException("Samples must contain whole frames.", nameof(samples));
        for (var frame = 0; frame < samples.Length; frame += channels)
        {
            // Never read the frame being written, even with very short delays and deep wow.
            var delay = Math.Max(1, delayFrames + wowFrames * Math.Sin(2 * Math.PI * phase));
            var read = writeFrame - delay;
            if (read < 0) read += bufferFrames;
            var first = (int)read;
            var second = (first + 1) % bufferFrames;
            var fraction = (float)(read - first);
            for (var channel = 0; channel < channels; channel++)
            {
                var input = samples[frame + channel];
                var a = buffer[first * channels + channel];
                var b = buffer[second * channels + channel];
                var delayed = a + (b - a) * fraction;
                filtered[channel] += filterAmount * (delayed - filtered[channel]);
                // Saturation lives in the loop; both recorded notes and repeats acquire coloration.
                buffer[writeFrame * channels + channel] = MathF.Tanh(input + filtered[channel] * Feedback);
                samples[frame + channel] = input * (1 - Wet) + filtered[channel] * (Wet * (1 - Feedback));
            }
            if (++writeFrame == bufferFrames) writeFrame = 0;
            phase += phaseStep;
            phase -= Math.Floor(phase);
        }
    }

    public void Reset()
    {
        Array.Clear(buffer);
        Array.Clear(filtered);
        writeFrame = 0;
        phase = 0;
    }
}
