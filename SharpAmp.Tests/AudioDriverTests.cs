using NAudio.Wave;
using SharpAmp.Core.Audio;

namespace SharpAmp.Tests;

public sealed class AudioDriverTests
{
    [Fact]
    public async Task StopWaitsForCaptureAndPlaybackAcknowledgementsBeforeDisposal()
    {
        var input = new FakeCapture();
        var output = new FakePlayer();
        using var driver = new AudioDriverFactory.AudioDriver(input, output, false, 0, [input, output]);
        driver.Start();
        var stopping = Task.Run(driver.Stop);
        await input.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await output.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopping.IsCompleted);
        input.AcknowledgeStop();
        Assert.False(stopping.IsCompleted);
        output.AcknowledgeStop();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        driver.Dispose();
        Assert.True(input.Disposed);
        Assert.True(output.Disposed);
    }

    [Fact]
    public void DisposeReleasesEveryResourceEvenWhenOneFails()
    {
        var input = new FakeCapture();
        var output = new FakePlayer { FailDispose = true };
        var driver = new AudioDriverFactory.AudioDriver(input, output, false, 0, [input, output]);
        Assert.Throws<AggregateException>(driver.Dispose);
        Assert.True(input.Disposed);
        Assert.True(output.Disposed);
        driver.Dispose();
    }

    private sealed class FakeCapture : IWaveIn
    {
        public TaskCompletionSource StopRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public WaveFormat WaveFormat { get; set; } = new(48000, 16, 1);
        public event EventHandler<WaveInEventArgs>? DataAvailable { add { } remove { } }
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public void StartRecording() { }
        public void StopRecording() => StopRequested.TrySetResult();
        public void AcknowledgeStop() => RecordingStopped?.Invoke(this, new StoppedEventArgs(null));
        public void Dispose() => Disposed = true;
    }

    private sealed class FakePlayer : IWavePlayer
    {
        public TaskCompletionSource StopRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public bool FailDispose { get; init; }
        public float Volume { get; set; } = 1;
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat { get; private set; } = new(48000, 16, 2);
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider waveProvider) => OutputWaveFormat = waveProvider.WaveFormat;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => StopRequested.TrySetResult();
        public void AcknowledgeStop()
        {
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs(null));
        }
        public void Dispose()
        {
            Disposed = true;
            if (FailDispose) throw new IOException("Device disposal failed.");
        }
    }
}
