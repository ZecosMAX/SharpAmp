using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SharpAmp.Avalonia.Services;
using SharpAmp.Avalonia.ViewModels;
using SharpAmp.Avalonia.Views;

namespace SharpAmp.Avalonia
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var settingsStore = new ApplicationSettingsStore();
                var savedSettings = settingsStore.Load();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(savedSettings.AudioSettings,
                        applicationPreferences: savedSettings.Preferences,
                        settingsStore: settingsStore,
                        settingsLoadError: savedSettings.Error),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
