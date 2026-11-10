using AirClean.Core.Deleting;
using AirClean.Core.Models;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class JournalStoreTests : IDisposable
{
    private readonly string _journal = Path.Combine(
        Path.GetTempPath(),
        "airclean-journal-test",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_batch_round_trips_with_items_and_trash_paths()
    {
        var store = new JournalStore(_journal);
        var report = Report(trashedBytes: 512, deleted: 1);
        var entries = new[]
        {
            new JournalEntry("/cache/a.bin", 512, "/trash/a.bin")
        };

        var written = store.Append(report, DeleteMode.Trash, entries, MinutesAgo(5));
        var read = Assert.Single(store.ReadRecent());

        Assert.Equal(written.BatchId, read.BatchId);
        Assert.Equal(DeleteMode.Trash, read.Mode);
        Assert.Equal(512, read.TrashedBytes);
        Assert.Equal(1, read.DeletedCount);
        Assert.False(read.Canceled);
        Assert.False(read.Legacy);
        Assert.True(read.CanUndo);

        var item = Assert.Single(read.Items);
        Assert.Equal("/cache/a.bin", item.Path);
        Assert.Equal("/trash/a.bin", item.TrashPath);
        Assert.False(item.Restored);
    }

    [Fact]
    public void ReadRecent_returns_the_newest_batches_first_and_honours_the_limit()
    {
        var store = new JournalStore(_journal);
        store.Append(Report(10), DeleteMode.Trash, [new JournalEntry("/a", 10, null)], MinutesAgo(30));
        store.Append(Report(20), DeleteMode.Trash, [new JournalEntry("/b", 20, null)], MinutesAgo(20));
        store.Append(Report(30), DeleteMode.Trash, [new JournalEntry("/c", 30, null)], MinutesAgo(10));

        var recent = store.ReadRecent(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("/c", recent[0].Items[0].Path);
        Assert.Equal("/b", recent[1].Items[0].Path);
    }

    [Fact]
    public void Find_returns_the_requested_batch_and_null_when_missing()
    {
        var store = new JournalStore(_journal);
        var report = Report(64);
        store.Append(report, DeleteMode.Trash, [new JournalEntry("/a", 64, "/trash/a")], MinutesAgo(5));

        Assert.NotNull(store.Find(report.BatchId));
        Assert.Null(store.Find(Guid.NewGuid()));
    }

    [Fact]
    public void MarkRestored_persists_the_restored_flag()
    {
        var store = new JournalStore(_journal);
        var report = Report(100);
        store.Append(
            report,
            DeleteMode.Trash,
            [new JournalEntry("/a", 50, "/trash/a"), new JournalEntry("/b", 50, "/trash/b")],
            MinutesAgo(5));

        Assert.True(store.MarkRestored(report.BatchId, ["/a"]));

        var batch = Assert.Single(store.ReadRecent());
        Assert.Equal(1, batch.RestoredCount);
        Assert.Equal(1, batch.PendingRestoreCount);
        Assert.True(batch.Items[0].Restored);
        Assert.False(batch.Items[1].Restored);
    }

    [Fact]
    public void MarkRestored_returns_false_for_an_unknown_batch()
    {
        var store = new JournalStore(_journal);
        Assert.False(store.MarkRestored(Guid.NewGuid(), ["/a"]));
    }

    [Fact]
    public void Prune_drops_batches_older_than_the_maximum_age()
    {
        var store = new JournalStore(_journal, maxAge: TimeSpan.FromDays(30));

        store.Append(Report(10), DeleteMode.Trash, [new JournalEntry("/old", 10, null)], DaysAgo(40));
        store.Append(Report(20), DeleteMode.Trash, [new JournalEntry("/new", 20, null)], MinutesAgo(1));

        var batch = Assert.Single(store.ReadRecent(10));
        Assert.Equal("/new", batch.Items[0].Path);
    }

    [Fact]
    public void Prune_keeps_only_the_newest_batches()
    {
        var store = new JournalStore(_journal, maxBatches: 2);

        store.Append(Report(10), DeleteMode.Trash, [new JournalEntry("/one", 10, null)], MinutesAgo(30));
        store.Append(Report(20), DeleteMode.Trash, [new JournalEntry("/two", 20, null)], MinutesAgo(20));
        store.Append(Report(30), DeleteMode.Trash, [new JournalEntry("/three", 30, null)], MinutesAgo(10));

        var recent = store.ReadRecent(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal("/three", recent[0].Items[0].Path);
        Assert.Equal("/two", recent[1].Items[0].Path);
    }

    [Fact]
    public void MarkTrashEmptied_disables_undo_only_for_batches_on_that_volume()
    {
        var store = new JournalStore(_journal);
        var first = Report(10);
        var second = Report(20);

        store.Append(first, DeleteMode.Trash, [new JournalEntry(@"C:\Temp\a.bin", 10, null)], MinutesAgo(5));
        store.Append(second, DeleteMode.Trash, [new JournalEntry(@"D:\Temp\b.bin", 20, null)], MinutesAgo(4));

        Assert.Equal(1, store.MarkTrashEmptied(@"C:\"));

        Assert.True(store.Find(first.BatchId)!.TrashEmptied);
        Assert.False(store.Find(first.BatchId)!.CanUndo);
        Assert.False(store.Find(second.BatchId)!.TrashEmptied);
        Assert.True(store.Find(second.BatchId)!.CanUndo);

        Assert.Equal(1, store.MarkTrashEmptied());
        Assert.True(store.Find(second.BatchId)!.TrashEmptied);
    }

    [Fact]
    public void Legacy_jsonl_history_is_readable_but_cannot_be_restored()
    {
        Directory.CreateDirectory(_journal);
        var legacyPath = Path.Combine(_journal, "2026-10.jsonl");
        File.WriteAllText(
            legacyPath,
            """
            {"v":1,"batchId":"11111111-1111-1111-1111-111111111111","at":"2026-10-30T00:00:00+00:00","mode":"permanent","deleted":2,"trashedBytes":0,"freedBytes":100,"skipped":{"0":1},"items":[{"p":"C:\\old\\a.bin","s":50},{"p":"C:\\old\\b.bin","s":50}]}
            """);

        var batch = Assert.Single(new JournalStore(_journal).ReadRecent());

        Assert.True(batch.Legacy);
        Assert.False(batch.CanUndo);
        Assert.Equal(0, batch.PendingRestoreCount);
        Assert.All(batch.Items, item => Assert.Null(item.TrashPath));
        Assert.Equal(2, batch.Items.Count);
    }

    [Fact]
    public void A_corrupt_batch_file_is_skipped_without_hiding_the_others()
    {
        var store = new JournalStore(_journal);
        store.Append(Report(10), DeleteMode.Trash, [new JournalEntry("/ok", 10, null)], MinutesAgo(5));

        var month = Path.Combine(_journal, "2026-11");
        Directory.CreateDirectory(month);
        File.WriteAllText(Path.Combine(month, "0000000000000000000-bad.json"), "{ this is not json");

        var batch = Assert.Single(store.ReadRecent());
        Assert.Equal("/ok", batch.Items[0].Path);
    }

    [Fact]
    public void Appending_leaves_no_temporary_files_behind()
    {
        var store = new JournalStore(_journal);
        store.Append(Report(10), DeleteMode.Trash, [new JournalEntry("/a", 10, null)], MinutesAgo(5));

        Assert.Empty(Directory.GetFiles(_journal, "*.tmp", SearchOption.AllDirectories));
    }

    private static DeletionReport Report(long trashedBytes = 0, int deleted = 1) => new(
        Guid.NewGuid(),
        deleted,
        trashedBytes,
        0,
        new Dictionary<SkipReason, int>(),
        TrashEmptied: false);

    private static DateTimeOffset MinutesAgo(int minutes) => DateTimeOffset.UtcNow.AddMinutes(-minutes);

    private static DateTimeOffset DaysAgo(int days) => DateTimeOffset.UtcNow.AddDays(-days);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_journal))
            {
                Directory.Delete(_journal, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
