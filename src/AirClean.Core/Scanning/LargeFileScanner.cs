using System.Diagnostics;
using AirClean.Core.Models;
using AirClean.Core.Scanning.Mft;
using AirClean.Core.Security;
using AirClean.Core.Trash;
using AirClean.Core.Volumes;

namespace AirClean.Core.Scanning;

/// <summary>
/// Finds the largest files on one volume. Unlike cache rules this deliberately looks inside
/// user folders, because the user picks the files themselves — nothing is pre-selected.
/// </summary>
public sealed class LargeFileScanner(ITrashService trash)
{
    public const long DefaultThresholdBytes = 100L * 1024 * 1024;
    public const int MaxItems = 200;

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        "System Volume Information",
        "$RECYCLE.BIN",
        ".Trash",
        ".Trashes"
    };

    private static readonly HashSet<string> SkippedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys",
        "hiberfil.sys",
        "swapfile.sys",
        "DumpStack.log",
        "DumpStack.log.tmp"
    };

    public Task<ScanResult> ScanAsync(
        VolumeInfo volume,
        long thresholdBytes,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => Scan(volume, thresholdBytes, progress, cancellationToken), cancellationToken);

    private ScanResult Scan(
        VolumeInfo volume,
        long thresholdBytes,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // NTFS exposes the whole file index without walking directories. It needs raw
        // volume access (elevation); when that is unavailable this returns false silently
        // and the normal walk below takes over.
        if (MftLargeFileScanner.TryScan(volume, thresholdBytes, progress, cancellationToken, out var mft) &&
            mft is not null)
        {
            var mftCategory = new CategoryScan
            {
                Category = CleanCategory.LargeFiles,
                Safety = Safety.Caution,
                IsExplicitSelection = true,
                MatchedBytes = mft.MatchedBytes,
                MatchedCount = mft.MatchedCount
            };

            foreach (var item in mft.Items)
            {
                mftCategory.Add(item);
            }

            stopwatch.Stop();

            return new ScanResult
            {
                Categories = [mftCategory, ScanTrash(volume)],
                Elapsed = stopwatch.Elapsed
            };
        }

        var candidates = new List<CleanItem>();
        long filesSeen = 0;
        long matchedBytes = 0;
        var skipped = 0;
        var lastReport = DateTime.MinValue;
        string? current = null;

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((volume.Root, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            current = directory;

            if (depth > 24)
            {
                continue;
            }

            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    AttributesToSkip = 0,
                    ReturnSpecialDirectories = false
                };

                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FileAttributes attributes;
                    try
                    {
                        attributes = entry.Attributes;
                    }
                    catch
                    {
                        skipped++;
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if (entry is DirectoryInfo)
                    {
                        if (!SkippedDirectoryNames.Contains(entry.Name) &&
                            !PathGuard.IsSystemProtected(entry.FullName))
                        {
                            pending.Push((entry.FullName, depth + 1));
                        }

                        continue;
                    }

                    filesSeen++;

                    long length;
                    try
                    {
                        length = ((FileInfo)entry).Length;
                    }
                    catch
                    {
                        continue;
                    }

                    if (length < thresholdBytes || SkippedFileNames.Contains(entry.Name))
                    {
                        continue;
                    }

                    candidates.Add(new CleanItem(entry.FullName, volume.Root, length, entry.LastWriteTimeUtc));
                    matchedBytes += length;

                    var now = DateTime.UtcNow;
                    if ((now - lastReport).TotalMilliseconds >= 120)
                    {
                        lastReport = now;
                        progress?.Report(new ScanProgress(filesSeen, matchedBytes, candidates.Count, current));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        var top = candidates
            .OrderByDescending(item => item.Size)
            .Take(MaxItems)
            .ToList();

        var largeFiles = new CategoryScan
        {
            Category = CleanCategory.LargeFiles,
            Safety = Safety.Caution,
            IsExplicitSelection = true,
            MatchedBytes = matchedBytes,
            MatchedCount = candidates.Count
        };

        foreach (var item in top)
        {
            largeFiles.Add(item);
        }

        progress?.Report(new ScanProgress(filesSeen, matchedBytes, candidates.Count, current));
        stopwatch.Stop();

        return new ScanResult
        {
            Categories = [largeFiles, ScanTrash(volume)],
            Elapsed = stopwatch.Elapsed,
            SkippedPermission = skipped
        };
    }

    private CategoryScan ScanTrash(VolumeInfo volume)
    {
        var trashSample = trash.Probe(volume.Root);
        var trashCategory = new CategoryScan
        {
            Category = CleanCategory.Trash,
            Safety = Safety.Safe,
            IsPermanent = true,
            TrashRoot = volume.Root
        };

        if (trashSample is { } sample)
        {
            trashCategory.Bytes = sample.Bytes;
            trashCategory.FileCount = sample.ItemCount;
        }

        return trashCategory;
    }
}
