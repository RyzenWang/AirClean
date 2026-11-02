using AirClean.Core.Models;

namespace AirClean.Core.Scanning;

/// <summary>Fixed rule IDs the elevated helper is allowed to act on.</summary>
public static class AdminRuleIds
{
    public const string WindowsUpdateCache = "win.update-cache";
    public const string LinuxAptCache = "linux.apt-cache";

    public static string? SystemUpdatesForCurrentPlatform =>
        OperatingSystem.IsWindows()
            ? WindowsUpdateCache
            : OperatingSystem.IsLinux()
                ? LinuxAptCache
                : null;
}

/// <summary>
/// A read-only scan and a permanent cleanup for fixed administrator-only locations.
/// The helper maps a rule ID to its own hard-coded path; callers never pass a path.
/// </summary>
public interface IAdminCleanup
{
    bool IsAvailable { get; }

    AdminScanResult? Scan(string ruleId, CancellationToken cancellationToken);

    AdminCleanupReport? Clean(
        string ruleId,
        IProgress<DeletionProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed record AdminScanResult(string RuleId, long Bytes, int FileCount);

public sealed record AdminCleanupReport(long Bytes, int FileCount, int Skipped);
