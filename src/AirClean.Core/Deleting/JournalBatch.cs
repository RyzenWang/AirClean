using AirClean.Core.Models;

namespace AirClean.Core.Deleting;

/// <summary>One file recorded in a cleanup batch.</summary>
public sealed record JournalItem(string Path, long Size, string? TrashPath, bool Restored);

/// <summary>
/// One cleanup batch as read from the journal. Batches written before journal v2 live in
/// monthly JSONL files: they are exposed with <see cref="Legacy"/> set, have no trash
/// paths, and cannot be restored.
/// </summary>
public sealed record JournalBatch(
    Guid BatchId,
    DateTimeOffset At,
    DeleteMode Mode,
    bool Canceled,
    long DeletedCount,
    long TrashedBytes,
    long FreedBytes,
    IReadOnlyDictionary<SkipReason, int> Skipped,
    IReadOnlyList<JournalItem> Items,
    bool Legacy = false,
    bool TrashEmptied = false)
{
    public int RestoredCount => Items.Count(item => item.Restored);

    /// <summary>Legacy batches have no restore paths, so nothing can be pending.</summary>
    public int PendingRestoreCount => Legacy ? 0 : Items.Count(item => !item.Restored);

    public long PendingRestoreBytes => Legacy
        ? 0
        : Items.Where(item => !item.Restored).Sum(item => item.Size);

    /// <summary>
    /// True when the batch still has items worth offering "undo" for. The UI additionally
    /// checks that the trash has not been emptied since. Permanent batches never qualify:
    /// there is nothing in the trash to move back.
    /// </summary>
    public bool CanUndo => !Legacy &&
                           !TrashEmptied &&
                           PendingRestoreCount > 0 &&
                           (Mode == DeleteMode.Trash || Items.Any(item => item.TrashPath is not null));
}

/// <summary>What one deletion contributed to the journal; internal to the deletion path.</summary>
internal sealed record JournalEntry(string Path, long Size, string? TrashPath);
