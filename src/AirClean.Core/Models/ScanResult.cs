namespace AirClean.Core.Models;

/// <summary>Everything one category contributed to a scan.</summary>
public sealed class CategoryScan
{
    public required CleanCategory Category { get; init; }

    public required Safety Safety { get; set; }

    public bool IsPermanent { get; init; }

    /// <summary>True when the user picked these files by hand (large files), not a rule.</summary>
    public bool IsExplicitSelection { get; init; }

    /// <summary>Volume whose recycle bin this row refers to, when it is a per-volume trash.</summary>
    public string? TrashRoot { get; init; }

    /// <summary>Set for fixed administrator-only rules; the elevated helper performs the cleanup.</summary>
    public string? AdminRuleId { get; init; }

    /// <summary>True for an action whose count is "1 cleanup" rather than a number of files.</summary>
    public bool IsAdminAction { get; init; }

    public long Bytes { get; set; }

    public int FileCount { get; set; }

    /// <summary>Everything that matched, when the list itself is capped (large files).</summary>
    public long MatchedBytes { get; set; }

    public int MatchedCount { get; set; }

    public bool Limited { get; set; }

    public List<CleanItem> Items { get; } = [];

    public void Add(CleanItem item)
    {
        Items.Add(item);
        Bytes += item.Size;
        FileCount++;
    }
}

public readonly record struct ScanProgress(
    long FilesSeen,
    long BytesFound,
    long ItemsFound,
    string? CurrentTarget);

public sealed class ScanResult
{
    public required IReadOnlyList<CategoryScan> Categories { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public int SkippedPermission { get; init; }

    public bool Canceled { get; init; }

    /// <summary>The scan stopped early (hash budget or item cap) and does not cover everything.</summary>
    public bool Limited { get; init; }

    /// <summary>Populated only by the duplicate scan.</summary>
    public IReadOnlyList<DuplicateGroup>? DuplicateGroups { get; init; }

    public long TotalBytes => Categories.Sum(category => category.Bytes);

    public int TotalCount => Categories.Sum(category => category.FileCount);

    public long TotalMatchedBytes => Categories.Sum(category => category.MatchedBytes > 0 ? category.MatchedBytes : category.Bytes);

    public int TotalMatchedCount => Categories.Sum(category => category.MatchedCount > 0 ? category.MatchedCount : category.FileCount);
}

public sealed record DeletionProgress(int Done, int Total, string? CurrentPath);

public sealed record DeletionReport(
    Guid BatchId,
    long DeletedCount,
    long TrashedBytes,
    long FreedBytes,
    IReadOnlyDictionary<SkipReason, int> Skipped,
    bool TrashEmptied,
    bool Canceled = false)
{
    public long TotalBytes => TrashedBytes + FreedBytes;
}
