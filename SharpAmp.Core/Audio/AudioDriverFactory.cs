using System.Globalization;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SharpAmp.Core.Audio;

internal static class AudioDriverFactory
{
    public static IAudioDriver Create(AudioSessionOptions options)
    {
        // NAudio captures the current context in its constructors. Keep stop notifications off a
        // caller's UI context so synchronous cleanup never waits for that same UI thread.
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { return CreateDriver(options); }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }

    private static IAudioDriver CreateDriver(AudioSessionOptions options)
    {
        var owned = new List<IDisposable>();
        try
        {
            var sharedAsio = options.Input.Backend == AudioBackend.Asio && options.Output.Backend == AudioBackend.Asio
                && string.Equals(options.Input.Id, options.Output.Id, StringComparison.OrdinalIgnoreCase);
            var format = new WaveFormat(options.SampleRate, 16, options.InputChannels);
            IWaveIn input;
            switch (options.Input.Backend)
            {
                case AudioBackend.Mme:
                    input = new WaveInEvent
                    {
                        DeviceNumber = int.Parse(options.Input.Id, CultureInfo.InvariantCulture),
                        WaveFormat = format,
                        BufferMilliseconds = options.LatencyMilliseconds,
                        NumberOfBuffers = 3
                    };
                    break;
                case AudioBackend.Wasapi:
                    var captureEndpoint = GetEndpoint(options.Input.Id, DataFlow.Capture);
                    owned.Add(captureEndpoint);
                    // Use the endpoint mix format; conversion happens in the managed pipeline.
                    input = new WasapiCapture(captureEndpoint, true, options.LatencyMilliseconds);
                    break;
                case AudioBackend.Asio:
                    input = new AsioCapture(options.Input.Id, options.SampleRate, options.InputChannels, options.AsioInputChannelOffset);
                    break;
                case AudioBackend.DirectSound:
                    input = new DirectSoundCapture(Guid.Parse(options.Input.Id), format, options.LatencyMilliseconds);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(options));
            }
            owned.Add(input);
            if (input is AsioCapture asioInput && !sharedAsio) asioInput.Initialize(null, 0);

            IWavePlayer output;
            switch (options.Output.Backend)
            {
                case AudioBackend.Mme:
                    output = new WaveOutEvent
                    {
                        DeviceNumber = int.Parse(options.Output.Id, CultureInfo.InvariantCulture),
                        DesiredLatency = options.LatencyMilliseconds,
                        NumberOfBuffers = 3
                    };
                    break;
                case AudioBackend.Wasapi:
                    var renderEndpoint = GetEndpoint(options.Output.Id, DataFlow.Render);
                    owned.Add(renderEndpoint);
                    output = new WasapiOut(renderEndpoint, AudioClientShareMode.Shared, true, options.LatencyMilliseconds);
                    break;
                case AudioBackend.Asio:
                    output = sharedAsio ? ((AsioCapture)input).Player : new AsioOut(options.Output.Id);
                    if (!sharedAsio) owned.Add(output);
                    var asio = (AsioOut)output;
                    if (options.OutputChannels > asio.DriverOutputChannelCount ||
                        options.AsioOutputChannelOffset > asio.DriverOutputChannelCount - options.OutputChannels)
                        throw new ArgumentException("Requested ASIO output channels exceed the driver's channel count.");
                    asio.ChannelOffset = options.AsioOutputChannelOffset;
                    break;
                case AudioBackend.DirectSound:
                    output = new DirectSoundOut(Guid.Parse(options.Output.Id), options.LatencyMilliseconds);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(options));
            }
            if (options.Output.Backend != AudioBackend.Asio) owned.Add(output);
            return new AudioDriver(input, output, sharedAsio, options.AsioOutputChannelOffset, owned);
        }
        catch
        {
            // Preserve the original initialization error, but release every successfully opened resource.
            foreach (var resource in owned.AsEnumerable().Reverse())
                try { resource.Dispose(); } catch { }
            throw;
        }
    }

    private static MMDevice GetEndpoint(string id, DataFlow expectedFlow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var endpoint = enumerator.GetDevice(id);
        if (endpoint.DataFlow == expectedFlow) return endpoint;
        endpoint.Dispose();
        throw new ArgumentException("The WASAPI endpoint has the wrong data direction.", nameof(id));
    }

    internal sealed class AudioDriver : IAudioDriver
    {
        private readonly IWaveIn input;
        private readonly IWavePlayer output;
        private readonly bool sharedAsio;
        private readonly int outputOffset;
        private readonly List<IDisposable> owned;
        private readonly ManualResetEventSlim inputStopped = new(true);
        private readonly ManualResetEventSlim outputStopped = new(true);
        private bool disposed;

        public AudioDriver(IWaveIn input, IWavePlayer output, bool sharedAsio, int outputOffset, List<IDisposable> owned)
        {
            this.input = input;
            this.output = output;
            this.sharedAsio = sharedAsio;
            this.outputOffset = outputOffset;
            this.owned = owned;
            input.DataAvailable += ForwardData;
            input.RecordingStopped += OnInputStopped;
            if (!sharedAsio) output.PlaybackStopped += OnOutputStopped;
            if (output is AsioOut asio) asio.DriverResetRequest += OnReset;
        }

        public WaveFormat InputFormat => input.WaveFormat;
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? Stopped;
        private void ForwardData(object? sender, WaveInEventArgs e) => DataAvailable?.Invoke(this, e);
        private void OnInputStopped(object? sender, StoppedEventArgs e)
        {
            inputStopped.Set();
            Stopped?.Invoke(this, e);
        }
        private void OnOutputStopped(object? sender, StoppedEventArgs e)
        {
            outputStopped.Set();
            Stopped?.Invoke(this, e);
        }
        private void OnReset(object? sender, EventArgs e) => Stopped?.Invoke(this, new StoppedEventArgs(new InvalidOperationException("ASIO driver settings changed; recreate the session.")));

        public void InitializeOutput(IWaveProvider source)
        {
            if (sharedAsio) ((AsioCapture)input).Initialize(source, outputOffset);
            else output.Init(source);
        }

        public void Start()
        {
            inputStopped.Reset();
            try { input.StartRecording(); }
            catch { inputStopped.Set(); throw; }
            if (!sharedAsio)
            {
                outputStopped.Reset();
                try { output.Play(); }
                catch { outputStopped.Set(); throw; }
            }
        }

        public void Stop()
        {
            try { input.StopRecording(); }
            finally { if (!sharedAsio) output.Stop(); }
            // WaveInEvent.StopRecording requests a stop; disposing its buffers immediately races
            // its recording worker. Wait for both native streams before releasing resources.
            if (!inputStopped.Wait(TimeSpan.FromSeconds(5)) || !outputStopped.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("An audio driver did not acknowledge stop within five seconds.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            input.DataAvailable -= ForwardData;
            input.RecordingStopped -= OnInputStopped;
            if (!sharedAsio) output.PlaybackStopped -= OnOutputStopped;
            if (output is AsioOut asio) asio.DriverResetRequest -= OnReset;
            List<Exception> errors = [];
            foreach (var resource in owned.AsEnumerable().Reverse())
                try { resource.Dispose(); } catch (Exception error) { errors.Add(error); }
            inputStopped.Dispose();
            outputStopped.Dispose();
            if (errors.Count != 0) throw new AggregateException("Failed to release audio resources.", errors);
        }
    }
}
