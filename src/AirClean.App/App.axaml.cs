using System.Globalization;
using AirClean.App.Services;
using AirClean.App.ViewModels;
using AirClean.App.Views;
using AirClean.Core.Deleting;
using AirClean.Core.Models;
using AirClean.Core.Rules;
using AirClean.Core.Scanning;
using AirClean.Core.Security;
using AirClean.Core.Services;
using AirClean.Platform;
using AirClean.Platform.Admin;
using AirClean.Platform.Autostart;
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
    private TrayIconService? _tray;
    private ReminderService? _reminders;
    private bool _exiting;

    /// <summary>Set by <see cref="Program"/> once the single-instance lock is held.</summary>
    internal static SingleInstance? SharedInstance { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += (_, args) =>
                CrashLog.Write("Dispatcher.UnhandledException", args.Exception);

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

            if (_settings.RunAtLogin)
            {
                // Repair a stale path after an update or a moved portable folder.
                AutostartService.TrySet(true, out _);
            }

            var trash = PlatformServices.CreateTrashService();
            var reveal = PlatformServices.CreateRevealService();
            var adminHelper = new AdminHelperClient();
            var exclusions = new ExclusionSet(_settings.Exclusions);
            var volumeScan = new VolumeScanService(RuleSet.ForCurrentPlatform(), trash, adminHelper, exclusions);
            var journal = new JournalStore();
            var deletion = new DeletionService(trash, journal, adminHelper, exclusions: exclusions);

            var viewModel = new MainWindowViewModel(
                _settings,
                localization,
                theme,
                SaveSettings,
                volumeScan,
                deletion,
                trash,
                reveal,
                adminHelper: adminHelper,
                reportExporter: new FileReportExporter(),
                exclusions: exclusions);

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

            window.Closing += (_, args) =>
            {
                if (!_exiting && _settings.CloseToTray)
                {
                    args.Cancel = true;
                    window.Hide();
                    return;
                }

                PersistWindow(window);
            };

            window.PropertyChanged += (_, args) =>
            {
                if (args.Property == Window.WindowStateProperty &&
                    window.WindowState == WindowState.Minimized &&
                    _settings.MinimizeToTray)
                {
                    window.Hide();
                }
            };

            desktop.MainWindow = window;

            void ShowMainWindow()
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }

                    window.Show();
                    window.Activate();
                });
            }

            void QuickScan()
            {
                ShowMainWindow();
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (viewModel.Stage is AppStage.Welcome or AppStage.Results)
                        {
                            viewModel.ScanCommand.Execute(null);
                        }
                    },
                    DispatcherPriority.Background);
            }

            void ExitApplication()
            {
                _exiting = true;
                _tray?.Dispose();
                _tray = null;
                desktop.Shutdown();
            }

            _tray = TrayIconService.TryCreate(localization, ShowMainWindow, QuickScan, ExitApplication);

            if (_tray is not null)
            {
                localization.LanguageChanged += (_, _) => _tray?.RefreshLanguage(localization);
            }

            _reminders = new ReminderService(
                _settings,
                localization,
                message =>
                {
                    viewModel.ShowReminder(message);
                    _tray?.ShowReminder(message);
                },
                SaveSettings);
            _reminders.Start();

            desktop.Exit += (_, _) =>
            {
                _reminders?.Dispose();
                _reminders = null;
                _tray?.Dispose();
                _tray = null;
            };

            if (SharedInstance is { } instance)
            {
                instance.Activated += (_, _) => ShowMainWindow();
            }

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
