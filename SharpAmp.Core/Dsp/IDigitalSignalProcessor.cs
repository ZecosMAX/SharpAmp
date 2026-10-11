namespace SharpAmp.Core.Dsp;

/// <summary>A stateful, in-place processor of interleaved float samples. One instance per stream.</summary>
public interface IDigitalSignalProcessor
{
    /// <summary>Configure the format and clear history before processing a new stream.</summary>
    void Initialize(int sampleRate, int channels);

    /// <summary>Process whole frames synchronously. Do not retain the span, block, or allocate per sample.</summary>
    void Process(Span<float> samples);

    /// <summary>Clear history without changing the format.</summary>
    void Reset();
}
