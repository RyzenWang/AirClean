using AirClean.Core.Models;
using AirClean.Core.Volumes;

namespace AirClean.Core.Scanning;

/// <summary>
/// One volume scan that publishes categories as they finish. The interface exists so the
/// window can be tested with a scanner that cancels partway through.
/// </summary>
public interface IVolumeScanService
{
    Task<ScanResult> ScanAsync(
        VolumeScanRequest request,
        IProgress<VolumeScanProgress>? progress,
        IProgress<ScanUpdate>? updates,
        CancellationToken cancellationToken);
}
