using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SharpAmp.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public ObservableCollection<SettingsPageViewModel> Pages { get; }
    [ObservableProperty] private SettingsPageViewModel? selectedPage;

    public SettingsViewModel(AudioSettingsViewModel audioSettings)
    {
        Pages = [audioSettings];
        SelectedPage = audioSettings;
    }
}
