using AirClean.Core.Deleting;
using AirClean.Core.Models;
using AirClean.Core.Scanning;
using AirClean.Core.Security;
using AirClean.Core.Trash;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class DeletionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "airclean-delete", Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "airclean-outside", Guid.NewGuid().ToString("N"));
    private readonly string _journal = Path.Combine(Path.GetTempPath(), "airclean-journal", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Permanent_mode_deletes_files_and_prunes_empty_directories()
    {
        var first = Write(_root, "a.tmp", 100);
        var second = Write(_root, Path.Combine("sub", "b.tmp"), 200);

        var report = Delete([BuildCategory((first, _root), (second, _root))], DeleteMode.Permanent);

        Assert.Equal(2, report.DeletedCount);
        Assert.Equal(300, report.FreedBytes);
        Assert.Equal(0, report.TrashedBytes);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.False(Directory.Exists(Path.Combine(_root, "sub")));
    }

    [Fact]
    public void Trash_mode_moves_files_and_counts_trashed_bytes()
    {
        var file = Write(_root, "cache.bin", 512);
        var trash = new TestTrash();

        var report = Delete([BuildCategory((file, _root))], DeleteMode.Trash, trash);

        Assert.Equal(1, report.DeletedCount);
        Assert.Equal(512, report.TrashedBytes);
        Assert.Single(trash.Moved);
    }

    [Fact]
    public void Paths_outside_the_rule_root_are_refused()
    {
        var file = Write(_outside, "secret.bin", 64);
        var report = Delete([BuildCategory((file, _root))], DeleteMode.Permanent);

        Assert.True(File.Exists(file));
        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(1, report.Skipped[SkipReason.Protected]);
    }

    [Fact]
    public void Files_changed_after_the_scan_are_skipped()
    {
        var file = Write(_root, "moving.bin", 128);
        var category = BuildCategory((file, _root));

        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));

        var report = Delete([category], DeleteMode.Permanent);

        Assert.True(File.Exists(file));
        Assert.Equal(1, report.Skipped[SkipReason.ChangedSinceScan]);
    }

    [Fact]
    public void Trash_category_empties_the_recycle_bin_and_reports_freed_bytes()
    {
        var trash = new TestTrash { Sample = new TrashSample(1_024, 3) };
        var category = new CategoryScan
        {
            Category = CleanCategory.Trash,
            Safety = Safety.Safe,
            IsPermanent = true,
            Bytes = 1_024,
            FileCount = 3
        };

        var report = Delete([category], DeleteMode.Trash, trash);

        Assert.True(report.TrashEmptied);
        Assert.Equal(1_024, report.FreedBytes);
    }

    [Fact]
    public void An_administrator_rule_is_cleaned_by_the_helper()
    {
        var category = new CategoryScan
        {
            Category = CleanCategory.SystemUpdates,
            Safety = Safety.Caution,
            IsPermanent = true,
            AdminRuleId = AdminRuleIds.WindowsUpdateCache,
            Bytes = 2_048,
            FileCount = 4
        };

        var helper = new FakeAdminCleanup
        {
            Report = new AdminCleanupReport(2_048, 4, 1)
        };

        var report = new DeletionService(new TestTrash(), new JournalStore(_journal), helper)
            .Delete([category], DeleteMode.Trash, null, CancellationToken.None);

        Assert.True(helper.Called);
        Assert.Equal(2_048, report.FreedBytes);
        Assert.Equal(4, report.DeletedCount);
        Assert.Equal(1, report.Skipped[SkipReason.Permission]);
    }

    [Fact]
    public void Skipped_items_record_a_sample_with_the_reason()
    {
        var file = Write(_root, "gone-by-delete.bin", 64);
        var category = BuildCategory((file, _root));
        File.Delete(file);

        var report = Delete([category], DeleteMode.Permanent);

        var item = Assert.Single(report.Items);
        Assert.Equal(file, item.Path);
        Assert.Equal(SkipReason.NotFound, item.Reason);
        Assert.False(report.SkippedTruncated);
    }

    [Fact]
    public void A_batch_aborts_when_more_than_thirty_percent_of_deletes_fail()
    {
        var files = Enumerable.Range(0, 12)
            .Select(index => Write(_root, $"fail-{index}.bin", 32))
            .ToArray();
        var trash = new TestTrash { FailMoves = true };

        var report = Delete([BuildCategory(files.Select(path => (path, _root)).ToArray())], DeleteMode.Trash, trash);

        Assert.True(report.Aborted);
        Assert.False(report.DiskFull);
        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(10, report.Skipped[SkipReason.DeleteFailed]);
        Assert.Equal(10, report.Items.Count);
    }

    [Fact]
    public void A_small_batch_does_not_abort_on_a_few_failures()
    {
        var files = Enumerable.Range(0, 5)
            .Select(index => Write(_root, $"few-{index}.bin", 32))
            .ToArray();
        var trash = new TestTrash { FailMoves = true };

        var report = Delete([BuildCategory(files.Select(path => (path, _root)).ToArray())], DeleteMode.Trash, trash);

        Assert.False(report.Aborted);
        Assert.Equal(5, report.Skipped[SkipReason.DeleteFailed]);
    }

    [Fact]
    public void A_disk_full_failure_aborts_with_the_disk_full_flag()
    {
        var files = Enumerable.Range(0, 12)
            .Select(index => Write(_root, $"full-{index}.bin", 32))
            .ToArray();
        var trash = new TestTrash { FailMoves = true };

        var report = new DeletionService(trash, new JournalStore(_journal), freeSpaceProbe: _ => 1)
            .Delete([BuildCategory(files.Select(path => (path, _root)).ToArray())], DeleteMode.Trash, null, CancellationToken.None);

        Assert.True(report.Aborted);
        Assert.True(report.DiskFull);
        Assert.Equal(1, report.Skipped[SkipReason.DeleteFailed]);
    }

    [Fact]
    public void Excluded_paths_are_refused()
    {
        var file = Write(_root, "keep-me.bin", 64);

        var report = new DeletionService(
                new TestTrash(),
                new JournalStore(_journal),
                exclusions: new ExclusionSet([_root]))
            .Delete([BuildCategory((file, _root))], DeleteMode.Permanent, null, CancellationToken.None);

        Assert.True(File.Exists(file));
        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(1, report.Skipped[SkipReason.Excluded]);
    }

    [Fact]
    public void A_journal_batch_is_written_for_every_cleanup()
    {
        var file = Write(_root, "a.tmp", 10);
        Delete([BuildCategory((file, _root))], DeleteMode.Permanent);

        var files = Directory.GetFiles(_journal, "*.json", SearchOption.AllDirectories);
        Assert.Single(files);
        var json = File.ReadAllText(files[0]);
        Assert.Contains("\"batchId\"", json);
        Assert.Contains("a.tmp", json);
    }

    [Fact]
    public void A_cancelled_batch_reports_what_it_already_removed()
    {
        var first = Write(_root, "a.tmp", 10);
        var second = Write(_root, "b.tmp", 10);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var report = new DeletionService(new TestTrash(), new JournalStore(_journal))
            .Delete([BuildCategory((first, _root), (second, _root))], DeleteMode.Permanent, null, cancellation.Token);

        Assert.True(report.Canceled);
        Assert.Equal(0, report.DeletedCount);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));

        // The journal still records the batch so the log stays truthful.
        Assert.Single(Directory.GetFiles(_journal, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void Trashed_items_record_the_trash_path_returned_by_the_platform()
    {
        var file = Write(_root, "cache.bin", 64);
        var trash = new TestTrash
        {
            TrashPathFactory = path => "/trash/" + Path.GetFileName(path)
        };

        Delete([BuildCategory((file, _root))], DeleteMode.Trash, trash);

        var batch = Assert.Single(new JournalStore(_journal).ReadRecent());
        var item = Assert.Single(batch.Items);
        Assert.Equal("/trash/cache.bin", item.TrashPath);
    }

    private DeletionReport Delete(IReadOnlyList<CategoryScan> categories, DeleteMode mode, TestTrash? trash = null) =>
        new DeletionService(trash ?? new TestTrash(), new JournalStore(_journal))
            .Delete(categories, mode, null, CancellationToken.None);

    private static CategoryScan BuildCategory(params (string Path, string Root)[] items)
    {
        var category = new CategoryScan
        {
            Category = CleanCategory.SystemTemp,
            Safety = Safety.Safe
        };

        foreach (var (path, root) in items)
        {
            var info = new FileInfo(path);
            category.Add(new CleanItem(path, root, info.Length, info.LastWriteTimeUtc));
        }

        return category;
    }

    private static string Write(string root, string relativePath, int size)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    private sealed class FakeAdminCleanup : IAdminCleanup
    {
        public AdminCleanupReport? Report { get; init; }

        public bool Called { get; private set; }

        public bool IsAvailable => true;

        public AdminScanResult? Scan(string ruleId, CancellationToken cancellationToken) => null;

        public AdminCleanupReport? Clean(
            string ruleId,
            IProgress<DeletionProgress>? progress,
            CancellationToken cancellationToken)
        {
            Called = true;
            return Report;
        }
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _root, _outside, _journal })
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
