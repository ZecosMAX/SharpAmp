using NAudio.Wave;
using SharpAmp.Avalonia.Models;
using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;
using SharpAmp.Core.Dsp.Processors;

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
        var chains = new List<ProcessorChain>();
        var options = new List<AudioSessionOptions>();
        var settings = Settings();
        using var vm = Create(settings, (snapshot, processor) =>
        {
            var driver = new FakeDriver();
            drivers.Add(driver);
            options.Add(snapshot);
            chains.Add(Assert.IsType<ProcessorChain>(processor));
            return new AudioSession(snapshot, processor, _ => driver);
        });

        vm.ToggleEngineCommand.Execute(null);
        var firstObservation = vm.SessionObservation;
        Assert.True(vm.IsEngineRunning);
        Assert.False(vm.CanEdit);
        Assert.All(vm.ProcessorChain.Processors, processor => Assert.False(processor.IsEditable));
        vm.ToggleEngineCommand.Execute(null);
        Assert.False(vm.IsEngineRunning);
        Assert.All(vm.ProcessorChain.Processors, processor => Assert.True(processor.IsEditable));
        Assert.Equal(1, drivers[0].Disposals);
        vm.ProcessorChain.Processors[0].Parameters[0].Value = 8;
        settings.SampleRate = 44100;
        vm.ToggleEngineCommand.Execute(null);
        await firstObservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsEngineRunning); // An old completion must not stop the new session.
        Assert.Equal(48000, options[0].SampleRate);
        Assert.Equal(44100, options[1].SampleRate);
        Assert.Equal(8, Assert.IsType<HardOverdriveProcessor>(chains[1].Processors[0]).Drive);
        Assert.NotSame(chains[0], chains[1]);

        vm.Dispose();
        await vm.SessionObservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsEngineRunning);
        Assert.Equal(1, drivers[1].Disposals);
        vm.ToggleEngineCommand.Execute(null);
        Assert.Equal(2, drivers.Count);
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

    private sealed class FakeDriver : IAudioDriver
    {
        public WaveFormat InputFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        public Exception? StartError { get; init; }
        public int Disposals { get; private set; }
        public event EventHandler<WaveInEventArgs>? DataAvailable { add { } remove { } }
        public event EventHandler<StoppedEventArgs>? Stopped;
        public void InitializeOutput(IWaveProvider source) { }
        public void Start() { if (StartError is not null) throw StartError; }
        public void Stop() { }
        public void Dispose() => Disposals++;
        public void Fail(Exception error) => Stopped?.Invoke(this, new StoppedEventArgs(error));
    }
}
