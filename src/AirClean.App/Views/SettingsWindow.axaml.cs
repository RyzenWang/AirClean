using AirClean.App.Services;
using AirClean.App.ViewModels;
using AirClean.Core.Models;
using AirClean.Platform.Autostart;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AirClean.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ThemeService _theme;
    private readonly LocalizationService _localization;
    private readonly Action _persist;

    /// <summary>Parameterless constructor for the XAML loader; the app uses the other one.</summary>
    public SettingsWindow()
        : this(
            new AppSettings(),
            new ThemeService(new AppSettings(), () => { }),
            new LocalizationService(),
            () => { })
    {
    }

    public SettingsWindow(
        AppSettings settings,
        ThemeService theme,
        LocalizationService localization,
        Action persist)
    {
        _theme = theme;
        _localization = localization;
        _persist = persist;

        InitializeComponent();
        DataContext = new SettingsViewModel(settings);
    }

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = _localization.T("L.AddFolder")
        });

        if (folders.Count > 0)
        {
            viewModel.AddExclusion(folders[0].Path.LocalPath);
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        viewModel.Apply();

        if (!AutostartService.TrySet(viewModel.RunAtLogin, out var error))
        {
            viewModel.ErrorMessage = _localization.Format(
                "L.AutostartFailedFormat",
                error ?? "unknown error");
            return;
        }

        _theme.Set(viewModel.ThemeValue, persist: false);
        _localization.Apply(viewModel.LanguageValue);
        _persist();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
