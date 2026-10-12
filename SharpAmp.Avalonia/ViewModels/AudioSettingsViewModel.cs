using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpAmp.Avalonia.Models;
using SharpAmp.Core.Audio;

namespace SharpAmp.Avalonia.ViewModels;

public partial class AudioSettingsViewModel : SettingsPageViewModel
{
    private readonly Func<AudioBackend, AudioDirection, IReadOnlyList<AudioDevice>> getDevices;
    public AudioSettings Settings { get; }
    public IReadOnlyList<AudioBackend> Backends { get; } = Enum.GetValues<AudioBackend>();
    public IReadOnlyList<int> SampleRates { get; } = [8000, 16000, 22050, 32000, 44100, 48000, 88200, 96000, 176400, 192000];

    [ObservableProperty] private IReadOnlyList<AudioDevice> inputDevices = [];
    [ObservableProperty] private IReadOnlyList<AudioDevice> outputDevices = [];
    [ObservableProperty] private string? inputDeviceMessage;
    [ObservableProperty] private string? outputDeviceMessage;

    public bool IsAsioInput => Settings.InputBackend == AudioBackend.Asio;
    public bool IsAsioOutput => Settings.OutputBackend == AudioBackend.Asio;
    public bool UsesAsio => IsAsioInput || IsAsioOutput;
    public string? ValidationMessage => Settings.GetValidationError();
    public bool HasValidationError => ValidationMessage is not null;

    public AudioSettingsViewModel(AudioSettings settings,
        Func<AudioBackend, AudioDirection, IReadOnlyList<AudioDevice>>? getDevices = null) : base("Audio")
    {
        Settings = settings;
        this.getDevices = getDevices ?? AudioDeviceCatalog.GetDevices;
        Settings.PropertyChanged += OnSettingsChanged;
        RefreshDevices();
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        RefreshDevices(AudioDirection.Input);
        RefreshDevices(AudioDirection.Output);
    }

    private void RefreshDevices(AudioDirection direction)
    {
        var isInput = direction == AudioDirection.Input;
        var backend = isInput ? Settings.InputBackend : Settings.OutputBackend;
        var previous = isInput ? Settings.InputDevice : Settings.OutputDevice;
        IReadOnlyList<AudioDevice> devices;
        string? message;
        try
        {
            devices = getDevices(backend, direction);
            message = devices.Count == 0 ? $"No {direction.ToString().ToLowerInvariant()} devices found. Check your connection and refresh." : null;
        }
        catch (Exception error)
        {
            // A broken driver must not hide devices from the other direction.
            devices = [];
            message = $"Could not load {direction.ToString().ToLowerInvariant()} devices: {error.Message}";
        }

        // Keep explicit selections by stable ID; never silently switch to another device.
        var selected = previous is null ? null : devices.FirstOrDefault(device => device.Backend == previous.Backend && device.Id == previous.Id);
        if (isInput)
        {
            InputDevices = devices;
            Settings.InputDevice = selected;
            InputDeviceMessage = message;
        }
        else
        {
            OutputDevices = devices;
            Settings.OutputDevice = selected;
            OutputDeviceMessage = message;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioSettings.InputBackend))
        {
            if (!IsAsioInput) Settings.AsioInputChannelOffset = 0;
            RefreshDevices(AudioDirection.Input);
            OnPropertyChanged(nameof(IsAsioInput));
            OnPropertyChanged(nameof(UsesAsio));
        }
        else if (e.PropertyName == nameof(AudioSettings.OutputBackend))
        {
            if (!IsAsioOutput) Settings.AsioOutputChannelOffset = 0;
            RefreshDevices(AudioDirection.Output);
            OnPropertyChanged(nameof(IsAsioOutput));
            OnPropertyChanged(nameof(UsesAsio));
        }
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
    }
}
