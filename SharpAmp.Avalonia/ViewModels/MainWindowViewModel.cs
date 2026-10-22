using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private LiveProcessorChain? liveChain;
    private LiveProcessorChain? scheduledUpdateChain;
    private bool disposed;

    public Models.AudioSettings AudioSettings { get; }
    public SettingsViewModel Settings { get; }
    public ProcessorChainViewModel ProcessorChain { get; } = new();
    internal Task SessionObservation { get; private set; } = Task.CompletedTask;
    internal Task ChainUpdateObservation { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(EngineButtonText), nameof(EngineStatus), nameof(EngineStatusLabel), nameof(EngineStatusDetail))]
    private bool isEngineRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEngineError))]
    private string? engineError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EngineStatus), nameof(EngineStatusDetail))]
    private bool isChainBypassed;

    public bool CanEdit => !IsEngineRunning;
    public bool HasEngineError => EngineError is not null;
    public string EngineButtonText => IsEngineRunning ? "Stop audio" : "Start audio";
    public string EngineStatusLabel => IsEngineRunning ? "LIVE" : "READY";
    public string EngineStatusDetail => IsEngineRunning
        ? IsChainBypassed
            ? "Board bypassed. You hear the clean input. Device changes require stopping audio."
            : !ProcessorChain.HasCompleteRoute
                ? "No output path. Connect INPUT to OUTPUT to hear your sound."
                : "Adjust, connect or bypass effects while you play. Stop audio to change devices."
        : "Shape your sound, choose audio devices in Settings, then start audio.";
    public string EngineStatus => $"{EngineStatusLabel} · {EngineStatusDetail}";

    partial void OnIsChainBypassedChanged(bool value)
    {
        if (liveChain is not null) liveChain.IsBypassed = value;
    }

    partial void OnIsEngineRunningChanged(bool value) => Settings.IsAudioEditable = !value;

    public MainWindowViewModel() : this(new Models.AudioSettings()) { }

    public MainWindowViewModel(Models.AudioSettings audioSettings,
        Func<AudioBackend, AudioDirection, IReadOnlyList<AudioDevice>>? getDevices = null,
        Func<AudioSessionOptions, IDigitalSignalProcessor, AudioSession>? createSession = null,
        Models.ApplicationPreferences? applicationPreferences = null,
        Services.ApplicationSettingsStore? settingsStore = null,
        string? settingsLoadError = null)
    {
        AudioSettings = audioSettings;
        Settings = new SettingsViewModel(new AudioSettingsViewModel(AudioSettings, getDevices),
            applicationPreferences, settingsStore, settingsLoadError);
        this.createSession = createSession ?? ((options, processor) => new AudioSession(options, processor));
        ProcessorChain.ChainChanged += OnChainChanged;
        ProcessorChain.PropertyChanged += OnBoardPropertyChanged;
    }

    private void OnBoardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProcessorChainViewModel.HasCompleteRoute)) return;
        OnPropertyChanged(nameof(EngineStatus));
        OnPropertyChanged(nameof(EngineStatusDetail));
    }

    private void OnChainChanged(object? sender, EventArgs e)
    {
        if (liveChain is null || !IsEngineRunning) return;
        if (ReferenceEquals(scheduledUpdateChain, liveChain)) return;
        scheduledUpdateChain = liveChain;
        ChainUpdateObservation = ApplyChainChangeAsync(liveChain);
    }

    private async Task ApplyChainChangeAsync(LiveProcessorChain target)
    {
        // Keep the UI context, and compile only the latest edit in each 30 ms interval.
        // In particular, moving a slider must not allocate delay buffers for every event.
        await Task.Delay(30);
        if (disposed || !IsEngineRunning || !ReferenceEquals(liveChain, target)
            || !ReferenceEquals(scheduledUpdateChain, target)) return;
        scheduledUpdateChain = null;
        try
        {
            target.Replace(ProcessorChain.CreateChain());
            EngineError = null;
        }
        catch (Exception error)
        {
            EngineError = $"The effect change could not be applied. Your previous sound is still playing. {error.Message}";
        }
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
            liveChain = new LiveProcessorChain(ProcessorChain.CreateChain(), IsChainBypassed);
            session = createSession(AudioSettings.ToSessionOptions(), liveChain);
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
        var previousChain = liveChain;
        session = null;
        liveChain = null;
        scheduledUpdateChain = null;
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
            try { previousChain?.Dispose(); }
            catch (Exception error) { EngineError ??= error.Message; }
            IsEngineRunning = false;
        }
    }

    public void Dispose()
    {
        disposed = true;
        ProcessorChain.ChainChanged -= OnChainChanged;
        ProcessorChain.PropertyChanged -= OnBoardPropertyChanged;
        StopSession();
        GC.SuppressFinalize(this);
    }
}
