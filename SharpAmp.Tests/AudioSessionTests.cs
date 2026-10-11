using NAudio.Wave;
using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Processors;

namespace SharpAmp.Tests;

public sealed class AudioSessionTests
{
    private static AudioSessionOptions Options(AudioBackend output = AudioBackend.Wasapi) => new(
        new(AudioBackend.Mme, AudioDirection.Input, "0", "Fake microphone"),
        new(output, AudioDirection.Output, "0", "Fake speakers"));

    [Fact]
    public void PcmInputFlowsThroughDspToStereoFloatOutput()
    {
        var driver = new FakeDriver(new WaveFormat(48000, 16, 1));
        using var session = new AudioSession(Options(), new DistortionProcessor(2, 0.5f), _ => driver);
        session.Start();
        driver.PushPcm16([16384, -16384]);
        var samples = driver.ReadFloat(4);
        var expected = MathF.Tanh(1) * 0.5f;
        Assert.Equal(new[] { expected, expected, -expected, -expected }, samples);
        Assert.Equal(48000, session.ProcessingFormat.SampleRate);
        Assert.True(session.IsRunning);
    }

    [Fact]
    public void LegacyOutputUsesPcm16()
    {
        var driver = new FakeDriver(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        using var session = new AudioSession(Options(AudioBackend.DirectSound), new ProcessorChain(), _ => driver);
        session.Start();
        driver.PushFloat([0.5f]);
        var bytes = new byte[4];
        Assert.Equal(4, driver.Source!.Read(bytes, 0, bytes.Length));
        Assert.Equal(WaveFormatEncoding.Pcm, driver.Source.WaveFormat.Encoding);
        Assert.InRange(BitConverter.ToInt16(bytes, 0), (short)16383, (short)16384);
        Assert.Equal(BitConverter.ToInt16(bytes, 0), BitConverter.ToInt16(bytes, 2));
    }

    [Fact]
    public void UnderflowIsSilenceAndOverflowDropsWholePackets()
    {
        var driver = new FakeDriver(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        using var session = new AudioSession(Options(), new ProcessorChain(), _ => driver);
        session.Start();
        Assert.All(driver.ReadFloat(16), x => Assert.Equal(0, x));
        driver.PushFloat(new float[12001]); // Queue holds 12000 frames.
        Assert.Equal(12001, session.DroppedInputFrames);
        driver.PushFloat([0.5f]);
        Assert.Equal(new[] { 0.5f, 0.5f }, driver.ReadFloat(2));
    }

    [Fact]
    public void ResamplesCaptureToProcessingRate()
    {
        var driver = new FakeDriver(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        using var session = new AudioSession(Options() with { OutputChannels = 1 }, new ProcessorChain(), _ => driver);
        session.Start();
        // Ten milliseconds at 1 kHz should retain frequency after conversion to 48 kHz.
        driver.PushFloat(Enumerable.Range(0, 4410).Select(i => MathF.Sin(2 * MathF.PI * 1000 * i / 44100) * 0.5f).ToArray());
        var samples = driver.ReadFloat(4800);
        var crossings = Enumerable.Range(100, 4500).Count(i => samples[i] < 0 && samples[i + 1] >= 0);
        Assert.InRange(crossings, 92, 95);
        Assert.Equal(48000, driver.Source!.WaveFormat.SampleRate);
    }

    [Fact]
    public async Task StopAndDisposeAreIdempotentAndSessionCannotRestart()
    {
        var driver = new FakeDriver(new WaveFormat(48000, 16, 1));
        var session = new AudioSession(Options(), new ProcessorChain(), _ => driver);
        session.Start();
        Assert.Throws<InvalidOperationException>(session.Start);
        session.Stop();
        session.Stop();
        Assert.Null(await session.Completion);
        Assert.False(session.IsRunning);
        Assert.Throws<InvalidOperationException>(session.Start);
        session.Dispose();
        session.Dispose();
        Assert.Equal(1, driver.Stops);
        Assert.Equal(1, driver.Disposals);
        Assert.Throws<ObjectDisposedException>(session.Start);
    }

    [Fact]
    public async Task StartFailureStopsAndRemainsDisposable()
    {
        var error = new InvalidOperationException("Driver failed to start.");
        var driver = new FakeDriver(new WaveFormat(48000, 16, 1)) { StartError = error };
        using var session = new AudioSession(Options(), new ProcessorChain(), _ => driver);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(session.Start));
        Assert.Same(error, await session.Completion);
        Assert.Equal(1, driver.Stops);
    }

    [Fact]
    public void InitializationFailureReleasesDriver()
    {
        var driver = new FakeDriver(new WaveFormat(48000, 16, 1)) { InitializationError = new InvalidOperationException("Unsupported output format.") };
        Assert.Throws<InvalidOperationException>(() => new AudioSession(Options(), new ProcessorChain(), _ => driver));
        Assert.Equal(1, driver.Disposals);
    }

    [Fact]
    public async Task DeviceFailureCompletesSessionAndStopsDriver()
    {
        var driver = new FakeDriver(new WaveFormat(48000, 16, 1));
        using var session = new AudioSession(Options(), new ProcessorChain(), _ => driver);
        session.Start();
        var error = new IOException("Device disconnected.");
        driver.Fail(error);
        Assert.Same(error, await session.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(session.IsRunning);
        Assert.Equal(1, driver.Stops);
    }

    [Fact]
    public async Task DspExceptionProducesSilenceAndCompletesSession()
    {
        var driver = new FakeDriver(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        using var session = new AudioSession(Options(), new ThrowingProcessor(), _ => driver);
        session.Start();
        driver.PushFloat([1]);
        Assert.All(driver.ReadFloat(2), sample => Assert.Equal(0, sample));
        Assert.IsType<InvalidOperationException>(await session.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PartialCaptureFrameIsReportedAsFault()
    {
        var driver = new FakeDriver(new WaveFormat(48000, 16, 1));
        using var session = new AudioSession(Options(), new ProcessorChain(), _ => driver);
        session.Start();
        driver.PushBytes([1]);
        Assert.IsType<InvalidOperationException>(await session.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void InvalidOptionsAreRejectedBeforeOpeningHardware()
    {
        var opened = false;
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSession(Options() with { SampleRate = 0 }, new ProcessorChain(), _ => { opened = true; throw new Exception(); }));
        Assert.False(opened);
        Assert.Throws<ArgumentException>(() => (Options() with { AsioInputChannelOffset = 1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Options() with { BufferMilliseconds = 50 }).Validate());
        Assert.Throws<ArgumentException>(() => new AudioSessionOptions(Options().Output, Options().Input).Validate());
    }

    private sealed class ThrowingProcessor : IDigitalSignalProcessor
    {
        public void Initialize(int sampleRate, int channels) { }
        public void Process(Span<float> samples) => throw new InvalidOperationException("DSP failed.");
        public void Reset() { }
    }

    private sealed class FakeDriver(WaveFormat format) : IAudioDriver
    {
        public WaveFormat InputFormat => format;
        public IWaveProvider? Source { get; private set; }
        public Exception? InitializationError { get; init; }
        public Exception? StartError { get; init; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? Stopped;
        public void InitializeOutput(IWaveProvider source)
        {
            if (InitializationError is not null) throw InitializationError;
            Source = source;
        }
        public void Start() { if (StartError is not null) throw StartError; }
        public void Stop() { Stops++; Stopped?.Invoke(this, new StoppedEventArgs(null)); }
        public void Dispose() => Disposals++;
        public void Fail(Exception error) => Stopped?.Invoke(this, new StoppedEventArgs(error));
        public void PushBytes(byte[] bytes) => DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));
        public void PushFloat(float[] samples)
        {
            var bytes = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            PushBytes(bytes);
        }
        public void PushPcm16(short[] samples)
        {
            var bytes = new byte[samples.Length * sizeof(short)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            PushBytes(bytes);
        }
        public float[] ReadFloat(int count)
        {
            var bytes = new byte[count * sizeof(float)];
            Assert.Equal(bytes.Length, Source!.Read(bytes, 0, bytes.Length));
            var samples = new float[count];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            return samples;
        }
    }
}
