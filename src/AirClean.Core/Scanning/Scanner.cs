using System.Diagnostics;
using System.IO.Enumeration;
using AirClean.Core.Models;
using AirClean.Core.Rules;
using AirClean.Core.Security;
using AirClean.Core.Trash;

namespace AirClean.Core.Scanning;

/// <summary>
/// Walks every rule root and aggregates matches into categories. The walker never follows
/// reparse points, never leaves a rule root, and de-duplicates paths so overlapping rules
/// cannot double count.
/// </summary>
public sealed class Scanner(RuleSet ruleSet, ITrashService trash)
{
    public const int MaxItems = 200_000;

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    private static readonly bool IgnoreCase = !OperatingSystem.IsLinux();

    public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private ScanResult Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var categories = CreateCategories();
        var seen = new HashSet<string>(PathComparer);
        var state = new ScanState();

        foreach (var rule in ruleSet.Rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var category = categories[rule.Category];

            if (rule.EmptyTrash)
            {
                var sample = trash.Probe();
                if (sample is { } trashSample)
                {
                    category.Bytes += trashSample.Bytes;
                    category.FileCount += trashSample.ItemCount;
                }

                continue;
            }

            foreach (var pattern in rule.Roots)
            {
                foreach (var root in PathExpander.Expand(pattern))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (PathGuard.IsProtected(root) || !Directory.Exists(root))
                    {
                        continue;
                    }

                    Walk(rule, root, category, seen, state, progress, cancellationToken);
                }
            }
        }

        Report(progress, state, force: true);
        stopwatch.Stop();

        return new ScanResult
        {
            Categories = categories.Values.OrderBy(category => (int)category.Category).ToArray(),
            Elapsed = stopwatch.Elapsed,
            SkippedPermission = state.SkippedPermission
        };
    }

    private Dictionary<CleanCategory, CategoryScan> CreateCategories()
    {
        var categories = new Dictionary<CleanCategory, CategoryScan>();

        foreach (var rule in ruleSet.Rules)
        {
            if (categories.TryGetValue(rule.Category, out var existing))
            {
                // A category is only as safe as its riskiest rule.
                if (rule.Safety == Safety.Caution)
                {
                    existing.Safety = Safety.Caution;
                }

                continue;
            }

            categories[rule.Category] = new CategoryScan
            {
                Category = rule.Category,
                Safety = rule.Safety,
                IsPermanent = rule.EmptyTrash
            };
        }

        return categories;
    }

    private void Walk(
        ScanRule rule,
        string root,
        CategoryScan category,
        HashSet<string> seen,
        ScanState state,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cutoff = rule.MinAgeDays > 0
            ? DateTime.UtcNow.AddDays(-rule.MinAgeDays)
            : DateTime.MinValue;

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (state.ItemsFound >= MaxItems)
            {
                state.Limited = true;
                category.Limited = true;
                return;
            }

            var (directory, depth) = pending.Pop();
            state.CurrentTarget = directory;

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
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        state.SkippedPermission++;
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if (entry is DirectoryInfo)
                    {
                        if (depth < rule.MaxDepth)
                        {
                            pending.Push((entry.FullName, depth + 1));
                        }

                        continue;
                    }

                    state.FilesSeen++;

                    var length = ReadLength(entry);
                    state.BytesSeen += length;

                    if (cutoff != DateTime.MinValue && entry.LastWriteTimeUtc > cutoff)
                    {
                        Report(progress, state, force: false);
                        continue;
                    }

                    if (!MatchesAny(entry.Name, rule.Include) || MatchesAny(entry.Name, rule.Exclude))
                    {
                        Report(progress, state, force: false);
                        continue;
                    }

                    if (!seen.Add(entry.FullName))
                    {
                        Report(progress, state, force: false);
                        continue;
                    }

                    category.Add(new CleanItem(entry.FullName, root, length, entry.LastWriteTimeUtc));
                    state.ItemsFound++;
                    state.BytesFound += length;

                    Report(progress, state, force: false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                state.SkippedPermission++;
            }
        }
    }

    private static long ReadLength(FileSystemInfo entry)
    {
        try
        {
            return entry is FileInfo file ? file.Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool MatchesAny(string name, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, name, IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Report(IProgress<ScanProgress>? progress, ScanState state, bool force)
    {
        var now = DateTime.UtcNow;

        if (!force && (now - state.LastReportUtc).TotalMilliseconds < 100)
        {
            return;
        }

        state.LastReportUtc = now;
        progress?.Report(new ScanProgress(state.FilesSeen, state.BytesFound, state.ItemsFound, state.CurrentTarget));
    }

    private sealed class ScanState
    {
        public long FilesSeen;
        public long BytesSeen;
        public long ItemsFound;
        public long BytesFound;
        public int SkippedPermission;
        public bool Limited;
        public string? CurrentTarget;
        public DateTime LastReportUtc = DateTime.MinValue;
    }
}
