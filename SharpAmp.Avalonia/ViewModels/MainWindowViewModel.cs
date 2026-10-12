namespace SharpAmp.Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public Models.AudioSettings AudioSettings { get; } = new();
        public SettingsViewModel Settings { get; }

        public MainWindowViewModel()
        {
            Settings = new SettingsViewModel(new AudioSettingsViewModel(AudioSettings));
        }
    }
}
