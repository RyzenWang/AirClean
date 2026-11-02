using AirClean.Core.Models;
using AirClean.Core.Volumes;

namespace AirClean.Core.Scanning;

/// <summary>
/// A read-only large-file scan that runs out of process with administrator rights. The
/// normal in-process MFT path is preferred; this is the fallback for an unelevated app.
/// </summary>
public interface IPrivilegedLargeFileScanner
{
    LargeFileScanResult? TryScan(
        VolumeInfo volume,
        long thresholdBytes,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>How a privileged scan hands its top-N result back to the ordinary process.</summary>
public sealed record LargeFileScanResult(
    IReadOnlyList<CleanItem> Items,
    long MatchedBytes,
    int MatchedCount);

/// <summary>
/// User-facing switch for the privileged scanner. Disabled by default; enabling it makes
/// the next scan ask Windows for elevation once.
/// </summary>
public interface IAdminHelper : IPrivilegedLargeFileScanner
{
    bool IsAvailable { get; }

    bool Enabled { get; set; }
}
