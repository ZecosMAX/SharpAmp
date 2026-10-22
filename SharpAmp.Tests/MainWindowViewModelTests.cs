using NAudio.Wave;
using SharpAmp.Avalonia.Models;
using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;

namespace SharpAmp.Tests;

public sealed class MainWindowViewModelTests
{
    private static AudioSettings Settings() => new()
    {
        InputDevice = new(AudioBackend.Wasapi, AudioDirection.Input, "input", "Test input"),
        OutputDevice = new(AudioBackend.Wasapi, AudioDirection.Output, "output", "Test output")
    };

    private static MainWindowViewModel Create(AudioSettings settings,
        Func<AudioSessionOptions, IDigitalSignalProcessor, AudioSession> factory) =>
        new(settings, (_, direction) => direction == AudioDirection.Input
            ? settings.InputDevice is { } input ? [input] : []
            : settings.OutputDevice is { } output ? [output] : [], factory);

    [Fact]
    public void MissingDevicesNeverOpenTheEngine()
    {
        using var vm = Create(new AudioSettings(), (_, _) => throw new Xunit.Sdk.XunitException("Must not open hardware"));
        vm.ToggleEngineCommand.Execute(null);
        Assert.False(vm.IsEngineRunning);
        Assert.True(vm.CanEdit);
        Assert.Contains("Choose an input", vm.EngineError);
    }

    [Fact]
    public async Task ToggleRestartsWithFreshSettingsAndClosingDisposesTheSession()
    {
        var drivers = new List<FakeDriver>();
        var chains = new List<LiveProcessorChain>();
        var options = new List<AudioSessionOptions>();
        var settings = Settings();
        using var vm = Create(settings, (snapshot, processor) =>
        {
            var driver = new FakeDriver();
            drivers.Add(driver);
            options.Add(snapshot);
            chains.Add(Assert.IsType<LiveProcessorChain>(processor));
            return new AudioSession(snapshot, processor, _ => driver);
        });

        vm.ToggleEngineCommand.Execute(null);
        var firstObservation = vm.SessionObservation;
        Assert.True(vm.IsEngineRunning);
        Assert.False(vm.CanEdit);
        Assert.False(vm.Settings.IsAudioEditable);
        Assert.All(vm.ProcessorChain.Processors, processor => Assert.True(processor.IsEditable));
        vm.ToggleEngineCommand.Execute(null);
        Assert.False(vm.IsEngineRunning);
        Assert.True(vm.Settings.IsAudioEditable);
        Assert.All(vm.ProcessorChain.Processors, processor => Assert.True(processor.IsEditable));
        Assert.Equal(1, drivers[0].Disposals);
        vm.ProcessorChain.Processors[0].Parameters[0].Value = 8;
        settings.SampleRate = 44100;
        vm.ToggleEngineCommand.Execute(null);
        await firstObservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsEngineRunning); // An old completion must not stop the new session.
        Assert.Equal(48000, options[0].SampleRate);
        Assert.Equal(44100, options[1].SampleRate);
        Assert.NotSame(chains[0], chains[1]);

        vm.Dispose();
        await vm.SessionObservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsEngineRunning);
        Assert.Equal(1, drivers[1].Disposals);
        vm.ToggleEngineCommand.Execute(null);
        Assert.Equal(2, drivers.Count);
    }

    [Fact]
    public async Task EffectsAndBoardBypassUpdateWithoutRestartingAudio()
    {
        var driver = new FakeDriver();
        var opened = 0;
        using var vm = Create(Settings(), (options, processor) =>
        {
            opened++;
            return new AudioSession(options, processor, _ => driver);
        });
        vm.ProcessorChain.NewBoard();
        var effect = vm.ProcessorChain.AddEffect("hardOverdrive");
        vm.ProcessorChain.Connect(ProcessorChainViewModel.InputId, effect.Id);
        vm.ProcessorChain.Connect(effect.Id, ProcessorChainViewModel.OutputId);
        effect.Parameters.Single(parameter => parameter.Key == "drive").Value = 2;
        effect.Parameters.Single(parameter => parameter.Key == "outputGain").Value = .5;
        vm.ToggleEngineCommand.Execute(null);
        Assert.True(vm.IsEngineRunning);
        Assert.Equal(.1f, PlayBlock()[^1], 5);

        effect.Parameters.Single(parameter => parameter.Key == "drive").Value = 4;
        await vm.ChainUpdateObservation;
        Assert.Equal(.2f, PlayBlock()[^1], 5);
        effect.IsBypassed = true;
        await vm.ChainUpdateObservation;
        Assert.Equal(.1f, PlayBlock()[^1], 5);
        effect.IsBypassed = false;
        vm.IsChainBypassed = true;
        await vm.ChainUpdateObservation;
        Assert.Equal(.1f, PlayBlock()[^1], 5);
        effect.Parameters.Single(parameter => parameter.Key == "drive").Value = 8;
        await vm.ChainUpdateObservation;
        Assert.Equal(.1f, PlayBlock()[^1], 5);
        vm.IsChainBypassed = false;
        Assert.Equal(.4f, PlayBlock()[^1], 5);
        vm.ProcessorChain.SelectedProcessor = effect;
        Assert.True(vm.ProcessorChain.RemoveSelected());
        await vm.ChainUpdateObservation;
        Assert.Equal(0f, PlayBlock()[^1], 5);
        Assert.Contains("No output path", vm.EngineStatus);
        vm.ProcessorChain.Connect(ProcessorChainViewModel.InputId, ProcessorChainViewModel.OutputId);
        await vm.ChainUpdateObservation;
        Assert.Equal(.1f, PlayBlock()[^1], 5);

        Assert.Equal(1, opened);
        Assert.Equal(0, driver.Stops);
        Assert.Equal(0, driver.Disposals);
        Assert.True(vm.IsEngineRunning);
        Assert.Null(vm.EngineError);

        float[] PlayBlock()
        {
            driver.PushFloat(Enumerable.Repeat(.1f, 1024).ToArray());
            return driver.ReadFloat(2048);
        }
    }

    [Fact]
    public async Task DanglingEffectsStaySilentUntilConnectedAndRewiringUpdatesLiveAudio()
    {
        var driver = new FakeDriver();
        var opened = 0;
        using var vm = Create(Settings(), (options, processor) =>
        {
            opened++;
            return new AudioSession(options, processor, _ => driver);
        });
        vm.ProcessorChain.NewBoard();
        vm.ToggleEngineCommand.Execute(null);
        var previousUpdate = vm.ChainUpdateObservation;
        var first = vm.ProcessorChain.AddEffect("gain");
        first.Parameters[0].Value = 2;
        Assert.Same(previousUpdate, vm.ChainUpdateObservation);
        Assert.Equal(.1f, PlayBlock()[^1], 5);

        Assert.True(vm.ProcessorChain.Connect(ProcessorChainViewModel.InputId, first.Id));
        await vm.ChainUpdateObservation;
        Assert.Equal(0f, PlayBlock()[^1], 5);
        Assert.Contains("No output path", vm.EngineStatusDetail);
        Assert.True(vm.ProcessorChain.Connect(first.Id, ProcessorChainViewModel.OutputId));
        await vm.ChainUpdateObservation;
        Assert.Equal(.2f, PlayBlock()[^1], 5);
        Assert.DoesNotContain("No output path", vm.EngineStatusDetail);

        previousUpdate = vm.ChainUpdateObservation;
        var second = vm.ProcessorChain.AddEffect("gain");
        second.Parameters[0].Value = 3;
        Assert.Same(previousUpdate, vm.ChainUpdateObservation);
        Assert.Equal(.2f, PlayBlock()[^1], 5);
        Assert.True(vm.ProcessorChain.Connect(first.Id, second.Id));
        Assert.True(vm.ProcessorChain.Connect(second.Id, ProcessorChainViewModel.OutputId));
        await vm.ChainUpdateObservation;
        Assert.Equal(.6f, PlayBlock()[^1], 5);

        vm.IsChainBypassed = true;
        vm.ProcessorChain.Disconnect(first.Id, second.Id);
        await vm.ChainUpdateObservation;
        Assert.Equal(.1f, PlayBlock()[^1], 5);
        Assert.Contains("Board bypassed", vm.EngineStatusDetail);
        vm.IsChainBypassed = false;
        Assert.Equal(0f, PlayBlock()[^1], 5);
        Assert.Contains("No output path", vm.EngineStatusDetail);
        Assert.Equal(1, opened);
        Assert.Equal(0, driver.Stops);
        Assert.Null(vm.EngineError);

        float[] PlayBlock()
        {
            driver.PushFloat(Enumerable.Repeat(.1f, 1024).ToArray());
            return driver.ReadFloat(2048);
        }
    }

    [Fact]
    public async Task RapidEditsShareOneUpdateAndPublishOnlyTheLatestValues()
    {
        var driver = new FakeDriver();
        using var vm = Create(Settings(), (options, processor) => new AudioSession(options, processor, _ => driver));
        vm.ProcessorChain.NewBoard();
        var effect = vm.ProcessorChain.AddEffect("gain");
        vm.ProcessorChain.Connect(ProcessorChainViewModel.InputId, effect.Id);
        vm.ProcessorChain.Connect(effect.Id, ProcessorChainViewModel.OutputId);
        vm.ToggleEngineCommand.Execute(null);
        var context = new QueuedSynchronizationContext();
        Task pending = Task.CompletedTask;
        context.Capture(() =>
        {
            effect.Parameters[0].Value = 1.1;
            pending = vm.ChainUpdateObservation;
            for (var step = 12; step <= 30; step++)
            {
                effect.Parameters[0].Value = step / 10d;
                Assert.Same(pending, vm.ChainUpdateObservation);
            }
        });
        // The queued UI continuation cannot compile until the UI context runs it.
        Assert.False(pending.IsCompleted);
        driver.PushFloat([.1f]);
        Assert.Equal(.1f, driver.ReadFloat(2)[0], 5);
        await context.RunNextAsync();
        await pending;
        driver.PushFloat(Enumerable.Repeat(.1f, 1024).ToArray());
        Assert.Equal(.3f, driver.ReadFloat(2048)[^1], 5);
        Assert.Equal(0, driver.Stops);
        Assert.Equal(0, driver.Disposals);
    }

    [Fact]
    public async Task PendingUpdateCannotPublishAfterStoppingOrIntoARestartedSession()
    {
        var chains = new List<LiveProcessorChain>();
        using var vm = Create(Settings(), (options, processor) =>
        {
            chains.Add(Assert.IsType<LiveProcessorChain>(processor));
            return new AudioSession(options, processor, _ => new FakeDriver());
        });
        vm.ProcessorChain.NewBoard();
        var effect = vm.ProcessorChain.AddEffect("gain");
        vm.ProcessorChain.Connect(ProcessorChainViewModel.InputId, effect.Id);
        vm.ProcessorChain.Connect(effect.Id, ProcessorChainViewModel.OutputId);
        vm.ToggleEngineCommand.Execute(null);
        var context = new QueuedSynchronizationContext();
        context.Capture(() => effect.Parameters[0].Value = 2);
        var pending = vm.ChainUpdateObservation;
        vm.ToggleEngineCommand.Execute(null);
        effect.Parameters[0].Value = 3;
        vm.ToggleEngineCommand.Execute(null);
        await context.RunNextAsync();
        await pending;

        var oldSamples = Enumerable.Repeat(.1f, 2048).ToArray();
        var newSamples = (float[])oldSamples.Clone();
        chains[0].Process(oldSamples);
        chains[1].Process(newSamples);
        Assert.Equal(.1f, oldSamples[^1], 5);
        Assert.Equal(.3f, newSamples[^1], 5);
        Assert.True(vm.IsEngineRunning);
        Assert.Null(vm.EngineError);
    }

    [Fact]
    public async Task PendingUpdateCannotPublishAfterDisposal()
    {
        LiveProcessorChain? chain = null;
        using var vm = Create(Settings(), (options, processor) =>
        {
            chain = Assert.IsType<LiveProcessorChain>(processor);
            return new AudioSession(options, processor, _ => new FakeDriver());
        });
        vm.ProcessorChain.NewBoard();
        var effect = vm.ProcessorChain.AddEffect("gain");
        vm.ProcessorChain.Connect(ProcessorChainViewModel.InputId, effect.Id);
        vm.ProcessorChain.Connect(effect.Id, ProcessorChainViewModel.OutputId);
        vm.ToggleEngineCommand.Execute(null);
        var context = new QueuedSynchronizationContext();
        context.Capture(() => effect.Parameters[0].Value = 2);
        var pending = vm.ChainUpdateObservation;
        vm.Dispose();
        await context.RunNextAsync();
        await pending;

        var samples = Enumerable.Repeat(.1f, 2048).ToArray();
        chain!.Process(samples);
        Assert.Equal(.1f, samples[^1], 5);
        Assert.False(vm.IsEngineRunning);
        Assert.Null(vm.EngineError);
    }

    [Fact]
    public void StartingWithBoardBypassImmediatelyPlaysCleanInput()
    {
        var driver = new FakeDriver();
        using var vm = Create(Settings(), (options, processor) => new AudioSession(options, processor, _ => driver));
        vm.IsChainBypassed = true;
        vm.ToggleEngineCommand.Execute(null);
        driver.PushFloat([.1f, -.2f]);
        Assert.Equal(new float[] { .1f, .1f, -.2f, -.2f }, driver.ReadFloat(4));
        Assert.Contains("Board bypassed", vm.EngineStatus);
    }

    [Fact]
    public void FailedStartReportsErrorAndCanRetry()
    {
        var broken = new FakeDriver { StartError = new InvalidOperationException("Driver unavailable") };
        var working = new FakeDriver();
        var next = broken;
        using var vm = Create(Settings(), (options, processor) => new AudioSession(options, processor, _ => next));
        vm.ToggleEngineCommand.Execute(null);
        Assert.False(vm.IsEngineRunning);
        Assert.Equal("Driver unavailable", vm.EngineError);
        Assert.Equal(1, broken.Disposals);
        next = working;
        vm.ToggleEngineCommand.Execute(null);
        Assert.True(vm.IsEngineRunning);
        Assert.Null(vm.EngineError);
    }

    [Fact]
    public async Task DeviceFailureTurnsOffTheEngineAndReleasesHardware()
    {
        var driver = new FakeDriver();
        using var vm = Create(Settings(), (options, processor) => new AudioSession(options, processor, _ => driver));
        vm.ToggleEngineCommand.Execute(null);
        driver.Fail(new IOException("Device disconnected"));
        await vm.SessionObservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsEngineRunning);
        Assert.True(vm.CanEdit);
        Assert.Equal("Device disconnected", vm.EngineError);
        Assert.Equal(1, driver.Disposals);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly TaskCompletionSource<(SendOrPostCallback Callback, object? State)> posted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Post(SendOrPostCallback callback, object? state) => posted.TrySetResult((callback, state));

        public void Capture(Action action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { action(); }
            finally { SetSynchronizationContext(previous); }
        }

        public async Task RunNextAsync()
        {
            var (callback, state) = await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Capture(() => callback(state));
        }
    }

    private sealed class FakeDriver : IAudioDriver
    {
        public WaveFormat InputFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        public Exception? StartError { get; init; }
        public IWaveProvider? Source { get; private set; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? Stopped;
        public void InitializeOutput(IWaveProvider source) => Source = source;
        public void Start() { if (StartError is not null) throw StartError; }
        public void Stop() => Stops++;
        public void Dispose() => Disposals++;
        public void Fail(Exception error) => Stopped?.Invoke(this, new StoppedEventArgs(error));
        public void PushFloat(float[] samples)
        {
            var bytes = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));
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
