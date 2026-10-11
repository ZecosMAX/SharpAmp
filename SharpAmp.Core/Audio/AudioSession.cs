using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SharpAmp.Core.Dsp;

namespace SharpAmp.Core.Audio;

/// <summary>Owns one capture → conversion → DSP → playback session. Single use; create another to restart.</summary>
public sealed class AudioSession : IDisposable
{
    private readonly object gate = new();
    private readonly IAudioDriver driver;
    private readonly BufferedWaveProvider buffer;
    private readonly TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool running;
    private bool started;
    private bool stopped;
    private bool disposed;
    private int faultQueued;
    private Exception? firstFault;
    private long droppedFrames;

    public bool IsRunning => running;
    public WaveFormat InputFormat => driver.InputFormat;
    public WaveFormat ProcessingFormat { get; }
    public long DroppedInputFrames => Interlocked.Read(ref droppedFrames);
    /// <summary>Completes on stop, with null for a requested stop or the error that ended the stream.</summary>
    public Task<Exception?> Completion => completion.Task;

    public AudioSession(AudioSessionOptions options, IDigitalSignalProcessor processor)
        : this(options, processor, AudioDriverFactory.Create) { }

    internal AudioSession(AudioSessionOptions options, IDigitalSignalProcessor processor, Func<AudioSessionOptions, IAudioDriver> factory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(processor);
        options.Validate();
        driver = factory(options);
        try
        {
            buffer = new BufferedWaveProvider(driver.InputFormat)
            {
                BufferLength = checked((int)((long)driver.InputFormat.SampleRate * options.BufferMilliseconds / 1000) * driver.InputFormat.BlockAlign),
                ReadFully = true
            };
            ISampleProvider source = buffer.ToSampleProvider();
            if (source.WaveFormat.SampleRate != options.SampleRate)
                source = new WdlResamplingSampleProvider(source, options.SampleRate);
            if (source.WaveFormat.Channels != options.OutputChannels)
                source = new ChannelMappingSampleProvider(source, options.OutputChannels);
            ProcessingFormat = source.WaveFormat;
            processor.Initialize(ProcessingFormat.SampleRate, ProcessingFormat.Channels);
            var processed = new ProcessingSampleProvider(source, processor, QueueFault);
            IWaveProvider output = options.Output.Backend is AudioBackend.Mme or AudioBackend.DirectSound
                ? new SampleToWaveProvider16(processed) : new SampleToWaveProvider(processed);
            driver.DataAvailable += OnData;
            driver.Stopped += OnStopped;
            driver.InitializeOutput(output);
        }
        catch
        {
            driver.DataAvailable -= OnData;
            driver.Stopped -= OnStopped;
            try { driver.Dispose(); } catch { /* Preserve the initialization error. */ }
            throw;
        }
    }

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started || stopped) throw new InvalidOperationException("Sessions are single use; create a new session to restart.");
            started = true;
            running = true;
            try { driver.Start(); }
            catch (Exception error)
            {
                Finish(error);
                throw;
            }
        }
    }

    public void Stop() { lock (gate) Finish(null); }

    private void Finish(Exception? error)
    {
        if (stopped) return;
        stopped = true;
        running = false;
        try { driver.Stop(); }
        catch (Exception stopError) { error ??= stopError; }
        completion.TrySetResult(error ?? Volatile.Read(ref firstFault));
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (!running) return;
        try
        {
            if (e.BytesRecorded % InputFormat.BlockAlign != 0) throw new InvalidOperationException("Capture delivered a partial audio frame.");
            // Drop whole packets on overflow, preserving frame alignment and a bounded queue.
            if (e.BytesRecorded > buffer.BufferLength - buffer.BufferedBytes)
            {
                Interlocked.Add(ref droppedFrames, e.BytesRecorded / InputFormat.BlockAlign);
                return;
            }
            buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }
        catch (Exception error) { QueueFault(error); }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (running) QueueFault(e.Exception ?? new InvalidOperationException("An audio device stopped unexpectedly."));
    }

    private void QueueFault(Exception error)
    {
        Interlocked.CompareExchange(ref firstFault, error, null);
        // Never stop/dispose native drivers inside their buffer callback.
        if (Interlocked.Exchange(ref faultQueued, 1) == 0)
            ThreadPool.QueueUserWorkItem(_ => { lock (gate) Finish(error); });
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            Finish(null);
            disposed = true;
            driver.DataAvailable -= OnData;
            driver.Stopped -= OnStopped;
            driver.Dispose();
        }
    }
}
