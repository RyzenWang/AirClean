using System.Collections.ObjectModel;
using System.Reflection;
using AirClean.App.Services;
using AirClean.Core.Deleting;
using AirClean.Core.Formatting;
using AirClean.Core.Models;
using AirClean.Core.Platform;
using AirClean.Core.Scanning;
using AirClean.Core.Trash;
using AirClean.Core.Volumes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Velopack;

namespace AirClean.App.ViewModels;

public enum AppStage
{
    Welcome,
    Scanning,
    Results,
    Completed
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly LocalizationService _localization;
    private readonly ThemeService _theme;
    private readonly Action _persist;
    private readonly VolumeScanService _volumeScan;
    private readonly DeletionService _deletion;
    private readonly ITrashService _trash;
    private readonly IAdminHelper? _adminHelper;
    private readonly long _largeFileThreshold;
    private readonly IReadOnlyList<string>? _duplicateRoots;

    private readonly UpdateService _updates = new();
    private UpdateInfo? _pendingUpdate;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _cleanCts;
    private AppStage _stage = AppStage.Welcome;
    private string? _statusKey;
    private string? _statusRaw;

    private VolumeItemViewModel? _selectedVolume;
    private string _diskLabel = string.Empty;
    private string _diskNumber = "0";
    private string _diskUnit = "B";
    private string _diskUsedValue = "0 B";
    private string _diskFreeValue = "0 B";
    private string _diskTotalValue = "0 B";
    private double _diskUsedRatio = 0.06;

    private long _selectedBytes;
    private int _selectedFiles;
    private long _totalBytes;
    private int _totalCount;
    private long _totalMatchedBytes;
    private int _totalMatchedCount;
    private string? _scannedRoot;
    private DeletionReport? _lastReport;

    public MainWindowViewModel(
        AppSettings settings,
        LocalizationService localization,
        ThemeService theme,
        Action persist,
        VolumeScanService volumeScan,
        DeletionService deletion,
        ITrashService trash,
        IRevealService reveal,
        long largeFileThreshold = LargeFileScanner.DefaultThresholdBytes,
        IReadOnlyList<VolumeInfo>? volumes = null,
        IReadOnlyList<string>? duplicateRoots = null,
        IAdminHelper? adminHelper = null)
    {
        _settings = settings;
        _localization = localization;
        _theme = theme;
        _persist = persist;
        _volumeScan = volumeScan;
        _deletion = deletion;
        _trash = trash;
        _adminHelper = adminHelper;
        _largeFileThreshold = largeFileThreshold;
        _duplicateRoots = duplicateRoots;

        if (_adminHelper is not null)
        {
            _adminHelper.Enabled = _settings.UseAdminHelper;
        }

        foreach (var volume in volumes ?? VolumeService.List())
        {
            Volumes.Add(new VolumeItemViewModel(volume, localization, ChooseVolume));
        }

        _selectedVolume = Volumes.FirstOrDefault(volume => volume.IsSystem) ?? Volumes.FirstOrDefault();
        SyncVolumeSelection();

        foreach (var category in Enum.GetValues<CleanCategory>())
        {
            var viewModel = new CategoryViewModel(category, localization, reveal.Reveal, reveal.IsSupported);
            viewModel.SelectionChanged += (_, _) => RecomputeSelection();
            Categories.Add(viewModel);
        }

        RefreshDisk();
        RefreshCategoryTexts();

        _localization.LanguageChanged += (_, _) =>
        {
            RefreshCategoryTexts();
            RecomputeSelection();
            RefreshCompletionTexts();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(FoundSummary));
            OnPropertyChanged(nameof(ScanFilesText));
            OnPropertyChanged(nameof(LanguageShortLabel));
            OnPropertyChanged(nameof(DiskName));
            OnPropertyChanged(nameof(DiskSubtitle));
            OnPropertyChanged(nameof(DiskUsedSummary));
            OnPropertyChanged(nameof(DiskFreeSummary));
            OnPropertyChanged(nameof(DiskTotalSummary));
            OnPropertyChanged(nameof(ScanScopeText));
            OnPropertyChanged(nameof(WelcomeScanHint));

            foreach (var volume in Volumes)
            {
                volume.RefreshLocalization(_localization);
            }
        };

        _theme.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        };
    }

    public ObservableCollection<CategoryViewModel> Categories { get; } = [];

    public ObservableCollection<CategoryViewModel> ConfirmItems { get; } = [];

    public ObservableCollection<CategoryViewModel> CompletionItems { get; } = [];

    public ObservableCollection<VolumeItemViewModel> Volumes { get; } = [];

    public ObservableCollection<string> VolumeList { get; } = [];

    public VolumeItemViewModel? SelectedVolume
    {
        get => _selectedVolume;
        private set
        {
            if (SetProperty(ref _selectedVolume, value))
            {
                SyncVolumeSelection();
                OnPropertyChanged(nameof(IsVolumeMode));
                OnPropertyChanged(nameof(ScanScopeText));
                OnPropertyChanged(nameof(DiskName));
                OnPropertyChanged(nameof(WelcomeScanHint));
            }
        }
    }

    /// <summary>One line under the drive chips on the welcome page.</summary>
    public string WelcomeScanHint => SelectedVolume is { } volume
        ? _localization.Format(
            volume.IsSystem ? "L.WelcomeScopeSystem" : "L.WelcomeScopeVolume",
            volume.Name,
            volume.FreeText)
        : string.Empty;

    /// <summary>Chip click: change the target without starting a scan.</summary>
    private void ChooseVolume(VolumeItemViewModel volume)
    {
        if (Stage == AppStage.Scanning || ReferenceEquals(volume, SelectedVolume))
        {
            return;
        }

        SelectedVolume = volume;
        RefreshDisk();
    }

    private void SyncVolumeSelection()
    {
        foreach (var volume in Volumes)
        {
            volume.IsSelected = ReferenceEquals(volume, _selectedVolume);
        }
    }

    public bool IsVolumeMode => SelectedVolume is { IsSystem: false };

    public bool CanSwitchVolume => Stage != AppStage.Scanning;

    public AppStage Stage
    {
        get => _stage;
        private set
        {
            if (!SetProperty(ref _stage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsWelcomeVisible));
            OnPropertyChanged(nameof(IsScanningVisible));
            OnPropertyChanged(nameof(IsResultsVisible));
            OnPropertyChanged(nameof(IsCompletedVisible));
            OnPropertyChanged(nameof(CanSwitchVolume));
        }
    }

    public bool IsWelcomeVisible => Stage == AppStage.Welcome;

    public bool IsScanningVisible => Stage == AppStage.Scanning;

    public bool IsResultsVisible => Stage == AppStage.Results;

    public bool IsCompletedVisible => Stage == AppStage.Completed;

    /// <summary>True once every stage has reported (or the scan was cancelled / failed).</summary>
    public bool IsScanComplete { get; private set; }

    public string LanguageShortLabel => _localization.LanguageShortLabel;

    public bool IsAdminHelperAvailable => _adminHelper?.IsAvailable == true;

    public bool UseAdminHelper
    {
        get => _adminHelper?.Enabled == true;
        set
        {
            if (_adminHelper is null || _adminHelper.Enabled == value)
            {
                return;
            }

            _adminHelper.Enabled = value;
            _settings.UseAdminHelper = value;
            _persist();
            OnPropertyChanged();
        }
    }

    public bool IsLightTheme => !_theme.IsDark;

    public bool IsDarkTheme => _theme.IsDark;

    public string VersionText => "AirClean " + AppVersion;

    /// <summary>The version stamped by the build, so a release build reports itself.</summary>
    public static string AppVersion { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var informational = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // SourceLink appends "+<commit>"; keep just the version.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    }

    // ---- updates ----

    public string UpdateText { get; private set; } = string.Empty;

    public bool HasUpdate => _pendingUpdate is not null;

    public bool IsCheckingUpdate { get; private set; }

    public string StatusText => _statusRaw
        ?? (_statusKey is null
            ? _localization.T("L.Ready") + " · " + _localization.T("L.LocalOnly")
            : _localization.T(_statusKey));

    // ---- disk ----

    public string DiskName => string.IsNullOrWhiteSpace(_diskLabel)
        ? _localization.T(IsVolumeMode ? "L.LocalVolume" : "L.SystemVolume")
        : _diskLabel;

    public string DiskNumber => _diskNumber;

    public string DiskUnit => _diskUnit;

    public string DiskUsedValue => _diskUsedValue;

    public string DiskFreeValue => _diskFreeValue;

    public string DiskTotalValue => _diskTotalValue;

    public double DiskUsedRatio => _diskUsedRatio;

    public double DiskUsedPercent => Math.Round(_diskUsedRatio * 100, 1);

    public string DiskSubtitle => _localization.Format("L.UsedOfFormat", _diskTotalValue);

    public string DiskUsedSummary => _localization.T("L.DiskUsed") + " " + _diskUsedValue;

    public string DiskFreeSummary => _localization.T("L.DiskFree") + " " + _diskFreeValue;

    public string DiskTotalSummary => _localization.T("L.DiskTotal") + " " + _diskTotalValue;

    public string ScanScopeText => _localization.T(IsVolumeMode ? "L.ScanScopeVolume" : "L.ScanScope");

    // ---- scan ----

    public string ScanFoundText { get; private set; } = "0 B";

    public string ScanTitleText { get; private set; } = string.Empty;

    public string ScanFilesText { get; private set; } = string.Empty;

    public string ScanTargetText { get; private set; } = string.Empty;

    public string ReclaimableNumber { get; private set; } = "0";

    public string ReclaimableUnit { get; private set; } = "B";

    public string FoundSummary => _totalMatchedCount == 0
        ? string.Empty
        : IsVolumeMode
            ? _localization.Format(
                "L.FoundVolumeFormat",
                ByteSize.Format(_totalMatchedBytes),
                _totalCount,
                _totalMatchedCount)
            : _localization.Format("L.FoundSummaryFormat", ByteSize.Format(_totalBytes), _totalCount);

    public string ScanElapsedText { get; private set; } = string.Empty;

    public bool HasLimited { get; private set; }

    // ---- selection ----

    public string SelectionSummary => _localization.Format(
        "L.SelectionSummaryFormat",
        _selectedFiles,
        ByteSize.Format(_selectedBytes));

    public string CleanButtonText => _selectedBytes > 0
        ? _localization.Format("L.CleanSelectedFormat", ByteSize.Format(_selectedBytes))
        : _localization.T("L.CleanSelected");

    public bool CanClean => _selectedBytes > 0 && !IsConfirmVisible && !IsDeleting;

    // ---- confirm ----

    public string ConfirmTitle { get; private set; } = string.Empty;

    public string ConfirmTrashNote { get; private set; } = string.Empty;

    public string ConfirmPermanentNote { get; private set; } = string.Empty;

    public bool HasConfirmTrashNote { get; private set; }

    public bool HasConfirmPermanentNote { get; private set; }

    private bool _usePermanent;

    public bool UsePermanent
    {
        get => _usePermanent;
        set => SetProperty(ref _usePermanent, value);
    }

    public bool IsConfirmVisible { get; private set; }

    public bool IsDeleting { get; private set; }

    public bool CanConfirm => !IsDeleting;

    public bool CanCancelClean => IsDeleting;

    /// <summary>0–100 for the cleaning progress bar.</summary>
    public double CleanPercent { get; private set; }

    public string CleanPercentText { get; private set; } = string.Empty;

    public string CleanCurrentFile { get; private set; } = string.Empty;

    public bool HasCleanCurrentFile => !string.IsNullOrEmpty(CleanCurrentFile);

    public string DeleteProgressText { get; private set; } = string.Empty;

    // ---- completion ----

    public string CompletedTitle { get; private set; } = string.Empty;

    public string CompletedSubTrash { get; private set; } = string.Empty;

    public string CompletedSubFreed { get; private set; } = string.Empty;

    public string CompletedNote { get; private set; } = string.Empty;

    public bool HasTrashNote { get; private set; }

    public string SkippedText { get; private set; } = string.Empty;

    public string SkippedDetail { get; private set; } = string.Empty;

    public bool HasSkipped { get; private set; }

    public bool ShowSubSeparator => HasTrashNote && HasFreedNote;

    public bool HasFreedNote { get; private set; }

    public bool CanOpenTrash => _trash.IsSupported && _lastReport is { TrashedBytes: > 0 };

    // ---- commands ----

    [RelayCommand]
    private Task ScanAsync() => RunScanAsync();

    [RelayCommand]
    private Task RescanAsync() => RunScanAsync();

    private async Task RunScanAsync()
    {
        if (Stage == AppStage.Scanning || SelectedVolume is not { } volume)
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        foreach (var category in Categories)
        {
            category.Reset();
            category.IsApplicable = false;
            category.IsScanning = false;
        }

        MarkPending(volume);
        RecomputeSelection();
        ScanFoundText = ByteSize.Format(0);
        ScanFilesText = _localization.Format("L.ScanFilesFormat", 0);
        ScanTargetText = string.Empty;
        ScanTitleText = _localization.T("L.Scanning");
        HasLimited = false;
        IsScanComplete = false;
        Stage = AppStage.Scanning;
        SetStatus("L.Scanning");

        var request = new VolumeScanRequest(
            volume.Info,
            volume.IsSystem ? ContentRoots() : [volume.Root],
            IncludeCaches: volume.IsSystem,
            IncludeLargeFiles: true,
            IncludeDuplicates: true,
            _largeFileThreshold,
            // A whole data drive can hold terabytes of candidates; cap the hashing so the
            // scan stays usable, and say so when the cap is reached.
            volume.IsSystem ? DuplicateScanner.DefaultHashBudgetBytes : 2L * 1024 * 1024 * 1024);

        var progress = new Progress<VolumeScanProgress>(OnVolumeProgress);
        var updates = new Progress<ScanUpdate>(OnScanUpdate);

        try
        {
            var result = await _volumeScan.ScanAsync(request, progress, updates, _cts.Token);
            Finish(result, volume);
        }
        catch (OperationCanceledException)
        {
            Stage = AppStage.Welcome;
            SetStatus(null);
        }
        catch (Exception exception)
        {
            Stage = AppStage.Welcome;
            SetStatusRaw(_localization.Format("L.ScanFailedFormat", exception.Message));
        }
        finally
        {
            IsScanComplete = true;
            OnPropertyChanged(nameof(IsScanComplete));
        }
    }

    /// <summary>Says which categories this volume will produce before anything is read.</summary>
    private void MarkPending(VolumeItemViewModel volume)
    {
        foreach (var category in Categories)
        {
            category.IsApplicable = category.Category == CleanCategory.SystemUpdates
                ? volume.IsSystem && IsAdminHelperAvailable && UseAdminHelper
                : volume.IsSystem
                    ? category.Category != CleanCategory.LargeFiles
                    : category.Category is CleanCategory.LargeFiles or CleanCategory.Trash or CleanCategory.Duplicates;

            category.IsScanning = category.IsApplicable;
        }
    }

    private void OnScanUpdate(ScanUpdate update)
    {
        var category = Categories.FirstOrDefault(item => update.Category is not null && item.Category == update.Category.Category);

        if (category is null)
        {
            return;
        }

        category.IsScanning = false;

        if (update.Category!.Category == CleanCategory.Duplicates && update.Groups is { Count: > 0 } groups)
        {
            category.ApplyDuplicates(update.Category, groups);
        }
        else
        {
            category.Apply(update.Category);
        }

        // Show results as soon as the first category lands.
        Stage = AppStage.Results;
        RecomputeSelection();
        RefreshTotals();
    }

    /// <summary>
    /// Checks the GitHub release channel. Silent unless the app was installed with the
    /// Setup or pkg; portable and source builds never self-update.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdate || _pendingUpdate is not null || !_updates.IsInstalled)
        {
            return;
        }

        IsCheckingUpdate = true;
        OnPropertyChanged(nameof(IsCheckingUpdate));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));

            var update = await _updates.CheckForUpdatesAsync();
            if (update is null)
            {
                return;
            }

            await _updates.DownloadAsync(update, _ => { }, CancellationToken.None);

            _pendingUpdate = update;
            UpdateText = _localization.Format("L.RestartToUpdateFormat", update.TargetFullRelease.Version);
            OnPropertyChanged(nameof(UpdateText));
            OnPropertyChanged(nameof(HasUpdate));
        }
        catch
        {
            // A failed check must never disturb reading or cleaning.
        }
        finally
        {
            IsCheckingUpdate = false;
            OnPropertyChanged(nameof(IsCheckingUpdate));
        }
    }

    [RelayCommand]
    private void ApplyUpdate()
    {
        if (_pendingUpdate is { } update)
        {
            _updates.ApplyAndRestart(update);
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private async Task SelectVolumeAsync(VolumeItemViewModel? volume)
    {
        if (volume is null || Stage == AppStage.Scanning)
        {
            return;
        }

        if (string.Equals(volume.Root, _scannedRoot, StringComparison.OrdinalIgnoreCase) &&
            Stage == AppStage.Results)
        {
            return;
        }

        SelectedVolume = volume;
        RefreshDisk();
        await RunScanAsync();
    }

    [RelayCommand]
    private void OpenConfirm()
    {
        if (CanClean)
        {
            BuildConfirm();
        }
    }

    [RelayCommand]
    private void CancelConfirm()
    {
        IsConfirmVisible = false;
        UsePermanent = false;
        OnPropertyChanged(nameof(IsConfirmVisible));
        OnPropertyChanged(nameof(CanClean));
    }

    [RelayCommand]
    private async Task ConfirmCleanAsync()
    {
        if (IsDeleting)
        {
            return;
        }

        var selected = Categories
            .Where(category => category.SelectedFileCount > 0)
            .Select(category => category.BuildScan())
            .Where(scan => scan is not null)
            .Select(scan => scan!)
            .ToList();

        if (selected.Count == 0)
        {
            return;
        }

        _cleanCts?.Dispose();
        _cleanCts = new CancellationTokenSource();

        IsDeleting = true;
        CleanPercent = 0;
        CleanPercentText = _localization.Format("L.CleanPercentFormat", 0);
        CleanCurrentFile = string.Empty;
        DeleteProgressText = _localization.T("L.Deleting");
        SetStatus("L.Deleting");
        OnPropertyChanged(nameof(IsDeleting));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanCancelClean));
        OnPropertyChanged(nameof(CanClean));

        var categories = selected;
        var mode = UsePermanent ? DeleteMode.Permanent : DeleteMode.Trash;
        var total = categories.Where(category => category.Category != CleanCategory.Trash).Sum(category => category.Items.Count);

        try
        {
            var progress = new Progress<DeletionProgress>(item =>
            {
                var percent = item.Total <= 0 ? 100 : Math.Clamp(item.Done * 100 / item.Total, 0, 100);
                CleanPercent = percent;
                CleanPercentText = _localization.Format("L.CleanPercentFormat", percent);
                CleanCurrentFile = item.CurrentPath ?? string.Empty;
                DeleteProgressText = _localization.Format("L.DeletingProgressFormat", item.Done, item.Total);
                OnPropertyChanged(nameof(CleanPercent));
                OnPropertyChanged(nameof(CleanPercentText));
                OnPropertyChanged(nameof(CleanCurrentFile));
                OnPropertyChanged(nameof(HasCleanCurrentFile));
                OnPropertyChanged(nameof(DeleteProgressText));
            });

            var report = await Task.Run(
                () => _deletion.Delete(categories, mode, progress, _cleanCts.Token));

            _lastReport = report;
            BuildCompletion(categories, report);
            Stage = AppStage.Completed;
            SetStatus(report.Canceled ? "L.CleanCanceled" : "L.CleanComplete");
        }
        catch (OperationCanceledException)
        {
            SetStatus("L.CleanCanceled");
            Stage = AppStage.Results;
        }
        catch (Exception exception)
        {
            SetStatusRaw(_localization.Format("L.CleanFailedFormat", exception.Message));
            Stage = AppStage.Results;
        }
        finally
        {
            IsDeleting = false;
            OnPropertyChanged(nameof(IsDeleting));
            OnPropertyChanged(nameof(CanConfirm));
            OnPropertyChanged(nameof(CanCancelClean));
            IsConfirmVisible = false;
            UsePermanent = false;
            OnPropertyChanged(nameof(IsConfirmVisible));
            OnPropertyChanged(nameof(CanClean));
            CleanPercent = 0;
            CleanCurrentFile = string.Empty;
            OnPropertyChanged(nameof(CleanPercent));
            OnPropertyChanged(nameof(CleanCurrentFile));
            OnPropertyChanged(nameof(HasCleanCurrentFile));
            RefreshDisk();
        }
    }

    [RelayCommand]
    private void CancelClean() => _cleanCts?.Cancel();

    [RelayCommand]
    private void Done()
    {
        _lastReport = null;
        CompletionItems.Clear();
        Stage = AppStage.Welcome;
        SetStatus(null);
    }

    [RelayCommand]
    private void OpenTrash() => _trash.OpenTrash();

    [RelayCommand]
    private void Escape()
    {
        if (IsConfirmVisible && !IsDeleting)
        {
            CancelConfirm();
            return;
        }

        if (Stage == AppStage.Scanning)
        {
            CancelScan();
        }
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        var next = _localization.IsChinese
            ? LocalizationService.English
            : LocalizationService.ChineseSimplified;

        SetLanguage(next);
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.Cycle();

    [RelayCommand]
    private void SetTheme(string? mode)
    {
        if (!string.IsNullOrWhiteSpace(mode))
        {
            _theme.Set(mode, persist: true);
        }
    }

    [RelayCommand]
    private void SetLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        _settings.Language = language;
        _localization.Apply(language);
        _persist();
    }

    // ---- helpers ----

    private void OnVolumeProgress(VolumeScanProgress progress)
    {
        ScanFoundText = progress.Stage switch
        {
            ScanStage.Duplicates => ByteSize.Format(progress.BytesSeen),
            _ => ByteSize.Format(progress.BytesSeen)
        };

        ScanFilesText = progress.Stage switch
        {
            ScanStage.Duplicates => _localization.Format("L.ScanDuplicatesFormat", progress.FilesSeen, progress.ItemsFound),
            ScanStage.LargeFiles => _localization.Format("L.ScanLargeFormat", progress.FilesSeen, progress.ItemsFound),
            _ => _localization.Format("L.ScanFilesFormat", progress.FilesSeen)
        };

        ScanTargetText = progress.Current ?? string.Empty;
        ScanTitleText = _localization.T(progress.Stage switch
        {
            ScanStage.Duplicates => "L.ScanningDuplicates",
            ScanStage.LargeFiles => "L.ScanningLarge",
            _ => "L.Scanning"
        });

        OnPropertyChanged(nameof(ScanFoundText));
        OnPropertyChanged(nameof(ScanFilesText));
        OnPropertyChanged(nameof(ScanTargetText));
        OnPropertyChanged(nameof(ScanTitleText));
    }

    private IReadOnlyList<string> ContentRoots() => _duplicateRoots ?? DefaultContentRoots();

    private static IReadOnlyList<string> DefaultContentRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(home, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        return folders
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Final pass. The result is authoritative: staged updates are only there to fill the
    /// window early, and a late one must never leave a row stuck on "waiting".
    /// </summary>
    private void Finish(ScanResult result, VolumeItemViewModel volume)
    {
        foreach (var category in Categories)
        {
            category.IsScanning = false;

            var scan = result.Categories.FirstOrDefault(item => item.Category == category.Category);
            if (scan is null)
            {
                continue;
            }

            if (scan.Category == CleanCategory.Duplicates)
            {
                category.ApplyDuplicates(scan, result.DuplicateGroups ?? []);
            }
            else
            {
                category.Apply(scan);
            }

            category.IsApplicable = true;
        }

        foreach (var category in Categories.Where(category => category.FileCount == 0))
        {
            category.IsApplicable = false;
        }

        HasLimited = result.Limited || Categories.Any(category => category.HasLimited_Internal);
        OnPropertyChanged(nameof(HasLimited));

        _scannedRoot = volume.Root;
        ScanElapsedText = _localization.Format("L.ScanElapsedFormat", Math.Round(result.Elapsed.TotalSeconds, 1));
        OnPropertyChanged(nameof(ScanElapsedText));
        OnPropertyChanged(nameof(ScanScopeText));

        RefreshTotals();
        RefreshDisk();

        if (_totalCount == 0)
        {
            Stage = AppStage.Welcome;
            SetStatus("L.NothingFound");
            return;
        }

        Stage = AppStage.Results;
        SetStatus("L.ScanComplete");
    }

    private void RefreshTotals()
    {
        _totalBytes = Categories.Sum(category => category.Bytes);
        _totalCount = Categories.Sum(category => category.FileCount);
        _totalMatchedBytes = Categories.Sum(category => category.Scan?.MatchedBytes is > 0 ? category.Scan.MatchedBytes : category.Bytes);
        _totalMatchedCount = Categories.Sum(category => category.Scan?.MatchedCount is > 0 ? category.Scan.MatchedCount : category.FileCount);

        OnPropertyChanged(nameof(FoundSummary));
        RecomputeSelection();
    }

    private void RefreshCategoryTexts()
    {
        foreach (var category in Categories)
        {
            category.RefreshLocalization();
        }
    }

    private void RecomputeSelection()
    {
        var selected = Categories.Where(category => category.SelectedFileCount > 0).ToList();

        _selectedBytes = selected.Sum(category => category.SelectedBytes);
        _selectedFiles = selected.Sum(category => category.SelectedFileCount);

        var (number, unit) = ByteSize.Split(_selectedBytes);
        ReclaimableNumber = number;
        ReclaimableUnit = unit;

        OnPropertyChanged(nameof(ReclaimableNumber));
        OnPropertyChanged(nameof(ReclaimableUnit));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CleanButtonText));
        OnPropertyChanged(nameof(CanClean));
    }

    private void BuildConfirm()
    {
        ConfirmItems.Clear();

        foreach (var category in Categories.Where(category => category.SelectedFileCount > 0))
        {
            ConfirmItems.Add(category);
        }

        var trashed = ConfirmItems
            .Where(category => category.Category != CleanCategory.Trash && category.AdminRuleId is null)
            .Sum(category => category.SelectedBytes);
        var permanentTrash = ConfirmItems
            .Where(category => category.Category == CleanCategory.Trash)
            .Sum(category => category.SelectedBytes);
        var permanentAdmin = ConfirmItems
            .Where(category => category.AdminRuleId is not null)
            .Sum(category => category.SelectedBytes);
        var permanent = permanentTrash + permanentAdmin;

        ConfirmTitle = _localization.Format("L.ConfirmTitleFormat", ByteSize.Format(trashed + permanent));
        ConfirmTrashNote = trashed > 0
            ? _localization.Format("L.ConfirmNoteTrash", ByteSize.Format(trashed))
            : string.Empty;
        ConfirmPermanentNote = string.Join(
            " ",
            new[]
            {
                permanentTrash > 0
                    ? _localization.Format("L.ConfirmNotePermanentTrash", ByteSize.Format(permanentTrash))
                    : string.Empty,
                permanentAdmin > 0
                    ? _localization.Format("L.ConfirmNotePermanentAdmin", ByteSize.Format(permanentAdmin))
                    : string.Empty
            }.Where(note => note.Length > 0));
        HasConfirmTrashNote = trashed > 0;
        HasConfirmPermanentNote = permanent > 0;
        UsePermanent = false;

        OnPropertyChanged(nameof(ConfirmTitle));
        OnPropertyChanged(nameof(ConfirmTrashNote));
        OnPropertyChanged(nameof(ConfirmPermanentNote));
        OnPropertyChanged(nameof(HasConfirmTrashNote));
        OnPropertyChanged(nameof(HasConfirmPermanentNote));
        OnPropertyChanged(nameof(UsePermanent));

        IsConfirmVisible = true;
        OnPropertyChanged(nameof(IsConfirmVisible));
        OnPropertyChanged(nameof(CanClean));
    }

    private void BuildCompletion(IReadOnlyList<CategoryScan> categories, DeletionReport report)
    {
        CompletionItems.Clear();

        foreach (var category in Categories.Where(category => categories.Any(scan => scan.Category == category.Category)))
        {
            CompletionItems.Add(category);
        }

        RefreshCompletionTexts();
    }

    private void RefreshCompletionTexts()
    {
        if (_lastReport is not { } report)
        {
            return;
        }

        var total = report.TotalBytes;
        CompletedTitle = _localization.Format("L.DoneTitleFormat", ByteSize.Format(total));
        CompletedSubTrash = report.TrashedBytes > 0
            ? _localization.Format("L.DoneSubTrashFormat", ByteSize.Format(report.TrashedBytes))
            : string.Empty;
        CompletedSubFreed = report.FreedBytes > 0
            ? _localization.Format("L.DoneSubFreedFormat", ByteSize.Format(report.FreedBytes))
            : string.Empty;
        HasFreedNote = report.FreedBytes > 0;
        CompletedNote = report.TrashedBytes > 0
            ? _localization.Format("L.DoneNoteTrashFormat", ByteSize.Format(report.TrashedBytes))
            : string.Empty;
        HasTrashNote = report.TrashedBytes > 0;

        var skipped = report.Skipped.Values.Sum();
        HasSkipped = skipped > 0;
        SkippedText = _localization.Format("L.SkippedFormat", skipped);
        SkippedDetail = string.Join(" · ", new[]
        {
            Detail("L.SkippedInUse", SkipReason.InUse),
            Detail("L.SkippedPermission", SkipReason.Permission),
            Detail("L.SkippedChanged", SkipReason.ChangedSinceScan)
        }.Where(part => part.Length > 0));

        OnPropertyChanged(nameof(CompletedTitle));
        OnPropertyChanged(nameof(CompletedSubTrash));
        OnPropertyChanged(nameof(CompletedSubFreed));
        OnPropertyChanged(nameof(CompletedNote));
        OnPropertyChanged(nameof(HasTrashNote));
        OnPropertyChanged(nameof(HasFreedNote));
        OnPropertyChanged(nameof(ShowSubSeparator));
        OnPropertyChanged(nameof(SkippedText));
        OnPropertyChanged(nameof(SkippedDetail));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(CanOpenTrash));

        string Detail(string key, SkipReason reason)
        {
            if (_lastReport is not { } current || !current.Skipped.TryGetValue(reason, out var count))
            {
                return string.Empty;
            }

            return _localization.Format("L.SkippedItemFormat", _localization.T(key), count);
        }
    }

    private void RefreshDisk()
    {
        if (SelectedVolume is not { } volume)
        {
            return;
        }

        var info = volume.Info;
        var (number, unit) = ByteSize.Split(info.UsedBytes);
        _diskLabel = volume.Name;
        _diskNumber = number;
        _diskUnit = unit;
        _diskUsedValue = ByteSize.Format(info.UsedBytes);
        _diskFreeValue = ByteSize.Format(info.FreeBytes);
        _diskTotalValue = ByteSize.Format(info.TotalBytes);
        _diskUsedRatio = info.UsedRatio;

        OnPropertyChanged(nameof(DiskName));
        OnPropertyChanged(nameof(DiskNumber));
        OnPropertyChanged(nameof(DiskUnit));
        OnPropertyChanged(nameof(DiskUsedValue));
        OnPropertyChanged(nameof(DiskFreeValue));
        OnPropertyChanged(nameof(DiskTotalValue));
        OnPropertyChanged(nameof(DiskUsedRatio));
        OnPropertyChanged(nameof(DiskUsedPercent));
        OnPropertyChanged(nameof(DiskSubtitle));
        OnPropertyChanged(nameof(DiskUsedSummary));
        OnPropertyChanged(nameof(DiskFreeSummary));
        OnPropertyChanged(nameof(DiskTotalSummary));
    }

    private void SetStatus(string? key)
    {
        _statusKey = key;
        _statusRaw = null;
        OnPropertyChanged(nameof(StatusText));
    }

    private void SetStatusRaw(string text)
    {
        _statusRaw = text;
        _statusKey = null;
        OnPropertyChanged(nameof(StatusText));
    }
}
