using NAudio.Wave;

namespace SharpAmp.Core.Audio;

internal interface IAudioDriver : IDisposable
{
    WaveFormat InputFormat { get; }
    event EventHandler<WaveInEventArgs>? DataAvailable;
    event EventHandler<StoppedEventArgs>? Stopped;
    void InitializeOutput(IWaveProvider source);
    void Start();
    void Stop();
}
