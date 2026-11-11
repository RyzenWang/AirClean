using AirClean.Core.Models;
using AirClean.Core.Scanning;
using AirClean.Core.Security;
using AirClean.Core.Trash;

namespace AirClean.Core.Deleting;

/// <summary>
/// Deletes exactly the items that were scanned. Every item is re-checked first: still
/// under its rule root, still outside the protected list, unchanged since the scan and not
/// a link. Anything that fails becomes a skip reason instead of an exception.
/// </summary>
public sealed class DeletionService(
    ITrashService trash,
    JournalStore? journal = null,
    IAdminCleanup? adminCleanup = null,
    Func<string, long?>? freeSpaceProbe = null)
{
    private const int MaxJournalEntries = 20_000;
    private const int MaxSkippedSamples = 500;
    private const int FailureRateMinimum = 10;
    private const int FailureRatePercent = 30;

    private readonly JournalStore _journal = journal ?? new JournalStore();
    private readonly Func<string, long?> _freeSpace = freeSpaceProbe ?? DefaultFreeSpace;

    public JournalStore Journal => _journal;

    public DeletionReport Delete(
        IReadOnlyList<CategoryScan> categories,
        DeleteMode mode,
        IProgress<DeletionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();
        var skipped = new Dictionary<SkipReason, int>();
        var skippedSamples = new List<SkippedItem>();
        var entries = new List<JournalEntry>();
        var directories = new List<(string Path, string Root)>();
        var total = categories
            .Where(category => category.Category != CleanCategory.Trash)
            .Sum(category => category.AdminRuleId is not null ? category.FileCount : category.Items.Count);
        var done = 0;

        long deleted = 0;
        long trashedBytes = 0;
        long freedBytes = 0;
        var trashEmptied = false;
        var canceled = false;
        var aborted = false;
        var diskFull = false;
        var attempted = 0;
        var failed = 0;

        void RecordFailure()
        {
            failed++;

            if (attempted >= FailureRateMinimum && failed * 100 > attempted * FailureRatePercent)
            {
                aborted = true;
            }
        }

        try
        {
            foreach (var category in categories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (aborted)
                {
                    break;
                }

                if (category.Category == CleanCategory.Trash)
                {
                    freedBytes += EmptyTrash(category.TrashRoot);
                    trashEmptied = true;
                    continue;
                }

                if (category.AdminRuleId is { } adminRuleId)
                {
                    var adminReport = adminCleanup?.Clean(adminRuleId, progress, cancellationToken);

                    if (adminReport is null)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        Skip(skipped, skippedSamples, SkipReason.Permission, string.Empty);
                        continue;
                    }

                    freedBytes += adminReport.Bytes;
                    deleted += adminReport.FileCount;

                    if (adminReport.Skipped > 0)
                    {
                        skipped[SkipReason.Permission] = skipped.GetValueOrDefault(SkipReason.Permission) + adminReport.Skipped;
                    }

                    continue;
                }

                foreach (var item in category.Items)
                {
                    if (aborted)
                    {
                        break;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    var blocked = category.IsExplicitSelection
                        ? PathGuard.IsSystemProtected(item.Path)
                        : PathGuard.IsProtected(item.Path);

                    if (!PathGuard.IsUnder(item.Root, item.Path) || blocked)
                    {
                        Skip(skipped, skippedSamples, SkipReason.Protected, item.Path);
                        continue;
                    }

                    FileInfo info;
                    try
                    {
                        info = new FileInfo(item.Path);
                        if (!info.Exists)
                        {
                            Skip(skipped, skippedSamples, SkipReason.NotFound, item.Path);
                            continue;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        Skip(skipped, skippedSamples, SkipReason.Permission, item.Path);
                        continue;
                    }

                    try
                    {
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Skip(skipped, skippedSamples, SkipReason.ReparsePoint, item.Path);
                            continue;
                        }

                        if (info.Length != item.Size || info.LastWriteTimeUtc != item.LastWriteUtc)
                        {
                            Skip(skipped, skippedSamples, SkipReason.ChangedSinceScan, item.Path);
                            continue;
                        }

                        string? trashPath = null;

                        if (mode == DeleteMode.Permanent)
                        {
                            if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                            {
                                info.Attributes &= ~FileAttributes.ReadOnly;
                            }

                            attempted++;
                            info.Delete();
                            freedBytes += item.Size;
                        }
                        else
                        {
                            attempted++;
                            var moved = trash.MoveToTrash(item.Path);
                            if (!moved.Success)
                            {
                                RecordFailure();
                                Skip(skipped, skippedSamples, SkipReason.DeleteFailed, item.Path);

                                if (IsDiskFull(item.Path, item.Size))
                                {
                                    diskFull = true;
                                    aborted = true;
                                }

                                if (aborted)
                                {
                                    break;
                                }

                                continue;
                            }

                            trashedBytes += item.Size;
                            trashPath = moved.TrashPath;
                        }

                        deleted++;
                        if (entries.Count < MaxJournalEntries)
                        {
                            entries.Add(new JournalEntry(item.Path, item.Size, trashPath));
                        }

                        if (Path.GetDirectoryName(item.Path) is { Length: > 0 } directory)
                        {
                            directories.Add((directory, item.Root));
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        RecordFailure();
                        Skip(skipped, skippedSamples, SkipReason.Permission, item.Path);
                    }
                    catch (IOException)
                    {
                        RecordFailure();
                        Skip(skipped, skippedSamples, SkipReason.InUse, item.Path);
                    }

                    if (aborted)
                    {
                        break;
                    }

                    done++;
                    if (done % 16 == 0 || done == total)
                    {
                        progress?.Report(new DeletionProgress(done, total, item.Path));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled batch still reports what it already removed, so the window can
            // show an honest partial result instead of pretending nothing happened.
            canceled = true;
        }

        progress?.Report(new DeletionProgress(done, total, null));
        PruneEmptyDirectories(directories, cancellationToken);

        var skippedTotal = skipped.Values.Sum();
        var report = new DeletionReport(
            batchId,
            deleted,
            trashedBytes,
            freedBytes,
            skipped,
            trashEmptied,
            canceled,
            skippedSamples,
            skippedTotal > skippedSamples.Count,
            aborted,
            diskFull);
        _journal.Append(report, mode, entries);
        return report;
    }

    private bool IsDiskFull(string path, long size)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            var free = _freeSpace(root);
            return free is >= 0 && free < size;
        }
        catch
        {
            return false;
        }
    }

    private static long? DefaultFreeSpace(string root)
    {
        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    private long EmptyTrash(string? rootPath)
    {
        try
        {
            var before = trash.Probe(rootPath)?.Bytes ?? 0;
            trash.Empty(rootPath);
            var after = trash.Probe(rootPath)?.Bytes ?? 0;
            return Math.Max(0, before - after);
        }
        catch
        {
            return 0;
        }
    }

    private static void Count(Dictionary<SkipReason, int> skipped, SkipReason reason)
    {
        skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
    }

    private static void Skip(
        Dictionary<SkipReason, int> skipped,
        List<SkippedItem> samples,
        SkipReason reason,
        string path)
    {
        Count(skipped, reason);

        if (samples.Count < MaxSkippedSamples)
        {
            samples.Add(new SkippedItem(path, reason));
        }
    }

    private static void PruneEmptyDirectories(List<(string Path, string Root)> directories, CancellationToken cancellationToken)
    {
        foreach (var (path, root) in directories
                     .Distinct()
                     .OrderByDescending(entry => entry.Path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = path;

            while (current is not null && PathGuard.IsUnder(root, current))
            {
                try
                {
                    if (Directory.EnumerateFileSystemEntries(current).Any())
                    {
                        break;
                    }

                    Directory.Delete(current);
                }
                catch
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }
        }
    }
}
