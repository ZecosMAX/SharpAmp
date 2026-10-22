using CommunityToolkit.Mvvm.ComponentModel;

namespace SharpAmp.Avalonia.Models;

/// <summary>Preferences for editing the pedalboard; these do not change its sound.</summary>
public partial class ApplicationPreferences : ObservableObject
{
    [ObservableProperty] private bool showHints = true;
    [ObservableProperty] private bool snapToGrid = true;
}
