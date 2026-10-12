using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SharpAmp.Core.Audio;

namespace SharpAmp.Avalonia.Models;

/// <summary>Editable settings retained for the lifetime of the main window.</summary>
public partial class AudioSettings : ObservableObject
{
    [ObservableProperty] private AudioBackend inputBackend = AudioBackend.Wasapi;
    [ObservableProperty] private AudioBackend outputBackend = AudioBackend.Wasapi;
    [ObservableProperty] private AudioDevice? inputDevice;
    [ObservableProperty] private AudioDevice? outputDevice;
    [ObservableProperty] private int sampleRate = 48000;
    [ObservableProperty] private int inputChannels = 1;
    [ObservableProperty] private int outputChannels = 2;
    [ObservableProperty] private int latencyMilliseconds = 50;
    [ObservableProperty] private int bufferMilliseconds = 250;
    [ObservableProperty] private int asioInputChannelOffset;
    [ObservableProperty] private int asioOutputChannelOffset;

    public string? GetValidationError()
    {
        if (!Enum.IsDefined(InputBackend) || !Enum.IsDefined(OutputBackend))
            return "Choose valid input and output backends.";
        if (InputDevice is null || OutputDevice is null)
            return "Choose an input device and an output device.";
        if (InputDevice.Backend != InputBackend || InputDevice.Direction != AudioDirection.Input ||
            OutputDevice.Backend != OutputBackend || OutputDevice.Direction != AudioDirection.Output ||
            string.IsNullOrWhiteSpace(InputDevice.Id) || string.IsNullOrWhiteSpace(OutputDevice.Id))
            return "Choose devices that match the selected backends and directions.";
        if (SampleRate is < 8000 or > 192000)
            return "Sample rate must be between 8,000 and 192,000 Hz.";
        if (InputChannels is < 1 or > 32 || OutputChannels is < 1 or > 32)
            return "Channel counts must be between 1 and 32.";
        if (LatencyMilliseconds is < 5 or > 1000)
            return "Requested latency must be between 5 and 1,000 ms.";
        if (BufferMilliseconds < LatencyMilliseconds * 2 || BufferMilliseconds > 5000)
            return "Buffer duration must be at least twice the requested latency and at most 5,000 ms.";
        if (AsioInputChannelOffset < 0 || AsioOutputChannelOffset < 0)
            return "ASIO channel offsets cannot be negative.";
        if ((InputBackend != AudioBackend.Asio && AsioInputChannelOffset != 0) ||
            (OutputBackend != AudioBackend.Asio && AsioOutputChannelOffset != 0))
            return "Channel offsets are only available for ASIO devices.";
        return null;
    }

    /// <summary>Creates a snapshot without opening hardware. Format support is checked at session start.</summary>
    public AudioSessionOptions ToSessionOptions()
    {
        if (GetValidationError() is { } error) throw new InvalidOperationException(error);
        return new AudioSessionOptions(InputDevice!, OutputDevice!)
        {
            SampleRate = SampleRate,
            InputChannels = InputChannels,
            OutputChannels = OutputChannels,
            LatencyMilliseconds = LatencyMilliseconds,
            BufferMilliseconds = BufferMilliseconds,
            AsioInputChannelOffset = AsioInputChannelOffset,
            AsioOutputChannelOffset = AsioOutputChannelOffset
        };
    }
}
