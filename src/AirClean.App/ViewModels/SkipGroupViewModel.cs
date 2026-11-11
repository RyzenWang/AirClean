using AirClean.App.Services;
using AirClean.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirClean.App.ViewModels;

/// <summary>One reason group in the completion page's skip report.</summary>
public sealed partial class SkipGroupViewModel : ObservableObject
{
    private bool _isExpanded;

    public SkipGroupViewModel(
        SkipReason reason,
        LocalizationService localization,
        IReadOnlyList<string> paths,
        int count)
    {
        Reason = reason;
        Title = localization.T(ReasonKey(reason));
        CountText = count == 1
            ? localization.T("L.ItemCountOneFormat")
            : localization.Format("L.ItemsCountFormat", count);
        Paths = paths;
    }

    public SkipReason Reason { get; }

    public string Title { get; }

    public string CountText { get; }

    public IReadOnlyList<string> Paths { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    private static string ReasonKey(SkipReason reason) => reason switch
    {
        SkipReason.Permission => "L.SkippedPermission",
        SkipReason.ChangedSinceScan => "L.SkippedChanged",
        SkipReason.NotFound => "L.SkippedNotFound",
        SkipReason.Protected => "L.SkippedProtected",
        SkipReason.ReparsePoint => "L.SkippedReparse",
        SkipReason.DeleteFailed => "L.SkippedDeleteFailed",
        SkipReason.Excluded => "L.SkippedExcluded",
        _ => "L.SkippedInUse"
    };
}
