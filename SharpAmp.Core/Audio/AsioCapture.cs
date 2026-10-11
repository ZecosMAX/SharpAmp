using NAudio.Wave;

namespace SharpAmp.Core.Audio;

internal sealed class AsioCapture : IWaveIn
{
    private readonly WaveFormat format;
    private float[] samples = [];
    private byte[] bytes = [];
    private bool failed;
    public AsioOut Player { get; }
    public WaveFormat WaveFormat { get => format; set => throw new NotSupportedException("The capture format is fixed at construction."); }
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public AsioCapture(string name, int sampleRate, int channels, int offset)
    {
        format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        Player = new AsioOut(name);
        try
        {
            if (channels > Player.DriverInputChannelCount || offset > Player.DriverInputChannelCount - channels)
                throw new ArgumentException("Requested ASIO input channels exceed the driver's channel count.");
            if (!Player.IsSampleRateSupported(sampleRate)) throw new ArgumentException("The ASIO driver does not support the requested sample rate.");
            Player.InputChannelOffset = offset;
            Player.AudioAvailable += OnAudio;
            Player.PlaybackStopped += OnStopped;
            Player.DriverResetRequest += OnReset;
        }
        catch { Player.Dispose(); throw; }
    }

    public void Initialize(IWaveProvider? output, int outputOffset)
    {
        Player.ChannelOffset = outputOffset;
        Player.InitRecordAndPlayback(output, format.Channels, format.SampleRate);
        samples = new float[Player.FramesPerBuffer * format.Channels];
        bytes = new byte[samples.Length * sizeof(float)];
    }

    private void OnAudio(object? sender, AsioAudioAvailableEventArgs e)
    {
        if (failed) return;
        try
        {
            var count = e.GetAsInterleavedSamples(samples);
            Buffer.BlockCopy(samples, 0, bytes, 0, count * sizeof(float));
            DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, count * sizeof(float)));
        }
        catch (Exception error)
        {
            failed = true;
            RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e) => RecordingStopped?.Invoke(this, e);
    private void OnReset(object? sender, EventArgs e) => RecordingStopped?.Invoke(this, new StoppedEventArgs(new InvalidOperationException("ASIO driver settings changed; recreate the session.")));
    public void StartRecording() => Player.Play();
    public void StopRecording() => Player.Stop();
    public void Dispose()
    {
        Player.AudioAvailable -= OnAudio;
        Player.PlaybackStopped -= OnStopped;
        Player.DriverResetRequest -= OnReset;
        Player.Dispose();
    }
}
