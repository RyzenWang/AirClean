using AirClean.Core.Models;
using AirClean.Core.Rules;
using AirClean.Core.Trash;
using AirClean.Core.Volumes;

namespace AirClean.Core.Scanning;

public enum ScanStage
{
    Caches,
    LargeFiles,
    Duplicates
}

/// <summary>One category finished; the UI can show it while the rest of the scan continues.</summary>
public sealed record ScanUpdate(
    ScanStage Stage,
    CategoryScan? Category,
    IReadOnlyList<DuplicateGroup>? Groups);

public readonly record struct VolumeScanProgress(
    ScanStage Stage,
    long FilesSeen,
    long BytesSeen,
    long ItemsFound,
    string? Current);

public sealed record VolumeScanRequest(
    VolumeInfo Volume,
    IReadOnlyList<string> DuplicateRoots,
    bool IncludeCaches = true,
    bool IncludeLargeFiles = true,
    bool IncludeDuplicates = true,
    long LargeFileThreshold = LargeFileScanner.DefaultThresholdBytes,
    long DuplicateHashBudget = DuplicateScanner.DefaultHashBudgetBytes);

/// <summary>
/// One scan per volume that produces every applicable category: cache rules on the system
/// volume, large files on any volume, the volume's trash, and duplicates. Categories are
/// published as they finish so the window fills in progressively instead of waiting for
/// the slowest hash to complete.
/// </summary>
public sealed class VolumeScanService(
    RuleSet ruleSet,
    ITrashService trash,
    IAdminHelper? adminHelper = null) : IVolumeScanService
{
    private readonly Scanner _rules = new(ruleSet, trash);
    private readonly LargeFileScanner _largeFiles = new(trash, adminHelper);
    private readonly DuplicateScanner _duplicates = new();
    private readonly IAdminCleanup? _adminCleanup = adminHelper;

    public async Task<ScanResult> ScanAsync(
        VolumeScanRequest request,
        IProgress<VolumeScanProgress>? progress,
        IProgress<ScanUpdate>? updates,
        CancellationToken cancellationToken)
    {
        var categories = new List<CategoryScan>();
        var groups = new List<DuplicateGroup>();
        var started = DateTime.UtcNow;
        var limited = false;
        var limitedByGroupCap = false;
        var duplicateGroupCount = 0;
        var skipped = 0;

        // Cheap categories first: the window is useful within a second or two.
        if (request.IncludeCaches && request.Volume.IsSystem)
        {
            var stageProgress = new Progress<ScanProgress>(value => progress?.Report(
                new VolumeScanProgress(ScanStage.Caches, value.FilesSeen, value.BytesFound, value.ItemsFound, value.CurrentTarget)));

            var rulesResult = await _rules.ScanAsync(stageProgress, cancellationToken);
            categories.AddRange(rulesResult.Categories);
            skipped += rulesResult.SkippedPermission;

            foreach (var category in rulesResult.Categories)
            {
                updates?.Report(new ScanUpdate(ScanStage.Caches, category, null));
            }
        }

        if (request.IncludeCaches && request.Volume.IsSystem && _adminCleanup is not null &&
            AdminRuleIds.SystemUpdatesForCurrentPlatform is { } adminRuleId)
        {
            var admin = await Task.Run(
                () => _adminCleanup.Scan(adminRuleId, cancellationToken),
                cancellationToken);

            if (admin is { FileCount: > 0 })
            {
                var adminCategory = new CategoryScan
                {
                    Category = CleanCategory.SystemUpdates,
                    Safety = Safety.Caution,
                    IsPermanent = true,
                    AdminRuleId = admin.RuleId,
                    Bytes = admin.Bytes,
                    FileCount = admin.FileCount
                };

                categories.Add(adminCategory);
                updates?.Report(new ScanUpdate(ScanStage.Caches, adminCategory, null));
            }
        }

        if (request.IncludeCaches && request.Volume.IsSystem && _adminCleanup is not null &&
            AdminRuleIds.SystemLogsForCurrentPlatform is { } logRuleId)
        {
            var logs = await Task.Run(
                () => _adminCleanup.Scan(logRuleId, cancellationToken),
                cancellationToken);

            if (logs is { FileCount: > 0 })
            {
                var logCategory = new CategoryScan
                {
                    Category = CleanCategory.SystemLogs,
                    Safety = Safety.Caution,
                    IsPermanent = true,
                    AdminRuleId = logs.RuleId,
                    IsAdminAction = true,
                    Bytes = logs.Bytes,
                    FileCount = logs.FileCount
                };

                categories.Add(logCategory);
                updates?.Report(new ScanUpdate(ScanStage.Caches, logCategory, null));
            }
        }

        if (request.IncludeLargeFiles)
        {
            var stageProgress = new Progress<ScanProgress>(value => progress?.Report(
                new VolumeScanProgress(ScanStage.LargeFiles, value.FilesSeen, value.BytesFound, value.ItemsFound, value.CurrentTarget)));

            var largeResult = await _largeFiles.ScanAsync(
                request.Volume,
                request.LargeFileThreshold,
                stageProgress,
                cancellationToken);

            foreach (var category in largeResult.Categories)
            {
                // The cache rules already report the trash on a system volume; do not add
                // a second row for the same recycle bin.
                if (category.Category == CleanCategory.Trash &&
                    categories.Any(existing => existing.Category == CleanCategory.Trash))
                {
                    continue;
                }

                categories.Add(category);
                updates?.Report(new ScanUpdate(ScanStage.LargeFiles, category, null));
            }

            skipped += largeResult.SkippedPermission;
        }
        else if (!categories.Any(category => category.Category == CleanCategory.Trash))
        {
            var trashScan = new CategoryScan
            {
                Category = CleanCategory.Trash,
                Safety = Safety.Safe,
                IsPermanent = true,
                TrashRoot = request.Volume.Root
            };

            if (trash.Probe(request.Volume.Root) is { } sample)
            {
                trashScan.Bytes = sample.Bytes;
                trashScan.FileCount = sample.ItemCount;
            }

            categories.Add(trashScan);
            updates?.Report(new ScanUpdate(ScanStage.LargeFiles, trashScan, null));
        }

        if (request.IncludeDuplicates && request.DuplicateRoots.Count > 0)
        {
            var stageProgress = new Progress<DuplicateScanProgress>(value => progress?.Report(
                new VolumeScanProgress(ScanStage.Duplicates, value.FilesSeen, value.BytesHashed, value.GroupsFound, value.CurrentPath)));

            var duplicateResult = await _duplicates.ScanAsync(
                request.DuplicateRoots,
                request.DuplicateHashBudget,
                stageProgress,
                cancellationToken);

            var duplicateCategory = duplicateResult.Categories.FirstOrDefault();
            if (duplicateCategory is not null)
            {
                categories.Add(duplicateCategory);
                groups.AddRange(duplicateResult.DuplicateGroups ?? []);
                updates?.Report(new ScanUpdate(ScanStage.Duplicates, duplicateCategory, duplicateResult.DuplicateGroups));
            }

            limited |= duplicateResult.Limited;
            limitedByGroupCap |= duplicateResult.LimitedByGroupCap;
            duplicateGroupCount = duplicateResult.DuplicateGroupCount;
        }

        return new ScanResult
        {
            Categories = categories,
            Elapsed = DateTime.UtcNow - started,
            SkippedPermission = skipped,
            Limited = limited,
            LimitedByGroupCap = limitedByGroupCap,
            DuplicateGroupCount = duplicateGroupCount,
            DuplicateGroups = groups
        };
    }
}
