namespace SharpAmp.Avalonia.ViewModels;

public abstract class SettingsPageViewModel(string title) : ViewModelBase
{
    public string Title { get; } = title;
}
