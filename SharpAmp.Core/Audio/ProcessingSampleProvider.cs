using NAudio.Wave;
using SharpAmp.Core.Dsp;

namespace SharpAmp.Core.Audio;

internal sealed class ProcessingSampleProvider(ISampleProvider source, IDigitalSignalProcessor processor, Action<Exception> onFault) : ISampleProvider
{
    private bool faulted;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        if (faulted)
        {
            buffer.AsSpan(offset, count).Clear();
            return count;
        }
        try
        {
            var read = source.Read(buffer, offset, count);
            var samples = buffer.AsSpan(offset, read);
            processor.Process(samples);
            // Keep malformed DSP output (NaN/Infinity) out of the driver and limit hard peaks.
            for (var i = 0; i < samples.Length; i++)
                samples[i] = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1, 1) : 0;
            return read;
        }
        catch (Exception error)
        {
            faulted = true;
            buffer.AsSpan(offset, count).Clear();
            onFault(error);
            return count;
        }
    }
}
