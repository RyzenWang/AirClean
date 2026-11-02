using System.Globalization;
using AirClean.App.Services;
using AirClean.App.ViewModels;
using AirClean.App.Views;
using AirClean.Core.Deleting;
using AirClean.Core.Models;
using AirClean.Core.Rules;
using AirClean.Core.Scanning;
using AirClean.Core.Services;
using AirClean.Platform;
using AirClean.Platform.Admin;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AirClean.App;

public partial class App : Application
{
    private SettingsService? _settingsService;
    private AppSettings? _settings;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _settingsService = new SettingsService();
            var isFirstRun = !File.Exists(_settingsService.SettingsPath);
            _settings = _settingsService.Load();

            if (isFirstRun && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                _settings.Language = LocalizationService.ChineseSimplified;
                _settingsService.Save(_settings);
            }

            var localization = new LocalizationService();
            localization.Apply(_settings.Language);

            var theme = new ThemeService(_settings, SaveSettings);

            var trash = PlatformServices.CreateTrashService();
            var reveal = PlatformServices.CreateRevealService();
            var adminHelper = new AdminHelperClient();
            var volumeScan = new VolumeScanService(RuleSet.ForCurrentPlatform(), trash, adminHelper);
            var deletion = new DeletionService(trash, adminCleanup: adminHelper);

            var viewModel = new MainWindowViewModel(
                _settings,
                localization,
                theme,
                SaveSettings,
                volumeScan,
                deletion,
                trash,
                reveal,
                adminHelper: adminHelper);

            var window = new MainWindow
            {
                DataContext = viewModel,
                Width = _settings.WindowWidth,
                Height = _settings.WindowHeight
            };

            if (_settings.WindowMaximized)
            {
                window.WindowState = WindowState.Maximized;
            }

            window.Closing += (_, _) => PersistWindow(window);
            desktop.MainWindow = window;

            // Installed builds check the release channel shortly after startup; source and
            // portable runs are no-ops because Velopack reports them as not installed.
            _ = Dispatcher.UIThread.InvokeAsync(
                () => viewModel.CheckForUpdatesCommand.Execute(null),
                DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void PersistWindow(Window window)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.WindowMaximized = window.WindowState == WindowState.Maximized;
        if (window.WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = window.Width;
            _settings.WindowHeight = window.Height;
        }

        _settingsService?.Save(_settings);
    }

    private void SaveSettings()
    {
        if (_settings is not null)
        {
            _settingsService?.Save(_settings);
        }
    }
}
