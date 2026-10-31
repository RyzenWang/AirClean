using AirClean.App.Services;
using AirClean.Core.Formatting;
using AirClean.Core.Models;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AirClean.App.ViewModels;

public sealed partial class CategoryViewModel : ObservableObject
{
    private const int MaxPreviewItems = 200;
    private const int MaxDirectoryGroups = 50;

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    private static readonly Dictionary<CleanCategory, StreamGeometry> IconCache = new()
    {
        [CleanCategory.SystemTemp] = StreamGeometry.Parse("M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5M12 8v4l3 2"),
        [CleanCategory.AppCache] = StreamGeometry.Parse("M12 2l9 5-9 5-9-5 9-5ZM3 12l9 5 9-5M3 17l9 5 9-5"),
        [CleanCategory.LogsDiagnostics] = StreamGeometry.Parse("M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8ZM14 2v6h6M9 13h6M9 17h6"),
        [CleanCategory.Trash] = StreamGeometry.Parse("M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6M10 11v6M14 11v6"),
        [CleanCategory.DeveloperCache] = StreamGeometry.Parse("M8 6l-5 6 5 6M16 6l5 6-5 6"),
        [CleanCategory.LargeFiles] = StreamGeometry.Parse("M3 6h18a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1ZM6 10h.01M6 14h.01M10 12h7"),
        [CleanCategory.Duplicates] = StreamGeometry.Parse("M8 8h12v12H8zM4 16V4h12")
    };

    private readonly LocalizationService _localization;
    private readonly Action<string> _reveal;
    private readonly bool _revealSupported;

    private bool _isApplicable = true;
    private bool _isScanning;
    private bool _isExpanded;
    private bool? _selectionState = false;
    private bool _defaultSelected;
    private int _selectedShownCount;
    private long _selectedShownBytes;
    private bool _suppressItemEvents;
    private bool _isDirectoryView;
    private string? _filter;

    public CategoryViewModel(
        CleanCategory category,
        LocalizationService localization,
        Action<string> reveal,
        bool revealSupported)
    {
        Category = category;
        _localization = localization;
        _reveal = reveal;
        _revealSupported = revealSupported;

        Name = localization.T(NameKey);
        Description = localization.T(DescriptionKey);
        CountText = localization.Format("L.CategoryCountFormat", 0);
    }

    public CleanCategory Category { get; }

    public Geometry Icon => IconCache[Category];

    public string Name { get; private set; }

    public string Description { get; private set; }

    public string NameKey => Category switch
    {
        CleanCategory.SystemTemp => "L.CategorySystemTemp",
        CleanCategory.AppCache => "L.CategoryAppCache",
        CleanCategory.LogsDiagnostics => "L.CategoryLogs",
        CleanCategory.Trash => "L.CategoryTrash",
        CleanCategory.LargeFiles => "L.CategoryLargeFiles",
        CleanCategory.Duplicates => "L.CategoryDuplicates",
        _ => "L.CategoryDev"
    };

    public string DescriptionKey => NameKey + "Desc";

    public Safety SafetyLevel { get; private set; } = Safety.Safe;

    public bool IsPermanent { get; private set; }

    /// <summary>Large files and duplicates have no bulk checkbox: each entry is picked by hand.</summary>
    public bool SupportsItemSelection => Category is CleanCategory.LargeFiles or CleanCategory.Duplicates;

    public bool IsDuplicates => Category == CleanCategory.Duplicates;

    public bool IsApplicable
    {
        get => _isApplicable;
        set => SetProperty(ref _isApplicable, value);
    }

    /// <summary>True while this category's stage is still running.</summary>
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    /// <summary>The scan stopped early for this category (item or hash cap).</summary>
    public bool HasLimited_Internal { get; private set; }

    /// <summary>All / none / some. Null drives the indeterminate checkbox.</summary>
    public bool? SelectionState
    {
        get => _selectionState;
        set
        {
            if (_selectionState == value)
            {
                return;
            }

            var selectAll = value == true;
            _defaultSelected = selectAll;

            _suppressItemEvents = true;
            foreach (var row in Details)
            {
                row.IsSelected = selectAll;
            }

            _suppressItemEvents = false;

            _selectedShownCount = selectAll ? Details.Count : 0;
            _selectedShownBytes = selectAll ? Details.Sum(row => row.Item.Size) : 0;
            SetState(selectAll);

            // Folder checkboxes and the header "select all" must follow the category box.
            RefreshDirectoryStates();
            OnPropertyChanged(nameof(VisibleSelectionState));
        OnPropertyChanged(nameof(SelectAllLabel));
            RaiseSelectionChanged();
        }
    }

    public bool IsSelected => _selectionState == true;

    public bool HasSelection => SelectedFileCount > 0;

    public long Bytes { get; private set; }

    public int FileCount { get; private set; }

    public bool HasItems => FileCount > 0;

    public bool CanExpand => FileCount > 0;

    public ObservableCollection<ItemRowViewModel> Details { get; } = [];

    public ObservableCollection<ItemRowViewModel> VisibleDetails { get; } = [];

    public ObservableCollection<DirectoryGroupViewModel> DirectoryGroups { get; } = [];

    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = [];

    public string DetailSummary { get; private set; } = string.Empty;

    public string SelectionNote { get; private set; } = string.Empty;

    public bool HasPartialSelection => _selectionState is null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsDirectoryView
    {
        get => _isDirectoryView;
        private set
        {
            if (SetProperty(ref _isDirectoryView, value))
            {
                OnPropertyChanged(nameof(IsFileView));
                OnPropertyChanged(nameof(IsDirectoryViewVisible));
            }
        }
    }

    public bool IsFileView => !IsDirectoryView && !IsDuplicates;

    public bool IsDirectoryViewVisible => IsDirectoryView && !IsDuplicates;

    public bool HasFilter => _filter is not null;

    public string FilterText => _filter ?? string.Empty;

    public string SizeText => ByteSize.Format(Bytes);

    public string CountText { get; private set; }

    public CategoryScan? Scan { get; private set; }

    public event EventHandler? SelectionChanged;

    public int SelectedFileCount => (_defaultSelected ? Math.Max(0, FileCount - Details.Count) : 0) + _selectedShownCount;

    public long SelectedBytes
    {
        get
        {
            var unseen = _defaultSelected ? Math.Max(0, Bytes - Details.Sum(row => row.Item.Size)) : 0;
            return unseen + _selectedShownBytes;
        }
    }

    public string DisplaySizeText => ByteSize.Format(SelectedBytes);

    public void Apply(CategoryScan scan)
    {
        Scan = scan;
        Bytes = scan.Bytes;
        FileCount = scan.FileCount;
        SafetyLevel = scan.Safety;
        IsPermanent = scan.IsPermanent;
        HasLimited_Internal = scan.Limited;
        IsScanning = false;
        CountText = _localization.Format("L.CategoryCountFormat", FileCount);

        var selectAll = scan.Safety == Safety.Safe && scan.FileCount > 0;
        _defaultSelected = selectAll;

        Details.Clear();
        foreach (var item in scan.Items.OrderByDescending(item => item.Size).Take(MaxPreviewItems))
        {
            Details.Add(new ItemRowViewModel(item, selectAll, _revealSupported, OnItemSelectionChanged, _reveal));
        }

        _selectedShownCount = selectAll ? Details.Count : 0;
        _selectedShownBytes = selectAll ? Details.Sum(row => row.Item.Size) : 0;

        _isDirectoryView = false;
        _filter = null;
        BuildDirectoryGroups();
        RebuildVisibleDetails();
        SetState(selectAll);
        RefreshDetailSummary();

        // Hand-picked categories open themselves: an empty list would hide the work.
        IsExpanded = SupportsItemSelection && FileCount > 0;

        OnPropertyChanged(nameof(Bytes));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanExpand));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(SafetyLevel));
        OnPropertyChanged(nameof(IsPermanent));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsDirectoryView));
        OnPropertyChanged(nameof(IsFileView));
    }

    /// <summary>Fills the duplicate view: groups on top, removable copies as item rows.</summary>
    public void ApplyDuplicates(CategoryScan scan, IReadOnlyList<DuplicateGroup> groups)
    {
        Apply(scan);

        DuplicateGroups.Clear();
        foreach (var group in groups)
        {
            DuplicateGroups.Add(new DuplicateGroupViewModel(group, _localization, OnDuplicateGroupToggled));
        }

        IsExpanded = groups.Count > 0;
        RefreshDetailSummary();

        OnPropertyChanged(nameof(DuplicateGroups));
        OnPropertyChanged(nameof(IsFileView));
        OnPropertyChanged(nameof(IsDirectoryViewVisible));
    }

    public void Reset()
    {
        Scan = null;
        Bytes = 0;
        FileCount = 0;
        CountText = _localization.Format("L.CategoryCountFormat", 0);
        IsExpanded = false;
        _defaultSelected = false;
        _selectedShownCount = 0;
        _selectedShownBytes = 0;
        _filter = null;
        _isDirectoryView = false;
        Details.Clear();
        VisibleDetails.Clear();
        DirectoryGroups.Clear();
        DuplicateGroups.Clear();
        DetailSummary = string.Empty;
        SetState(false);

        OnPropertyChanged(nameof(VisibleSelectionState));
        OnPropertyChanged(nameof(SelectAllLabel));
        OnPropertyChanged(nameof(DuplicateGroups));
        OnPropertyChanged(nameof(Bytes));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanExpand));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(DetailSummary));
        OnPropertyChanged(nameof(IsDirectoryView));
        OnPropertyChanged(nameof(IsFileView));
    }

    public void RefreshLocalization()
    {
        Name = _localization.T(NameKey);
        Description = _localization.T(DescriptionKey);
        CountText = _localization.Format("L.CategoryCountFormat", FileCount);
        BuildDirectoryGroups();
        RebuildVisibleDetails();
        RefreshDetailSummary();

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>Scan payload restricted to what the user actually selected.</summary>
    public CategoryScan? BuildScan()
    {
        if (Scan is null || SelectedFileCount == 0)
        {
            return null;
        }

        if (SelectedFileCount >= FileCount)
        {
            return Scan;
        }

        var filtered = new CategoryScan
        {
            Category = Scan.Category,
            Safety = Scan.Safety,
            IsPermanent = Scan.IsPermanent,
            IsExplicitSelection = SupportsItemSelection,
            TrashRoot = Scan.TrashRoot
        };

        var selectedShown = Details
            .Where(row => row.IsSelected)
            .Select(row => row.FullPath)
            .ToHashSet(PathComparer);

        var excludedShown = Details
            .Where(row => !row.IsSelected)
            .Select(row => row.FullPath)
            .ToHashSet(PathComparer);

        foreach (var item in Scan.Items)
        {
            var include = _defaultSelected
                ? !excludedShown.Contains(item.Path)
                : selectedShown.Contains(item.Path);

            if (include)
            {
                filtered.Add(item);
            }
        }

        return filtered;
    }

    private void OnItemSelectionChanged()
    {
        if (_suppressItemEvents)
        {
            return;
        }

        _selectedShownCount = Details.Count(row => row.IsSelected);
        _selectedShownBytes = Details.Where(row => row.IsSelected).Sum(row => row.Item.Size);

        var total = SelectedFileCount;
        SetState(total == 0 ? false : total >= FileCount ? true : null);

        // Folder checkboxes and the header action must track the rows, not just the category.
        RefreshDirectoryStates();
        OnPropertyChanged(nameof(VisibleSelectionState));
        OnPropertyChanged(nameof(SelectAllLabel));
        RefreshDetailSummary();
        RaiseSelectionChanged();
    }

    private void OnDuplicateGroupToggled(DuplicateGroupViewModel group)
    {
        var paths = group.RemovablePaths.ToHashSet(PathComparer);

        _suppressItemEvents = true;
        foreach (var row in Details.Where(row => paths.Contains(row.FullPath)))
        {
            row.IsSelected = group.IsSelected;
        }

        _suppressItemEvents = false;

        _selectedShownCount = Details.Count(row => row.IsSelected);
        _selectedShownBytes = Details.Where(row => row.IsSelected).Sum(row => row.Item.Size);

        var total = SelectedFileCount;
        SetState(total == 0 ? false : total >= FileCount ? true : null);

        RefreshDirectoryStates();
        OnPropertyChanged(nameof(VisibleSelectionState));
        OnPropertyChanged(nameof(SelectAllLabel));
        RefreshDetailSummary();
        RaiseSelectionChanged();
    }

    private void SetState(bool? state)
    {
        if (_selectionState == state)
        {
            return;
        }

        _selectionState = state;
        OnPropertyChanged(nameof(SelectionState));
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasPartialSelection));
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedFileCount));
        OnPropertyChanged(nameof(DisplaySizeText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasPartialSelection));
        OnPropertyChanged(nameof(SelectionNote));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshDetailSummary()
    {
        if (FileCount == 0)
        {
            DetailSummary = string.Empty;
            SelectionNote = string.Empty;
            OnPropertyChanged(nameof(DetailSummary));
            OnPropertyChanged(nameof(SelectionNote));
            return;
        }

        if (HasFilter)
        {
            DetailSummary = _localization.Format("L.FilteredCountFormat", VisibleDetails.Count);
        }
        else if (IsDuplicates)
        {
            DetailSummary = _localization.Format(
                "L.DuplicateSummaryLineFormat",
                DuplicateGroups.Count,
                ByteSize.Format(Scan?.MatchedBytes ?? Bytes));
        }
        else if (IsDirectoryView)
        {
            DetailSummary = _localization.Format("L.DirectorySummaryFormat", DirectoryGroups.Count, Details.Count);
        }
        else
        {
            var matched = Scan is { MatchedCount: > 0 } ? Scan.MatchedCount : FileCount;
            DetailSummary = _localization.Format("L.DetailsSummaryFormat", Details.Count, matched);
        }

        SelectionNote = HasPartialSelection
            ? _localization.Format("L.PartialNoteFormat", SelectedFileCount, FileCount)
            : string.Empty;

        OnPropertyChanged(nameof(DetailSummary));
        OnPropertyChanged(nameof(SelectionNote));
    }

    private void BuildDirectoryGroups()
    {
        DirectoryGroups.Clear();

        var groups = Details
            .GroupBy(row => Path.GetDirectoryName(row.FullPath) ?? string.Empty)
            .Select(group => new DirectoryGroupViewModel(
                group.Key,
                group.Count(),
                group.Sum(row => row.Item.Size),
                _localization,
                () => ApplyFilter(group.Key),
                OnDirectoryToggled))
            .OrderByDescending(group => group.Bytes)
            .Take(MaxDirectoryGroups);

        foreach (var group in groups)
        {
            DirectoryGroups.Add(group);
        }

        RefreshDirectoryStates();
    }

    /// <summary>Checkbox for "all files currently listed", respecting an active folder filter.</summary>
    public bool? VisibleSelectionState
    {
        get
        {
            if (VisibleDetails.Count == 0)
            {
                return false;
            }

            var selected = VisibleDetails.Count(row => row.IsSelected);
            return selected == 0 ? false : selected == VisibleDetails.Count ? true : null;
        }
        set
        {
            if (value is null || VisibleDetails.Count == 0)
            {
                return;
            }

            _suppressItemEvents = true;
            foreach (var row in VisibleDetails)
            {
                row.IsSelected = value.Value;
            }

            _suppressItemEvents = false;

            RecomputeAfterBatch();
        }
    }

    /// <summary>"Select all" turns into "clear" once everything listed is selected.</summary>
    public string SelectAllLabel => VisibleSelectionState == true
        ? _localization.T("L.SelectNone")
        : _localization.T("L.SelectAll");

    /// <summary>Header button: select everything listed, or clear it when already full.</summary>
    [RelayCommand]
    private void ToggleVisibleSelection() => VisibleSelectionState = VisibleSelectionState != true;

    private void OnDirectoryToggled(DirectoryGroupViewModel group, bool? value)
    {
        if (value is null)
        {
            return;
        }

        _suppressItemEvents = true;
        foreach (var row in Details.Where(row => IsInFolder(row, group.Path)))
        {
            row.IsSelected = value.Value;
        }

        _suppressItemEvents = false;

        RecomputeAfterBatch();
    }

    /// <summary>Recomputes counters once after a batch of row changes.</summary>
    private void RecomputeAfterBatch()
    {
        _selectedShownCount = Details.Count(row => row.IsSelected);
        _selectedShownBytes = Details.Where(row => row.IsSelected).Sum(row => row.Item.Size);

        var total = SelectedFileCount;
        SetState(total == 0 ? false : total >= FileCount ? true : null);

        RefreshDirectoryStates();
        OnPropertyChanged(nameof(VisibleSelectionState));
        OnPropertyChanged(nameof(SelectAllLabel));
        RefreshDetailSummary();
        RaiseSelectionChanged();
    }

    private void RefreshDirectoryStates()
    {
        foreach (var group in DirectoryGroups)
        {
            var rows = Details.Where(row => IsInFolder(row, group.Path)).ToList();

            if (rows.Count == 0)
            {
                group.RefreshState(false);
                continue;
            }

            var selected = rows.Count(row => row.IsSelected);
            group.RefreshState(selected == 0 ? false : selected == rows.Count ? true : null);
        }
    }

    private static bool IsInFolder(ItemRowViewModel row, string folder) =>
        string.Equals(Path.GetDirectoryName(row.FullPath), folder, PathComparison);

    private void RebuildVisibleDetails()
    {
        VisibleDetails.Clear();

        var rows = _filter is null
            ? Details
            : Details.Where(row => string.Equals(Path.GetDirectoryName(row.FullPath), _filter, PathComparison));

        foreach (var row in rows)
        {
            VisibleDetails.Add(row);
        }

        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(FilterText));
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        if (CanExpand)
        {
            IsExpanded = !IsExpanded;
        }
    }

    [RelayCommand]
    private void ShowFiles()
    {
        IsDirectoryView = false;
        RefreshDetailSummary();
    }

    [RelayCommand]
    private void ShowDirectories()
    {
        IsDirectoryView = true;
        RefreshDetailSummary();
    }

    [RelayCommand]
    private void FilterByDirectory(DirectoryGroupViewModel? group)
    {
        if (group is not null)
        {
            ApplyFilter(group.Path);
        }
    }

    private void ApplyFilter(string path)
    {
        _filter = path;
        IsDirectoryView = false;
        RebuildVisibleDetails();
        RefreshDetailSummary();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        _filter = null;
        RebuildVisibleDetails();
        RefreshDetailSummary();
    }
}
