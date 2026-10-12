using SharpAmp.Avalonia.Models;
using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Core.Audio;

namespace SharpAmp.Tests;

public sealed class AudioSettingsTests
{
    private static AudioDevice Device(AudioBackend backend, AudioDirection direction, string id = "device") =>
        new(backend, direction, id, $"{backend} {direction}");

    private static AudioSettings ValidSettings() => new()
    {
        InputDevice = Device(AudioBackend.Wasapi, AudioDirection.Input),
        OutputDevice = Device(AudioBackend.Wasapi, AudioDirection.Output)
    };

    [Fact]
    public void SettingsCreateAnIndependentEngineOptionsSnapshot()
    {
        var settings = ValidSettings();
        settings.InputBackend = AudioBackend.Asio;
        settings.InputDevice = Device(AudioBackend.Asio, AudioDirection.Input);
        settings.SampleRate = 96000;
        settings.InputChannels = 2;
        settings.OutputChannels = 4;
        settings.LatencyMilliseconds = 20;
        settings.BufferMilliseconds = 100;
        settings.AsioInputChannelOffset = 3;

        var options = settings.ToSessionOptions();
        options.Validate();
        Assert.Same(settings.InputDevice, options.Input);
        Assert.Same(settings.OutputDevice, options.Output);
        Assert.Equal(96000, options.SampleRate);
        Assert.Equal(2, options.InputChannels);
        Assert.Equal(4, options.OutputChannels);
        Assert.Equal(20, options.LatencyMilliseconds);
        Assert.Equal(100, options.BufferMilliseconds);
        Assert.Equal(3, options.AsioInputChannelOffset);
        settings.SampleRate = 44100;
        Assert.Equal(96000, options.SampleRate);
    }

    [Theory]
    [InlineData(0, 50, 250)]
    [InlineData(192001, 50, 250)]
    [InlineData(48000, 4, 250)]
    [InlineData(48000, 1001, 2500)]
    [InlineData(48000, 50, 99)]
    [InlineData(48000, 50, 5001)]
    public void InvalidTimingCannotBeConverted(int rate, int latency, int buffer)
    {
        var settings = ValidSettings();
        settings.SampleRate = rate;
        settings.LatencyMilliseconds = latency;
        settings.BufferMilliseconds = buffer;
        Assert.NotNull(settings.GetValidationError());
        Assert.Throws<InvalidOperationException>(() => settings.ToSessionOptions());
    }

    [Fact]
    public void MissingMismatchedAndWrongDirectionDevicesAreRejected()
    {
        var settings = ValidSettings();
        settings.InputDevice = null;
        Assert.Throws<InvalidOperationException>(() => settings.ToSessionOptions());
        settings.InputDevice = Device(AudioBackend.Mme, AudioDirection.Input);
        Assert.Throws<InvalidOperationException>(() => settings.ToSessionOptions());
        settings.InputDevice = settings.OutputDevice;
        Assert.Throws<InvalidOperationException>(() => settings.ToSessionOptions());
    }

    [Fact]
    public void RefreshPreservesDeviceIdAndClearsDisconnectedSelection()
    {
        var settings = ValidSettings();
        var connected = true;
        var name = "Original name";
        var vm = new AudioSettingsViewModel(settings, (backend, direction) =>
            connected ? [Device(backend, direction) with { Name = name }] : [Device(backend, direction, "replacement")]);
        var original = settings.InputDevice;
        name = "Renamed device";
        vm.RefreshDevicesCommand.Execute(null);
        Assert.Equal(original!.Id, settings.InputDevice!.Id);
        Assert.Equal(name, settings.InputDevice.Name);
        Assert.NotSame(original, settings.InputDevice);
        connected = false;
        vm.RefreshDevicesCommand.Execute(null);
        Assert.Null(settings.InputDevice);
        Assert.Null(settings.OutputDevice);
        Assert.True(vm.HasValidationError);
    }

    [Fact]
    public void SwitchingBackendClearsDeviceAndResetsAsioOffset()
    {
        var settings = ValidSettings();
        settings.InputBackend = AudioBackend.Asio;
        settings.InputDevice = Device(AudioBackend.Asio, AudioDirection.Input);
        settings.AsioInputChannelOffset = 4;
        var vm = new AudioSettingsViewModel(settings, (backend, direction) => [Device(backend, direction)]);
        var output = settings.OutputDevice;

        settings.InputBackend = AudioBackend.Mme;

        Assert.Null(settings.InputDevice);
        Assert.Same(output, settings.OutputDevice);
        Assert.Equal(0, settings.AsioInputChannelOffset);
        Assert.False(vm.IsAsioInput);
        Assert.All(vm.InputDevices, device => Assert.Equal(AudioBackend.Mme, device.Backend));
    }

    [Fact]
    public void FailedEnumerationDoesNotHideOtherDirectionAndCanRecover()
    {
        var fail = true;
        var vm = new AudioSettingsViewModel(ValidSettings(), (backend, direction) =>
            fail && direction == AudioDirection.Input
                ? throw new InvalidOperationException("Driver unavailable")
                : [Device(backend, direction)]);
        Assert.Empty(vm.InputDevices);
        Assert.Null(vm.Settings.InputDevice);
        Assert.Contains("Driver unavailable", vm.InputDeviceMessage);
        Assert.Single(vm.OutputDevices);
        Assert.NotNull(vm.Settings.OutputDevice);

        fail = false;
        vm.RefreshDevicesCommand.Execute(null);
        Assert.Null(vm.InputDeviceMessage);
        Assert.Single(vm.InputDevices);
    }

    [Fact]
    public void NavigationRetainsSharedSettingsAndNotifiesValidationChanges()
    {
        var model = ValidSettings();
        var audio = new AudioSettingsViewModel(model, (backend, direction) => [Device(backend, direction)]);
        var settings = new SettingsViewModel(audio);
        var other = new OtherSettingsPage();
        settings.Pages.Add(other);
        var changed = new List<string?>();
        audio.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.BufferMilliseconds = 50;
        settings.SelectedPage = other;
        settings.SelectedPage = audio;

        Assert.Same(model, Assert.IsType<AudioSettingsViewModel>(settings.SelectedPage).Settings);
        Assert.Equal(50, audio.Settings.BufferMilliseconds);
        Assert.True(audio.HasValidationError);
        Assert.Contains(nameof(AudioSettingsViewModel.ValidationMessage), changed);
        model.BufferMilliseconds = 100;
        Assert.False(audio.HasValidationError);
        model.ToSessionOptions().Validate();
    }

    private sealed class OtherSettingsPage() : SettingsPageViewModel("Other");
}
