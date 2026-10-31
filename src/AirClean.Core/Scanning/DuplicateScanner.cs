using System.Diagnostics;
using System.Security.Cryptography;
using AirClean.Core.Models;

namespace AirClean.Core.Scanning;

/// <summary>
/// Finds byte-identical files: bucket by exact size, compare the first 64 KB, then hash
/// the whole file only for the survivors. Everything below <see cref="MinFileSize"/> is
/// ignored — the payoff is not worth the noise.
///
/// Hashing is capped so a scan folded into a normal volume scan stays bounded; when the
/// cap is hit the result reports <see cref="ScanResult.Limited"/> instead of silently
/// pretending it looked at everything.
/// </summary>
public sealed class DuplicateScanner
{
    public const long MinFileSize = 1024 * 1024;
    public const int MaxGroups = 200;
    public const long DefaultHashBudgetBytes = 8L * 1024 * 1024 * 1024;

    private const int QuickHashBytes = 64 * 1024;
    private const int MaxDepth = 16;

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    public Task<ScanResult> ScanAsync(
        IReadOnlyList<string> roots,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken) =>
        ScanAsync(roots, DefaultHashBudgetBytes, progress, cancellationToken);

    public Task<ScanResult> ScanAsync(
        IReadOnlyList<string> roots,
        long hashBudgetBytes,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => Scan(roots, hashBudgetBytes, progress, cancellationToken), cancellationToken);

    private ScanResult Scan(
        IReadOnlyList<string> roots,
        long hashBudgetBytes,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var bySize = new Dictionary<long, List<string>>();
        var seen = new HashSet<string>(PathComparer);
        var filesSeen = 0;
        var lastReport = DateTime.MinValue;
        string? current = null;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(root))
            {
                continue;
            }

            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root, 0));

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, depth) = pending.Pop();
                current = directory;

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

                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if (entry is DirectoryInfo)
                        {
                            if (depth < MaxDepth &&
                                !string.Equals(entry.Name, "node_modules", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(entry.Name, ".git", StringComparison.OrdinalIgnoreCase))
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

                        if (length < MinFileSize || !seen.Add(entry.FullName))
                        {
                            continue;
                        }

                        if (!bySize.TryGetValue(length, out var list))
                        {
                            list = [];
                            bySize[length] = list;
                        }

                        list.Add(entry.FullName);

                        var now = DateTime.UtcNow;
                        if ((now - lastReport).TotalMilliseconds >= 120)
                        {
                            lastReport = now;
                            progress?.Report(new DuplicateScanProgress(filesSeen, 0, 0, 0, current));
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // An unreadable directory simply contributes nothing.
                }
            }
        }

        var candidates = bySize.Where(pair => pair.Value.Count > 1).ToList();
        var groups = new List<DuplicateGroup>();
        long bytesHashed = 0;
        var budgetExceeded = false;

        foreach (var (size, paths) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var byQuickHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (bytesHashed >= hashBudgetBytes)
                {
                    budgetExceeded = true;
                    break;
                }

                var quick = QuickHash(path);
                bytesHashed += QuickHashBytes;

                if (quick is null)
                {
                    continue;
                }

                if (!byQuickHash.TryGetValue(quick, out var list))
                {
                    list = [];
                    byQuickHash[quick] = list;
                }

                list.Add(path);
            }

            foreach (var bucket in byQuickHash.Values.Where(list => list.Count > 1))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var byFullHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);

                foreach (var path in bucket)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (bytesHashed >= hashBudgetBytes)
                    {
                        budgetExceeded = true;
                        break;
                    }

                    var hash = FullHash(path);
                    bytesHashed += size;

                    if (hash is null)
                    {
                        continue;
                    }

                    if (!byFullHash.TryGetValue(hash, out var list))
                    {
                        list = [];
                        byFullHash[hash] = list;
                    }

                    list.Add(path);
                }

                foreach (var (hash, duplicates) in byFullHash)
                {
                    if (duplicates.Count < 2)
                    {
                        continue;
                    }

                    var files = duplicates
                        .Select(path => new DuplicateFile(path, size, SafeLastWrite(path)))
                        .OrderBy(file => file.LastWriteUtc)
                        .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    groups.Add(new DuplicateGroup
                    {
                        Key = $"{hash}:{size}",
                        FileSize = size,
                        Files = files
                    });
                }

                progress?.Report(new DuplicateScanProgress(filesSeen, paths.Count, bytesHashed, groups.Count, current));
            }
        }

        var top = groups
            .OrderByDescending(group => group.RemovableBytes)
            .Take(MaxGroups)
            .ToList();

        var category = new CategoryScan
        {
            Category = CleanCategory.Duplicates,
            Safety = Safety.Caution,
            IsExplicitSelection = true,
            MatchedCount = top.Sum(group => group.Removable.Count),
            MatchedBytes = top.Sum(group => group.RemovableBytes)
        };

        foreach (var group in top)
        {
            foreach (var file in group.Removable)
            {
                var root = Path.GetPathRoot(file.Path) ?? "/";
                category.Add(new CleanItem(file.Path, root, file.Size, file.LastWriteUtc));
            }
        }

        progress?.Report(new DuplicateScanProgress(filesSeen, candidates.Sum(pair => pair.Value.Count), bytesHashed, top.Count, null));
        stopwatch.Stop();

        return new ScanResult
        {
            Categories = [category],
            DuplicateGroups = top,
            Elapsed = stopwatch.Elapsed,
            Limited = budgetExceeded
        };
    }

    private static string? QuickHash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, QuickHashBytes, FileOptions.SequentialScan);
            var buffer = new byte[QuickHashBytes];
            var read = stream.Read(buffer, 0, buffer.Length);
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
        }
        catch
        {
            return null;
        }
    }

    private static string? FullHash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return null;
        }
    }

    private static DateTime SafeLastWrite(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}
