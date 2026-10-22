using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpAmp.Avalonia.Models;
using SharpAmp.Avalonia.Services;

namespace SharpAmp.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ApplicationSettingsStore store;

    public AudioSettingsViewModel Audio { get; }
    public ApplicationPreferences Preferences { get; }
    public ObservableCollection<SettingsPageViewModel> Pages { get; }
    [ObservableProperty] private SettingsPageViewModel? selectedPage;
    [ObservableProperty] private bool isAudioEditable = true;
    [ObservableProperty] private bool hasUnsavedChanges;
    [ObservableProperty] private bool hasPersistenceError;
    [ObservableProperty] private string persistenceMessage;

    public SettingsViewModel(AudioSettingsViewModel audioSettings,
        ApplicationPreferences? preferences = null,
        ApplicationSettingsStore? settingsStore = null,
        string? settingsLoadError = null)
    {
        Audio = audioSettings;
        Preferences = preferences ?? new ApplicationPreferences();
        store = settingsStore ?? new ApplicationSettingsStore();
        Pages = [audioSettings];
        SelectedPage = audioSettings;
        persistenceMessage = settingsLoadError ?? "Use Save settings to remember these choices on this device.";
        hasPersistenceError = settingsLoadError is not null;
        Audio.Settings.PropertyChanged += OnSettingChanged;
        Preferences.PropertyChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        HasUnsavedChanges = true;
        if (!HasPersistenceError) PersistenceMessage = "You have unsaved settings.";
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            store.Save(Audio.Settings, Preferences);
            HasUnsavedChanges = false;
            HasPersistenceError = false;
            PersistenceMessage = "Settings saved. They will be restored next time you open SharpAmp.";
        }
        catch (Exception error)
        {
            HasPersistenceError = true;
            PersistenceMessage = $"Settings could not be saved. Your previous settings file has been kept. {error.Message}";
        }
    }
}
