using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpAmp.Core.Audio;
using SharpAmp.Core.Dsp;

namespace SharpAmp.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Func<AudioSessionOptions, IDigitalSignalProcessor, AudioSession> createSession;
    private AudioSession? session;
    private bool disposed;

    public Models.AudioSettings AudioSettings { get; }
    public SettingsViewModel Settings { get; }
    public ProcessorChainViewModel ProcessorChain { get; } = new();
    internal Task SessionObservation { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(EngineButtonText), nameof(EngineStatus))]
    private bool isEngineRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEngineError))]
    private string? engineError;

    public bool CanEdit => !IsEngineRunning;
    public bool HasEngineError => EngineError is not null;
    public string EngineButtonText => IsEngineRunning ? "Stop audio" : "Start audio";
    public string EngineStatus => IsEngineRunning
        ? "ENGINE ON · Stop audio to edit the chain or audio settings."
        : "ENGINE OFF · Set your sound and choose devices in Settings, then start audio.";

    partial void OnIsEngineRunningChanged(bool value)
    {
        foreach (var processor in ProcessorChain.Processors)
            processor.IsEditable = !value;
    }

    public MainWindowViewModel() : this(new Models.AudioSettings()) { }

    public MainWindowViewModel(Models.AudioSettings audioSettings,
        Func<AudioBackend, AudioDirection, IReadOnlyList<AudioDevice>>? getDevices = null,
        Func<AudioSessionOptions, IDigitalSignalProcessor, AudioSession>? createSession = null)
    {
        AudioSettings = audioSettings;
        Settings = new SettingsViewModel(new AudioSettingsViewModel(AudioSettings, getDevices));
        this.createSession = createSession ?? ((options, processor) => new AudioSession(options, processor));
    }

    [RelayCommand]
    private void ToggleEngine()
    {
        if (disposed) return;
        EngineError = null;
        if (session is not null)
        {
            StopSession();
            return;
        }

        try
        {
            session = createSession(AudioSettings.ToSessionOptions(), ProcessorChain.CreateChain());
            session.Start();
            IsEngineRunning = true;
            SessionObservation = ObserveSessionAsync(session);
        }
        catch (Exception error)
        {
            EngineError = error.Message;
            StopSession();
        }
    }

    private async Task ObserveSessionAsync(AudioSession observed)
    {
        // Called on the UI thread; retain its synchronization context for bound properties.
        var error = await observed.Completion;
        if (!ReferenceEquals(session, observed)) return;
        EngineError = error?.Message;
        StopSession();
    }

    private void StopSession()
    {
        var previous = session;
        session = null;
        try
        {
            previous?.Dispose();
            if (previous?.Completion.IsCompletedSuccessfully == true && previous.Completion.Result is { } error)
                EngineError ??= error.Message;
        }
        catch (Exception error)
        {
            EngineError ??= error.Message;
        }
        finally
        {
            IsEngineRunning = false;
        }
    }

    public void Dispose()
    {
        disposed = true;
        StopSession();
        GC.SuppressFinalize(this);
    }
}
