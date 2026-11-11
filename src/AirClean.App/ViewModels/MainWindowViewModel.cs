using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using AirClean.App.Services;
using Avalonia.Threading;
using AirClean.Core.Deleting;
using AirClean.Core.Formatting;
using AirClean.Core.Models;
using AirClean.Core.Platform;
using AirClean.Core.Scanning;
using AirClean.Core.Security;
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
    private readonly IVolumeScanService _volumeScan;
    private readonly DeletionService _deletion;
    private readonly JournalStore _journal;
    private readonly ITrashService _trash;
    private readonly IAdminHelper? _adminHelper;
    private readonly IReportExporter? _reportExporter;
    private readonly ExclusionSet _exclusions;
    private readonly long _largeFileThreshold;
    private readonly IReadOnlyList<string>? _duplicateRoots;

    private readonly UpdateService _updates = new();
    private UpdateInfo? _pendingUpdate;

    private readonly DispatcherTimer _scanTimer;
    private readonly Stopwatch _scanClock = new();

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
    private bool _limitedByGroupCap;
    private int _duplicateGroupCount;
    private string? _scannedRoot;
    private bool _wasScanCanceled;
    private DeletionReport? _lastReport;
    private JournalBatch? _lastBatch;
    private bool _isUndoing;
    private string _undoMessage = string.Empty;
    private int _undoRestoredCount;
    private int _undoFailedCount;
    private long _undoRestoredBytes;
    private string? _undoError;
    private bool _isEmptyTrashConfirmVisible;
    private bool _isEmptyingTrash;
    private bool _isExportingSkip;

    public MainWindowViewModel(
        AppSettings settings,
        LocalizationService localization,
        ThemeService theme,
        Action persist,
        IVolumeScanService volumeScan,
        DeletionService deletion,
        ITrashService trash,
        IRevealService reveal,
        long largeFileThreshold = 0,
        IReadOnlyList<VolumeInfo>? volumes = null,
        IReadOnlyList<string>? duplicateRoots = null,
        IAdminHelper? adminHelper = null,
        IReportExporter? reportExporter = null,
        ExclusionSet? exclusions = null)
    {
        _settings = settings;
        _localization = localization;
        _theme = theme;
        _persist = persist;
        _volumeScan = volumeScan;
        _deletion = deletion;
        _journal = deletion.Journal;
        _trash = trash;
        _adminHelper = adminHelper;
        _reportExporter = reportExporter;
        _exclusions = exclusions ?? new ExclusionSet(_settings.Exclusions);
        _largeFileThreshold = largeFileThreshold;
        _duplicateRoots = duplicateRoots;

        _scanTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _scanTimer.Tick += (_, _) => UpdateScanElapsed();

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
        RefreshRecentCleanups();

        _localization.LanguageChanged += (_, _) =>
        {
            RefreshCategoryTexts();
            RecomputeSelection();
            RefreshCompletionTexts();
            RefreshRecentCleanups();
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
            OnPropertyChanged(nameof(UndoButtonText));
            OnPropertyChanged(nameof(EmptyTrashButtonText));
            OnPropertyChanged(nameof(LimitedNote));
            OnPropertyChanged(nameof(ExportSkipText));
            RefreshUndoMessage();

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

    /// <summary>Raised when the gear button asks the window to open settings.</summary>
    public event EventHandler? SettingsRequested;

    internal AppSettings Settings => _settings;

    internal LocalizationService Localization => _localization;

    internal ThemeService Theme => _theme;

    internal Action Persist => _persist;

    public ObservableCollection<RecentCleanupViewModel> RecentCleanups { get; } = [];

    public bool HasRecentCleanups => RecentCleanups.Count > 0;

    public bool HasNoRecentCleanups => !HasRecentCleanups;

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
            volume.IsSystem
                ? (IsQuickScan ? "L.WelcomeScopeQuick" : "L.WelcomeScopeSystem")
                : (IsQuickScan ? "L.WelcomeScopeVolumeQuick" : "L.WelcomeScopeVolume"),
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

    /// <summary>True when the configured scan is the fast cache/log/trash pass.</summary>
    public bool IsQuickScan => string.Equals(_settings.ScanMode, "Quick", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>True when a cancelled scan stopped on the results page with partial data.</summary>
    public bool WasScanCanceled
    {
        get => _wasScanCanceled;
        private set => SetProperty(ref _wasScanCanceled, value);
    }

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

    public string ScanScopeText => IsVolumeMode
        ? _localization.T("L.ScanScopeVolume")
        : _localization.T(IsQuickScan ? "L.ScanScopeQuick" : "L.ScanScopeFull");

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

    public string LimitedNote
    {
        get
        {
            if (_limitedByGroupCap && _duplicateGroupCount > DuplicateScanner.MaxGroups)
            {
                return _localization.Format(
                    "L.LimitedGroupsNoteFormat",
                    DuplicateScanner.MaxGroups,
                    _duplicateGroupCount - DuplicateScanner.MaxGroups);
            }

            return _localization.T("L.LimitedNote");
        }
    }

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

    public bool HasSkipped { get; private set; }

    public bool HasAbortNote => _lastReport is { Aborted: true };

    public string AbortNote => _lastReport is { DiskFull: true }
        ? _localization.T("L.AbortDiskFull")
        : _localization.T("L.AbortFailureRate");

    /// <summary>Hidden once the whole batch has been restored; the category sizes would lie.</summary>
    public bool HasCleanupDetails =>
        !(_undoRestoredCount > 0 && _lastReport is { TrashedBytes: 0, FreedBytes: 0 });

    public bool ShowSubSeparator => HasTrashNote && HasFreedNote;

    public bool HasFreedNote { get; private set; }

    public bool CanOpenTrash => _trash.IsSupported && _lastReport is { TrashedBytes: > 0 };

    public bool CanUndo => !IsUndoing &&
                           _trash.IsSupported &&
                           _lastReport is { TrashedBytes: > 0 } &&
                           _lastBatch is { } batch &&
                           batch.CanUndo;

    public bool IsUndoVisible => (CanUndo || IsUndoing) && _lastReport is not null;

    public bool IsUndoing
    {
        get => _isUndoing;
        private set
        {
            if (SetProperty(ref _isUndoing, value))
            {
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(IsUndoVisible));
                OnPropertyChanged(nameof(UndoButtonText));
            }
        }
    }

    public string UndoButtonText => IsUndoing
        ? _localization.T("L.Undoing")
        : _localization.T("L.UndoLastClean");

    public string UndoMessage
    {
        get => _undoMessage;
        private set
        {
            if (SetProperty(ref _undoMessage, value))
            {
                OnPropertyChanged(nameof(HasUndoMessage));
            }
        }
    }

    public bool HasUndoMessage => _undoMessage.Length > 0;

    public bool CanEmptyTrash => !IsEmptyingTrash &&
                                 _trash.IsSupported &&
                                 _lastReport is { TrashedBytes: > 0 };

    public bool IsEmptyTrashVisible => _trash.IsSupported && _lastReport is { TrashedBytes: > 0 };

    public bool IsEmptyTrashConfirmVisible
    {
        get => _isEmptyTrashConfirmVisible;
        private set
        {
            if (SetProperty(ref _isEmptyTrashConfirmVisible, value))
            {
                OnPropertyChanged(nameof(CanEmptyTrash));
            }
        }
    }

    public bool IsEmptyingTrash
    {
        get => _isEmptyingTrash;
        private set
        {
            if (SetProperty(ref _isEmptyingTrash, value))
            {
                OnPropertyChanged(nameof(CanEmptyTrash));
                OnPropertyChanged(nameof(IsEmptyTrashVisible));
                OnPropertyChanged(nameof(EmptyTrashButtonText));
            }
        }
    }

    public string EmptyTrashButtonText => IsEmptyingTrash
        ? _localization.T("L.EmptyingTrash")
        : _localization.T("L.EmptyTrashNow");

    public ObservableCollection<SkipGroupViewModel> SkipGroups { get; } = [];

    public bool HasSkipGroups => SkipGroups.Count > 0;

    public bool SkippedTruncated { get; private set; }

    public bool CanExportSkipReport => _reportExporter is not null &&
                                       !IsExportingSkip &&
                                       _lastReport is { Items.Count: > 0 };

    public bool IsExportingSkip
    {
        get => _isExportingSkip;
        private set
        {
            if (SetProperty(ref _isExportingSkip, value))
            {
                OnPropertyChanged(nameof(CanExportSkipReport));
                OnPropertyChanged(nameof(ExportSkipText));
            }
        }
    }

    public string ExportSkipText => IsExportingSkip
        ? _localization.T("L.Exporting")
        : _localization.T("L.ExportCsv");

    // ---- commands ----

    [RelayCommand]
    private Task ScanAsync() => RunScanAsync();

    [RelayCommand]
    private Task RescanAsync() => RunScanAsync();

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Re-reads fields the settings window can change while the app is open.</summary>
    public void RefreshSettings()
    {
        if (_adminHelper is not null)
        {
            _adminHelper.Enabled = _settings.UseAdminHelper;
        }

        _exclusions.Replace(_settings.Exclusions);

        OnPropertyChanged(nameof(UseAdminHelper));
        OnPropertyChanged(nameof(IsQuickScan));
        OnPropertyChanged(nameof(ScanScopeText));
        OnPropertyChanged(nameof(WelcomeScanHint));
    }

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
        WasScanCanceled = false;
        IsScanComplete = false;
        Stage = AppStage.Scanning;
        SetStatus("L.Scanning");

        _scanClock.Restart();
        SetScanElapsed(TimeSpan.Zero);
        _scanTimer.Start();

        var quick = IsQuickScan;
        var thresholdBytes = _largeFileThreshold > 0
            ? _largeFileThreshold
            : Math.Max(1, _settings.LargeFileThresholdMb) * 1024L * 1024L;
        var hashBudgetBytes = Math.Max(1, _settings.HashBudgetGb) * 1024L * 1024L * 1024L;
        var request = new VolumeScanRequest(
            volume.Info,
            volume.IsSystem ? ContentRoots() : [volume.Root],
            IncludeCaches: volume.IsSystem,
            // Quick scans skip large files and duplicates on the system drive. A data
            // drive in quick mode still lists large files, because otherwise the scan
            // would only probe the trash and report nothing useful.
            IncludeLargeFiles: !quick || !volume.IsSystem,
            IncludeDuplicates: !quick,
            thresholdBytes,
            hashBudgetBytes,
            LargeFileLimit: _settings.LargeFileLimit,
            DuplicateMinSizeBytes: Math.Max(1, _settings.DuplicateMinSizeMb) * 1024L * 1024L,
            DuplicateGroupLimit: _settings.DuplicateGroupLimit);

        var progress = new Progress<VolumeScanProgress>(OnVolumeProgress);
        var updates = new Progress<ScanUpdate>(OnScanUpdate);

        try
        {
            var result = await _volumeScan.ScanAsync(request, progress, updates, _cts.Token);
            Finish(result, volume);
        }
        catch (OperationCanceledException)
        {
            SetScanElapsed(_scanClock.Elapsed);

            if (Stage == AppStage.Results ||
                Categories.Any(category => category.IsApplicable && category.FileCount > 0))
            {
                WasScanCanceled = true;

                foreach (var category in Categories.Where(category => category.IsScanning))
                {
                    category.IsScanning = false;
                    category.IsCanceled = true;
                }

                Stage = AppStage.Results;
                SetStatus("L.ScanCanceled");
            }
            else
            {
                Stage = AppStage.Welcome;
                SetStatus(null);
            }
        }
        catch (Exception exception)
        {
            Stage = AppStage.Welcome;
            SetStatusRaw(_localization.Format("L.ScanFailedFormat", exception.Message));
        }
        finally
        {
            _scanTimer.Stop();
            _scanClock.Stop();
            IsScanComplete = true;
            OnPropertyChanged(nameof(IsScanComplete));
        }
    }

    private void UpdateScanElapsed()
    {
        if (Stage != AppStage.Scanning)
        {
            _scanTimer.Stop();
            return;
        }

        SetScanElapsed(_scanClock.Elapsed);
    }

    private void SetScanElapsed(TimeSpan elapsed)
    {
        ScanElapsedText = _localization.Format("L.ScanElapsedFormat", Math.Round(elapsed.TotalSeconds, 1));
        OnPropertyChanged(nameof(ScanElapsedText));
    }

    /// <summary>Says which categories this volume will produce before anything is read.</summary>
    private void MarkPending(VolumeItemViewModel volume)
    {
        var quick = IsQuickScan;

        foreach (var category in Categories)
        {
            var adminCategory = category.Category is CleanCategory.SystemUpdates or CleanCategory.SystemLogs;
            var adminRuleAvailable = category.Category == CleanCategory.SystemUpdates
                ? AdminRuleIds.SystemUpdatesForCurrentPlatform is not null
                : AdminRuleIds.SystemLogsForCurrentPlatform is not null;

            category.IsApplicable = adminCategory
                ? volume.IsSystem && adminRuleAvailable && IsAdminHelperAvailable && UseAdminHelper
                : volume.IsSystem
                    ? category.Category != CleanCategory.LargeFiles &&
                      (category.Category != CleanCategory.Duplicates || !quick)
                    : category.Category is CleanCategory.LargeFiles or CleanCategory.Trash ||
                      (category.Category == CleanCategory.Duplicates && !quick);

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
            _lastBatch = _journal.Find(report.BatchId);
            ResetUndoState();
            RefreshRecentCleanups();
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
        _lastBatch = null;
        IsEmptyTrashConfirmVisible = false;
        IsEmptyingTrash = false;
        SkipGroups.Clear();
        SkippedTruncated = false;
        OnPropertyChanged(nameof(HasSkipGroups));
        OnPropertyChanged(nameof(SkippedTruncated));
        OnPropertyChanged(nameof(CanExportSkipReport));
        ResetUndoState();
        CompletionItems.Clear();
        Stage = AppStage.Welcome;
        SetStatus(null);
    }

    [RelayCommand]
    private void OpenTrash() => _trash.OpenTrash();

    [RelayCommand]
    private async Task UndoLastCleanAsync()
    {
        if (_lastBatch is not { } batch || IsUndoing)
        {
            return;
        }

        IsUndoing = true;
        _undoRestoredCount = 0;
        _undoFailedCount = 0;
        _undoRestoredBytes = 0;
        _undoError = null;
        UndoMessage = string.Empty;

        var restoredPaths = new List<string>();

        try
        {
            foreach (var item in batch.Items.Where(item => !item.Restored).Reverse())
            {
                if (_trash.TryRestore(item.TrashPath, item.Path, out var error))
                {
                    restoredPaths.Add(item.Path);
                    _undoRestoredCount++;
                    _undoRestoredBytes += item.Size;
                }
                else
                {
                    _undoFailedCount++;
                    _undoError = error;
                }
            }
        }
        finally
        {
            IsUndoing = false;
        }

        if (restoredPaths.Count > 0)
        {
            _journal.MarkRestored(batch.BatchId, restoredPaths);
            _lastBatch = _journal.Find(batch.BatchId) ?? batch;

            if (_lastReport is { } report)
            {
                _lastReport = report with
                {
                    TrashedBytes = Math.Max(0, report.TrashedBytes - _undoRestoredBytes)
                };
            }

            RefreshCompletionTexts();
            RefreshRecentCleanups();
        }

        RefreshUndoMessage();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(IsUndoVisible));
        SetStatusRaw(_undoMessage);
    }

    [RelayCommand]
    private void OpenEmptyTrashConfirm()
    {
        if (CanEmptyTrash)
        {
            IsEmptyTrashConfirmVisible = true;
        }
    }

    [RelayCommand]
    private void CancelEmptyTrashConfirm() => IsEmptyTrashConfirmVisible = false;

    [RelayCommand]
    private async Task ConfirmEmptyTrashAsync()
    {
        if (IsEmptyingTrash)
        {
            return;
        }

        IsEmptyTrashConfirmVisible = false;
        IsEmptyingTrash = true;

        try
        {
            // Windows empties one drive's recycle bin; the Unix trash is shared, so all
            // batches have to be marked as non-restorable there.
            var volumeRoot = OperatingSystem.IsWindows() ? SelectedVolume?.Info.Root : null;
            var freed = await Task.Run(() => _trash.Empty(volumeRoot));

            _journal.MarkTrashEmptied(volumeRoot);

            if (_lastReport is { } report)
            {
                _lastReport = report with
                {
                    TrashedBytes = 0,
                    FreedBytes = report.FreedBytes + Math.Max(0, freed),
                    TrashEmptied = true
                };
            }

            _lastBatch = _lastReport is { } current ? _journal.Find(current.BatchId) : null;

            RefreshCompletionTexts();
            RefreshRecentCleanups();
            SetStatusRaw(_localization.Format("L.EmptyTrashDoneFormat", ByteSize.Format(freed)));
        }
        catch (Exception exception)
        {
            SetStatusRaw(_localization.Format("L.EmptyTrashFailedFormat", exception.Message));
        }
        finally
        {
            IsEmptyingTrash = false;
        }
    }

    [RelayCommand]
    private async Task ExportSkipReportAsync()
    {
        if (_reportExporter is null ||
            _lastReport is not { } report ||
            report.Items.Count == 0 ||
            IsExportingSkip)
        {
            return;
        }

        IsExportingSkip = true;

        try
        {
            var csv = SkipReportCsv.Build(report.Items, DateTimeOffset.Now);
            var saved = await _reportExporter.SaveAsync(
                $"airclean-skipped-{DateTime.Now:yyyyMMdd}.csv",
                csv);

            SetStatusRaw(_localization.T(saved ? "L.ExportSaved" : "L.ExportCanceled"));
        }
        finally
        {
            IsExportingSkip = false;
        }
    }

    [RelayCommand]
    private void Escape()
    {
        if (IsEmptyTrashConfirmVisible && !IsEmptyingTrash)
        {
            CancelEmptyTrashConfirm();
            return;
        }

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
        _limitedByGroupCap = result.LimitedByGroupCap;
        _duplicateGroupCount = result.DuplicateGroupCount;
        OnPropertyChanged(nameof(HasLimited));
        OnPropertyChanged(nameof(LimitedNote));

        _scannedRoot = volume.Root;
        _scanTimer.Stop();
        _scanClock.Stop();
        SetScanElapsed(result.Elapsed);
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
            SkipGroups.Clear();
            SkippedTruncated = false;
            OnPropertyChanged(nameof(HasSkipGroups));
            OnPropertyChanged(nameof(SkippedTruncated));
            OnPropertyChanged(nameof(CanExportSkipReport));
            OnPropertyChanged(nameof(HasAbortNote));
            OnPropertyChanged(nameof(AbortNote));
            return;
        }

        var total = report.TotalBytes;
        var fullyUndone = _undoRestoredCount > 0 &&
                          report.TrashedBytes == 0 &&
                          report.FreedBytes == 0;

        CompletedTitle = fullyUndone
            ? _localization.Format("L.UndoneTitleFormat", ByteSize.Format(_undoRestoredBytes))
            : _localization.Format("L.DoneTitleFormat", ByteSize.Format(total));

        CompletedSubTrash = !fullyUndone && report.TrashedBytes > 0
            ? _localization.Format("L.DoneSubTrashFormat", ByteSize.Format(report.TrashedBytes))
            : string.Empty;
        CompletedSubFreed = !fullyUndone && report.FreedBytes > 0
            ? _localization.Format("L.DoneSubFreedFormat", ByteSize.Format(report.FreedBytes))
            : string.Empty;

        HasFreedNote = !fullyUndone && report.FreedBytes > 0;
        CompletedNote = !fullyUndone && report.TrashedBytes > 0
            ? _localization.Format("L.DoneNoteTrashFormat", ByteSize.Format(report.TrashedBytes))
            : string.Empty;
        HasTrashNote = !fullyUndone && report.TrashedBytes > 0;

        var skipped = report.Skipped.Values.Sum();
        HasSkipped = skipped > 0;
        SkippedText = skipped == 1
            ? _localization.T("L.SkippedOne")
            : _localization.Format("L.SkippedFormat", skipped);
        RefreshSkipGroups(report);

        OnPropertyChanged(nameof(CompletedTitle));
        OnPropertyChanged(nameof(CompletedSubTrash));
        OnPropertyChanged(nameof(CompletedSubFreed));
        OnPropertyChanged(nameof(CompletedNote));
        OnPropertyChanged(nameof(HasTrashNote));
        OnPropertyChanged(nameof(HasFreedNote));
        OnPropertyChanged(nameof(ShowSubSeparator));
        OnPropertyChanged(nameof(SkippedText));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(CanOpenTrash));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(IsUndoVisible));
        OnPropertyChanged(nameof(HasCleanupDetails));
        OnPropertyChanged(nameof(HasAbortNote));
        OnPropertyChanged(nameof(AbortNote));
    }

    private void RefreshSkipGroups(DeletionReport report)
    {
        SkipGroups.Clear();

        foreach (var group in report.Items
                     .GroupBy(item => item.Reason)
                     .OrderByDescending(group => group.Count()))
        {
            var paths = group
                .Select(item => item.Path)
                .Where(path => path.Length > 0)
                .Take(20)
                .ToArray();

            SkipGroups.Add(new SkipGroupViewModel(group.Key, _localization, paths, group.Count()));
        }

        SkippedTruncated = report.SkippedTruncated ||
                           report.Skipped.Values.Sum() > report.Items.Count;

        OnPropertyChanged(nameof(HasSkipGroups));
        OnPropertyChanged(nameof(SkippedTruncated));
        OnPropertyChanged(nameof(CanExportSkipReport));
    }

    private void ResetUndoState()
    {
        _undoRestoredCount = 0;
        _undoFailedCount = 0;
        _undoRestoredBytes = 0;
        _undoError = null;
        UndoMessage = string.Empty;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(IsUndoVisible));
        OnPropertyChanged(nameof(HasCleanupDetails));
    }

    private void RefreshUndoMessage()
    {
        if (_undoRestoredCount == 0 && _undoFailedCount == 0)
        {
            UndoMessage = string.Empty;
            return;
        }

        UndoMessage = _undoFailedCount == 0
            ? _localization.Format("L.UndoResultFormat", _undoRestoredCount, ByteSize.Format(_undoRestoredBytes))
            : _localization.Format(
                "L.UndoPartialFormat",
                _undoRestoredCount,
                _undoRestoredCount + _undoFailedCount,
                _undoError ?? string.Empty);
    }

    private void RefreshRecentCleanups()
    {
        RecentCleanups.Clear();

        foreach (var batch in _journal.ReadRecent(3))
        {
            RecentCleanups.Add(new RecentCleanupViewModel(
                batch,
                _localization,
                _trash.IsSupported,
                UndoRecentAsync));
        }

        OnPropertyChanged(nameof(HasRecentCleanups));
        OnPropertyChanged(nameof(HasNoRecentCleanups));
    }

    private async Task UndoRecentAsync(RecentCleanupViewModel item)
    {
        if (item.Batch is not { } batch || !batch.CanUndo)
        {
            return;
        }

        var restored = new List<string>();
        var bytes = 0L;
        var failed = 0;
        string? error = null;

        await Task.Run(() =>
        {
            foreach (var entry in batch.Items.Where(entry => !entry.Restored).Reverse())
            {
                if (_trash.TryRestore(entry.TrashPath, entry.Path, out var reason))
                {
                    restored.Add(entry.Path);
                    bytes += entry.Size;
                }
                else
                {
                    failed++;
                    error = reason;
                }
            }
        });

        if (restored.Count > 0)
        {
            _journal.MarkRestored(batch.BatchId, restored);
        }

        RefreshRecentCleanups();

        SetStatusRaw(failed == 0
            ? _localization.Format("L.UndoResultFormat", restored.Count, ByteSize.Format(bytes))
            : _localization.Format("L.UndoPartialFormat", restored.Count, restored.Count + failed, error ?? string.Empty));
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
