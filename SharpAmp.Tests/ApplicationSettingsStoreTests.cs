using System.Text.Json.Nodes;
using SharpAmp.Avalonia.Models;
using SharpAmp.Avalonia.Services;
using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Core.Audio;

namespace SharpAmp.Tests;

public sealed class ApplicationSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SharpAmp-settings-tests-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(directory, "settings.json");

    private static AudioSettings Audio() => new()
    {
        InputBackend = AudioBackend.Asio,
        OutputBackend = AudioBackend.Wasapi,
        InputDevice = new(AudioBackend.Asio, AudioDirection.Input, "stable-input-id", "Input device"),
        OutputDevice = new(AudioBackend.Wasapi, AudioDirection.Output, "stable-output-id", "Output device"),
        SampleRate = 96000, InputChannels = 2, OutputChannels = 4,
        LatencyMilliseconds = 20, BufferMilliseconds = 120,
        AsioInputChannelOffset = 3
    };

    [Fact]
    public void MissingSettingsUseDefaultsWithoutCreatingAFile()
    {
        var loaded = new ApplicationSettingsStore(SettingsPath).Load();

        Assert.Null(loaded.Error);
        Assert.Equal(48000, loaded.AudioSettings.SampleRate);
        Assert.Null(loaded.AudioSettings.InputDevice);
        Assert.True(loaded.Preferences.ShowHints);
        Assert.True(loaded.Preferences.SnapToGrid);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void AudioConfigurationAndWorkspacePreferencesRoundTripTogether()
    {
        var store = new ApplicationSettingsStore(SettingsPath);
        var audio = Audio();
        store.Save(audio, new ApplicationPreferences { ShowHints = false, SnapToGrid = false });

        var loaded = store.Load();

        Assert.Null(loaded.Error);
        Assert.NotSame(audio, loaded.AudioSettings);
        Assert.Equal(audio.ToSessionOptions(), loaded.AudioSettings.ToSessionOptions());
        Assert.Equal(audio.InputDevice, loaded.AudioSettings.InputDevice);
        Assert.Equal(audio.OutputDevice, loaded.AudioSettings.OutputDevice);
        Assert.False(loaded.Preferences.ShowHints);
        Assert.False(loaded.Preferences.SnapToGrid);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void RestoredDeviceIdsAreMatchedToCurrentEnumerationWithoutChoosingAReplacement()
    {
        var store = new ApplicationSettingsStore(SettingsPath);
        store.Save(Audio(), new ApplicationPreferences());
        var loaded = store.Load();
        var currentInput = new AudioDevice(AudioBackend.Asio, AudioDirection.Input, "stable-input-id", "Renamed input");
        var vm = new AudioSettingsViewModel(loaded.AudioSettings, (backend, direction) =>
            direction == AudioDirection.Input ? [currentInput] : [new(backend, direction, "unrelated-id", "Different speakers")]);

        Assert.Same(currentInput, loaded.AudioSettings.InputDevice);
        Assert.Null(loaded.AudioSettings.OutputDevice);
        Assert.Contains("unavailable", vm.OutputDeviceMessage);
        Assert.True(vm.HasValidationError);
    }

    [Fact]
    public void PreferencesCanBeSavedBeforeAudioDevicesHaveBeenChosen()
    {
        var store = new ApplicationSettingsStore(SettingsPath);
        store.Save(new AudioSettings(), new ApplicationPreferences { ShowHints = false });

        var loaded = store.Load();

        Assert.Null(loaded.Error);
        Assert.False(loaded.Preferences.ShowHints);
        Assert.Null(loaded.AudioSettings.InputDevice);
        Assert.Null(loaded.AudioSettings.OutputDevice);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    public void UnreadableDocumentsAreReportedAndNeverOverwritten(string original)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, original);
        var store = new ApplicationSettingsStore(SettingsPath);

        var loaded = store.Load();

        Assert.NotNull(loaded.Error);
        Assert.Equal(48000, loaded.AudioSettings.SampleRate);
        Assert.ThrowsAny<Exception>(() => store.Save(Audio(), new ApplicationPreferences()));
        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("Version", 2)]
    [InlineData("SampleRate", 0)]
    [InlineData("BufferMilliseconds", 10)]
    public void UnsupportedVersionAndInvalidAudioSettingsArePreserved(string field, int value)
    {
        var store = new ApplicationSettingsStore(SettingsPath);
        store.Save(Audio(), new ApplicationPreferences());
        var document = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        if (field == "Version") document[field] = value;
        else document["Audio"]![field] = value;
        var original = document.ToJsonString();
        File.WriteAllText(SettingsPath, original);

        var loaded = store.Load();

        Assert.NotNull(loaded.Error);
        Assert.Throws<InvalidDataException>(() => store.Save(Audio(), new ApplicationPreferences()));
        Assert.Equal(original, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void InvalidEditsCannotReplaceAValidSettingsFile()
    {
        var store = new ApplicationSettingsStore(SettingsPath);
        store.Save(Audio(), new ApplicationPreferences());
        var original = File.ReadAllText(SettingsPath);
        var edited = Audio();
        edited.BufferMilliseconds = 10;

        Assert.Throws<InvalidOperationException>(() => store.Save(edited, new ApplicationPreferences()));
        Assert.Equal(original, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void ViewModelSavesExplicitlyAndReportsFailuresWithoutLosingEdits()
    {
        var store = new ApplicationSettingsStore(SettingsPath);
        var audio = new AudioSettingsViewModel(new AudioSettings(), (_, _) => []);
        var vm = new SettingsViewModel(audio, settingsStore: store);
        vm.Preferences.SnapToGrid = false;
        Assert.True(vm.HasUnsavedChanges);
        Assert.False(File.Exists(SettingsPath));

        vm.SaveCommand.Execute(null);

        Assert.False(vm.HasPersistenceError);
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(store.Load().Preferences.SnapToGrid);
        File.WriteAllText(SettingsPath, "invalid document");
        vm.Preferences.ShowHints = false;

        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasPersistenceError);
        Assert.True(vm.HasUnsavedChanges);
        Assert.False(vm.Preferences.ShowHints);
        Assert.Contains("could not be saved", vm.PersistenceMessage);
        Assert.Equal("invalid document", File.ReadAllText(SettingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
