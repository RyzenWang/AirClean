using System.Text.Json;
using AirClean.Core.Models;
using AirClean.Core.Security;
using AirClean.Core.Services;
using AirClean.Core.Trash;

namespace AirClean.Core.Deleting;

internal sealed record JournalEntry(string Path, long Size);

/// <summary>
/// Deletes exactly the items that were scanned. Every item is re-checked first: still
/// under its rule root, still outside the protected list, unchanged since the scan and not
/// a link. Anything that fails becomes a skip reason instead of an exception.
/// </summary>
public sealed class DeletionService(ITrashService trash, string? journalDirectory = null)
{
    private const int MaxJournalEntries = 20_000;

    private readonly string _journalDirectory = journalDirectory
        ?? Path.Combine(SettingsService.GetDefaultDirectory(), "journal");

    public DeletionReport Delete(
        IReadOnlyList<CategoryScan> categories,
        DeleteMode mode,
        IProgress<DeletionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();
        var skipped = new Dictionary<SkipReason, int>();
        var entries = new List<JournalEntry>();
        var directories = new List<(string Path, string Root)>();
        var total = categories.Where(category => category.Category != CleanCategory.Trash).Sum(category => category.Items.Count);
        var done = 0;

        long deleted = 0;
        long trashedBytes = 0;
        long freedBytes = 0;
        var trashEmptied = false;
        var canceled = false;

        try
        {
            foreach (var category in categories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (category.Category == CleanCategory.Trash)
                {
                    freedBytes += EmptyTrash(category.TrashRoot);
                    trashEmptied = true;
                    continue;
                }

                foreach (var item in category.Items)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    var blocked = category.IsExplicitSelection
                        ? PathGuard.IsSystemProtected(item.Path)
                        : PathGuard.IsProtected(item.Path);

                    if (!PathGuard.IsUnder(item.Root, item.Path) || blocked)
                    {
                        Count(skipped, SkipReason.Protected);
                        continue;
                    }

                    FileInfo info;
                    try
                    {
                        info = new FileInfo(item.Path);
                        if (!info.Exists)
                        {
                            Count(skipped, SkipReason.NotFound);
                            continue;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        Count(skipped, SkipReason.Permission);
                        continue;
                    }

                    try
                    {
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Count(skipped, SkipReason.ReparsePoint);
                            continue;
                        }

                        if (info.Length != item.Size || info.LastWriteTimeUtc != item.LastWriteUtc)
                        {
                            Count(skipped, SkipReason.ChangedSinceScan);
                            continue;
                        }

                        if (mode == DeleteMode.Permanent)
                        {
                            if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                            {
                                info.Attributes &= ~FileAttributes.ReadOnly;
                            }

                            info.Delete();
                            freedBytes += item.Size;
                        }
                        else
                        {
                            if (!trash.MoveToTrash(item.Path, out _))
                            {
                                Count(skipped, SkipReason.Permission);
                                continue;
                            }

                            trashedBytes += item.Size;
                        }

                        deleted++;
                        if (entries.Count < MaxJournalEntries)
                        {
                            entries.Add(new JournalEntry(item.Path, item.Size));
                        }

                        if (Path.GetDirectoryName(item.Path) is { Length: > 0 } directory)
                        {
                            directories.Add((directory, item.Root));
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Count(skipped, SkipReason.Permission);
                    }
                    catch (IOException)
                    {
                        Count(skipped, SkipReason.InUse);
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

        var report = new DeletionReport(batchId, deleted, trashedBytes, freedBytes, skipped, trashEmptied, canceled);
        JournalWriter.Append(_journalDirectory, batchId, mode, report, entries);
        return report;
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

internal static class JournalWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Append(string directory, Guid batchId, DeleteMode mode, DeletionReport report, IReadOnlyList<JournalEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyy-MM") + ".jsonl");
            var payload = new
            {
                v = 1,
                batchId,
                at = DateTimeOffset.UtcNow,
                mode = mode.ToString().ToLowerInvariant(),
                deleted = report.DeletedCount,
                trashedBytes = report.TrashedBytes,
                freedBytes = report.FreedBytes,
                skipped = report.Skipped,
                items = entries
            };

            File.AppendAllText(path, JsonSerializer.Serialize(payload, Options) + Environment.NewLine);
        }
        catch
        {
            // The journal is a convenience; losing it must never fail a cleanup.
        }
    }
}
