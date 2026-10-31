using AirClean.App.Services;
using AirClean.Core.Formatting;
using AirClean.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirClean.App.ViewModels;

/// <summary>One set of identical files; the oldest copy is kept.</summary>
public sealed partial class DuplicateGroupViewModel : ObservableObject
{
    private readonly Action<DuplicateGroupViewModel> _onToggled;
    private bool _isSelected;

    public DuplicateGroupViewModel(
        DuplicateGroup group,
        LocalizationService localization,
        Action<DuplicateGroupViewModel> onToggled)
    {
        _onToggled = onToggled;

        Key = group.Key;
        Title = Path.GetFileName(group.Keeper.Path);
        Summary = localization.Format(
            "L.DuplicateSummaryFormat",
            group.Files.Count,
            ByteSize.Format(group.FileSize),
            ByteSize.Format(group.RemovableBytes));
        KeepText = localization.Format("L.DuplicateKeepFormat", group.Keeper.Path);
        RemovablePaths = group.Removable.Select(file => file.Path).ToArray();
        RemovableBytes = group.RemovableBytes;
    }

    public string Key { get; }

    public string Title { get; }

    public string Summary { get; }

    public string KeepText { get; }

    public IReadOnlyList<string> RemovablePaths { get; }

    public long RemovableBytes { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onToggled(this);
            }
        }
    }
}
