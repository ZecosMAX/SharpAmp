using NAudio.Wave;

namespace SharpAmp.Core.Audio;

/// <summary>Mono is duplicated, downmix to mono is averaged; otherwise channels map by index and extras are silent.</summary>
internal sealed class ChannelMappingSampleProvider(ISampleProvider source, int channels) : ISampleProvider
{
    private float[] input = [];
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, channels);

    public int Read(float[] buffer, int offset, int count)
    {
        if (count % channels != 0) throw new ArgumentException("Read must request whole frames.", nameof(count));
        var inputChannels = source.WaveFormat.Channels;
        var needed = checked(count / channels * inputChannels);
        if (input.Length < needed) input = new float[needed];
        var read = source.Read(input, 0, needed);
        var frames = read / inputChannels;
        for (var frame = 0; frame < frames; frame++)
            for (var channel = 0; channel < channels; channel++)
            {
                var value = 0f;
                if (inputChannels == 1) value = input[frame];
                else if (channels == 1)
                {
                    for (var c = 0; c < inputChannels; c++) value += input[frame * inputChannels + c] / inputChannels;
                }
                else if (channel < inputChannels) value = input[frame * inputChannels + channel];
                buffer[offset + frame * channels + channel] = value;
            }
        return frames * channels;
    }
}
