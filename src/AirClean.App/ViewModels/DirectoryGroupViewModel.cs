using AirClean.App.Services;
using AirClean.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirClean.App.ViewModels;

/// <summary>
/// One folder in the aggregated view of a category. The checkbox selects every file in
/// that folder; clicking the name drills into it.
/// </summary>
public sealed partial class DirectoryGroupViewModel : ObservableObject
{
    private readonly Action<DirectoryGroupViewModel, bool?> _onToggled;
    private bool? _isSelected;

    public DirectoryGroupViewModel(
        string path,
        int fileCount,
        long bytes,
        LocalizationService localization,
        Action onSelect,
        Action<DirectoryGroupViewModel, bool?> onToggled)
    {
        Path = path;
        FileCount = fileCount;
        Bytes = bytes;
        _onToggled = onToggled;

        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        Name = string.IsNullOrEmpty(trimmed) ? localization.T("L.UnknownFolder") : System.IO.Path.GetFileName(trimmed);

        if (string.IsNullOrEmpty(Name))
        {
            Name = trimmed;
        }

        CountText = localization.Format("L.CategoryCountFormat", fileCount);
        SizeText = ByteSize.Format(bytes);
        SelectCommand = new RelayCommand(onSelect);
    }

    public string Path { get; }

    public string Name { get; }

    public int FileCount { get; }

    public long Bytes { get; }

    public string CountText { get; }

    public string SizeText { get; }

    /// <summary>Drill into the folder (clicking the name).</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>All / none / some of the files in this folder.</summary>
    public bool? IsSelected
    {
        get => _isSelected;
        set
        {
            if (value is null || _isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            _onToggled(this, value);
        }
    }

    /// <summary>Updates the checkbox from the underlying rows without re-triggering them.</summary>
    public void RefreshState(bool? state)
    {
        if (_isSelected == state)
        {
            return;
        }

        _isSelected = state;
        OnPropertyChanged(nameof(IsSelected));
    }
}
