using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpAmp.Avalonia.Models;
using SharpAmp.Core.Audio;

namespace SharpAmp.Avalonia.Services;

public sealed record ApplicationSettingsLoadResult(
    AudioSettings AudioSettings, ApplicationPreferences Preferences, string? Error = null);

/// <summary>Saves local preferences separately from portable sound presets and board layouts.</summary>
public sealed class ApplicationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath { get; }

    public ApplicationSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpAmp", "settings.json");
    }

    public ApplicationSettingsLoadResult Load()
    {
        try
        {
            var saved = ReadDocument();
            return new(saved.Audio.ToModel(), new ApplicationPreferences
            {
                ShowHints = saved.Preferences.ShowHints,
                SnapToGrid = saved.Preferences.SnapToGrid
            });
        }
        catch (FileNotFoundException) { return new(new AudioSettings(), new ApplicationPreferences()); }
        catch (DirectoryNotFoundException) { return new(new AudioSettings(), new ApplicationPreferences()); }
        catch (Exception error) when (IsStorageError(error))
        {
            return new(new AudioSettings(), new ApplicationPreferences(),
                $"Saved settings could not be loaded. Your settings file has been kept unchanged. {error.Message}");
        }
    }

    public void Save(AudioSettings audio, ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(preferences);
        if (audio.GetValidationError(requireDevices: false) is { } error)
            throw new InvalidOperationException(error);

        // Never replace an unreadable, invalid, or newer settings document with defaults.
        try { _ = ReadDocument(); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }

        var document = new SettingsDocument
        {
            Version = 1,
            Audio = AudioDocument.FromModel(audio),
            Preferences = new PreferencesDocument { ShowHints = preferences.ShowHints, SnapToGrid = preferences.SnapToGrid }
        };
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            // Flush the complete document before an atomic rename on the same volume.
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private SettingsDocument ReadDocument()
    {
        var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(FilePath), JsonOptions)
            ?? throw new InvalidDataException("The settings document is empty.");
        if (document.Version != 1)
            throw new InvalidDataException("This version of the settings document is not supported.");
        if (document.Audio is null || document.Preferences is null)
            throw new InvalidDataException("The settings document is incomplete.");
        if (document.Audio.ToModel().GetValidationError(requireDevices: false) is { } error)
            throw new InvalidDataException(error);
        return document;
    }

    private static bool IsStorageError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;

    private sealed class SettingsDocument
    {
        public required int Version { get; init; }
        public required AudioDocument Audio { get; init; }
        public required PreferencesDocument Preferences { get; init; }
    }

    private sealed class PreferencesDocument
    {
        public required bool ShowHints { get; init; }
        public required bool SnapToGrid { get; init; }
    }

    private sealed class AudioDocument
    {
        public required AudioBackend InputBackend { get; init; }
        public required AudioBackend OutputBackend { get; init; }
        public required AudioDevice? InputDevice { get; init; }
        public required AudioDevice? OutputDevice { get; init; }
        public required int SampleRate { get; init; }
        public required int InputChannels { get; init; }
        public required int OutputChannels { get; init; }
        public required int LatencyMilliseconds { get; init; }
        public required int BufferMilliseconds { get; init; }
        public required int AsioInputChannelOffset { get; init; }
        public required int AsioOutputChannelOffset { get; init; }

        public AudioSettings ToModel() => new()
        {
            InputBackend = InputBackend, OutputBackend = OutputBackend,
            InputDevice = InputDevice, OutputDevice = OutputDevice,
            SampleRate = SampleRate, InputChannels = InputChannels, OutputChannels = OutputChannels,
            LatencyMilliseconds = LatencyMilliseconds, BufferMilliseconds = BufferMilliseconds,
            AsioInputChannelOffset = AsioInputChannelOffset, AsioOutputChannelOffset = AsioOutputChannelOffset
        };

        public static AudioDocument FromModel(AudioSettings audio) => new()
        {
            InputBackend = audio.InputBackend, OutputBackend = audio.OutputBackend,
            InputDevice = audio.InputDevice, OutputDevice = audio.OutputDevice,
            SampleRate = audio.SampleRate, InputChannels = audio.InputChannels, OutputChannels = audio.OutputChannels,
            LatencyMilliseconds = audio.LatencyMilliseconds, BufferMilliseconds = audio.BufferMilliseconds,
            AsioInputChannelOffset = audio.AsioInputChannelOffset, AsioOutputChannelOffset = audio.AsioOutputChannelOffset
        };
    }
}
