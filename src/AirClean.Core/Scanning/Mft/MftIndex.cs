using AirClean.Core.Models;
using AirClean.Core.Security;
using AirClean.Core.Volumes;

namespace AirClean.Core.Scanning.Mft;

internal sealed record MftLargeFileResult(
    IReadOnlyList<CleanItem> Items,
    long MatchedBytes,
    int MatchedCount);

/// <summary>
/// Turns parsed MFT records into volume paths and keeps the largest files. The MFT gives us
/// record numbers, parent record numbers and names, so a path is a parent-chain walk rather
/// than another directory enumeration.
/// </summary>
internal sealed class MftIndex
{
    private const int MaxDepth = 256;

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        "$RECYCLE.BIN",
        "$Extend",
        "System Volume Information",
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

    private readonly Dictionary<uint, MftEntry> _entries;

    public MftIndex(Dictionary<uint, MftEntry> entries)
    {
        _entries = entries;
    }

    public MftLargeFileResult FindLargeFiles(
        VolumeInfo volume,
        long thresholdBytes,
        CancellationToken cancellationToken)
    {
        var root = EnsureTrailingSeparator(volume.Root);
        var matches = new List<CleanItem>();
        long matchedBytes = 0;
        var matchedCount = 0;

        foreach (var entry in _entries.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!entry.InUse ||
                entry.IsExtension ||
                entry.IsDirectory ||
                entry.IsReparsePoint ||
                entry.RecordNumber < 16 ||
                entry.Size < thresholdBytes)
            {
                continue;
            }

            var path = TryBuildPath(entry, root);

            if (path is null ||
                IsSkipped(path, entry.Name) ||
                PathGuard.IsSystemProtected(path))
            {
                continue;
            }

            matches.Add(new CleanItem(path, volume.Root, entry.Size, entry.LastWriteUtc));
            matchedBytes += entry.Size;
            matchedCount++;
        }

        var top = matches
            .OrderByDescending(item => item.Size)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Take(LargeFileScanner.MaxItems)
            .ToList();

        return new MftLargeFileResult(top, matchedBytes, matchedCount);
    }

    private string? TryBuildPath(MftEntry entry, string root)
    {
        var parts = new List<string>(8);
        var current = entry;

        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (current.RecordNumber == 5)
            {
                if (parts.Count == 0)
                {
                    return null;
                }

                parts.Reverse();
                var path = root + string.Join(Path.DirectorySeparatorChar, parts);

                return path.Length <= 32_767 ? path : null;
            }

            if (!current.InUse ||
                current.IsReparsePoint ||
                string.IsNullOrEmpty(current.Name) ||
                current.Name is "." or ".." ||
                current.Name.IndexOfAny(['\\', '/']) >= 0)
            {
                return null;
            }

            parts.Add(current.Name);

            if (!_entries.TryGetValue(current.ParentRecord, out var parent) || !parent.IsDirectory)
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    private static bool IsSkipped(string path, string fileName)
    {
        if (SkippedFileNames.Contains(fileName))
        {
            return true;
        }

        foreach (var segment in path.Split(Path.DirectorySeparatorChar))
        {
            if (SkippedDirectoryNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    private static string EnsureTrailingSeparator(string root) =>
        root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
}
