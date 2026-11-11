using System.Collections.ObjectModel;
using AirClean.Core.Models;
using AirClean.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirClean.App.ViewModels;

/// <summary>
/// Edits a draft copy of <see cref="AppSettings"/>. Nothing is written until
/// <see cref="Apply"/> runs, so Cancel really cancels.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _source;
    private int _themeIndex;
    private int _languageIndex;
    private string _scanMode;
    private decimal? _largeFileThresholdMb;
    private decimal? _largeFileLimit;
    private decimal? _duplicateMinSizeMb;
    private decimal? _duplicateGroupLimit;
    private decimal? _hashBudgetGb;
    private bool _checkForUpdatesOnStartup;
    private bool _closeToTray;
    private bool _minimizeToTray;
    private bool _reminderEnabled;
    private decimal? _reminderIntervalDays;
    private bool _runAtLogin;
    private bool _useAdminHelper;
    private string _errorMessage = string.Empty;

    public SettingsViewModel(AppSettings source)
    {
        _source = source;

        _themeIndex = source.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        _languageIndex = source.Language == "zh-CN" ? 1 : 0;
        _scanMode = source.ScanMode == "Full" ? "Full" : "Quick";
        _largeFileThresholdMb = source.LargeFileThresholdMb;
        _largeFileLimit = source.LargeFileLimit;
        _duplicateMinSizeMb = source.DuplicateMinSizeMb;
        _duplicateGroupLimit = source.DuplicateGroupLimit;
        _hashBudgetGb = source.HashBudgetGb;
        _checkForUpdatesOnStartup = source.CheckForUpdatesOnStartup;
        _closeToTray = source.CloseToTray;
        _minimizeToTray = source.MinimizeToTray;
        _reminderEnabled = source.ReminderEnabled;
        _reminderIntervalDays = source.ReminderIntervalDays;
        _runAtLogin = source.RunAtLogin;
        _useAdminHelper = source.UseAdminHelper;

        foreach (var path in source.Exclusions)
        {
            Exclusions.Add(new SettingsExclusionViewModel(path, RemoveExclusion));
        }
    }

    public ObservableCollection<SettingsExclusionViewModel> Exclusions { get; } = [];

    public bool HasExclusions => Exclusions.Count > 0;

    public int ThemeIndex
    {
        get => _themeIndex;
        set => SetProperty(ref _themeIndex, value);
    }

    public int LanguageIndex
    {
        get => _languageIndex;
        set => SetProperty(ref _languageIndex, value);
    }

    public bool IsQuickScan
    {
        get => _scanMode == "Quick";
        set
        {
            if (value && _scanMode != "Quick")
            {
                _scanMode = "Quick";
                OnPropertyChanged(nameof(IsQuickScan));
                OnPropertyChanged(nameof(IsFullScan));
            }
        }
    }

    public bool IsFullScan
    {
        get => _scanMode == "Full";
        set
        {
            if (value && _scanMode != "Full")
            {
                _scanMode = "Full";
                OnPropertyChanged(nameof(IsQuickScan));
                OnPropertyChanged(nameof(IsFullScan));
            }
        }
    }

    public decimal? LargeFileThresholdMb
    {
        get => _largeFileThresholdMb;
        set => SetProperty(ref _largeFileThresholdMb, value);
    }

    public decimal? LargeFileLimit
    {
        get => _largeFileLimit;
        set => SetProperty(ref _largeFileLimit, value);
    }

    public decimal? DuplicateMinSizeMb
    {
        get => _duplicateMinSizeMb;
        set => SetProperty(ref _duplicateMinSizeMb, value);
    }

    public decimal? DuplicateGroupLimit
    {
        get => _duplicateGroupLimit;
        set => SetProperty(ref _duplicateGroupLimit, value);
    }

    public decimal? HashBudgetGb
    {
        get => _hashBudgetGb;
        set => SetProperty(ref _hashBudgetGb, value);
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set => SetProperty(ref _checkForUpdatesOnStartup, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool ReminderEnabled
    {
        get => _reminderEnabled;
        set => SetProperty(ref _reminderEnabled, value);
    }

    public decimal? ReminderIntervalDays
    {
        get => _reminderIntervalDays;
        set => SetProperty(ref _reminderIntervalDays, value);
    }

    public bool RunAtLogin
    {
        get => _runAtLogin;
        set => SetProperty(ref _runAtLogin, value);
    }

    public bool UseAdminHelper
    {
        get => _useAdminHelper;
        set => SetProperty(ref _useAdminHelper, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorMessage.Length > 0;

    public string ThemeValue => ThemeIndex switch
    {
        1 => "Light",
        2 => "Dark",
        _ => "System"
    };

    public string LanguageValue => LanguageIndex == 1 ? "zh-CN" : "en";

    public void AddExclusion(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0 ||
            Exclusions.Any(item => string.Equals(
                item.Path,
                trimmed,
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Exclusions.Add(new SettingsExclusionViewModel(trimmed, RemoveExclusion));
        OnPropertyChanged(nameof(HasExclusions));
    }

    /// <summary>Copies the draft into the live settings and normalises it.</summary>
    public void Apply()
    {
        _source.Theme = ThemeValue;
        _source.Language = LanguageValue;
        _source.ScanMode = IsFullScan ? "Full" : "Quick";
        _source.Exclusions = Exclusions.Select(item => item.Path).ToList();
        _source.LargeFileThresholdMb = ToInt(LargeFileThresholdMb, 100);
        _source.LargeFileLimit = ToInt(LargeFileLimit, 200);
        _source.DuplicateMinSizeMb = ToInt(DuplicateMinSizeMb, 1);
        _source.DuplicateGroupLimit = ToInt(DuplicateGroupLimit, 200);
        _source.HashBudgetGb = ToInt(HashBudgetGb, 8);
        _source.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;
        _source.CloseToTray = CloseToTray;
        _source.MinimizeToTray = MinimizeToTray;
        _source.ReminderEnabled = ReminderEnabled;
        _source.ReminderIntervalDays = ToInt(ReminderIntervalDays, 7);
        _source.RunAtLogin = RunAtLogin;
        _source.UseAdminHelper = UseAdminHelper;

        SettingsService.Normalize(_source);
    }

    private void RemoveExclusion(string path)
    {
        var item = Exclusions.FirstOrDefault(
            entry => string.Equals(entry.Path, path, StringComparison.Ordinal));
        if (item is not null)
        {
            Exclusions.Remove(item);
            OnPropertyChanged(nameof(HasExclusions));
        }
    }

    private static int ToInt(decimal? value, int fallback) =>
        value is { } number && number >= int.MinValue && number <= int.MaxValue
            ? (int)number
            : fallback;
}

public sealed partial class SettingsExclusionViewModel : ObservableObject
{
    private readonly Action<string> _remove;

    public SettingsExclusionViewModel(string path, Action<string> remove)
    {
        Path = path;
        _remove = remove;
    }

    public string Path { get; }

    [RelayCommand]
    private void Remove() => _remove(Path);
}
