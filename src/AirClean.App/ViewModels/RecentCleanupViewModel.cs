using AirClean.App.Services;
using AirClean.Core.Deleting;
using AirClean.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirClean.App.ViewModels;

/// <summary>
/// One row in the welcome page's recent-cleanup list. The row owns its undo command so the
/// data template stays declarative; the view model supplies the actual restore work.
/// </summary>
public sealed class RecentCleanupViewModel : ObservableObject
{
    public RecentCleanupViewModel(
        JournalBatch batch,
        LocalizationService localization,
        bool trashSupported,
        Func<RecentCleanupViewModel, Task> undo)
    {
        Batch = batch;

        var when = batch.At.ToLocalTime().ToString("MMM d HH:mm", localization.Culture);
        var count = localization.Format("L.ItemsCountFormat", batch.DeletedCount);
        var size = ByteSize.Format(batch.TrashedBytes + batch.FreedBytes);
        SummaryText = $"{when} · {count} · {size}";

        CanUndo = trashSupported && batch.CanUndo;
        UndoText = localization.T("L.UndoShort");
        UndoCommand = new AsyncRelayCommand(() => undo(this), () => CanUndo);
    }

    public JournalBatch Batch { get; }

    public string SummaryText { get; }

    public string UndoText { get; }

    public bool CanUndo { get; }

    public IAsyncRelayCommand UndoCommand { get; }
}
