using Velopack;
using Velopack.Sources;

namespace AirClean.App.Services;

/// <summary>
/// Thin wrapper around Velopack. Update checks only mean something in an installed build;
/// running from <c>bin</c> or a portable archive is treated as not installed, and the
/// manager is created lazily so tests never need a Velopack installation.
/// </summary>
public sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/RyzenWang/AirClean";

    private readonly Lazy<UpdateManager> _manager = new(
        () => new UpdateManager(new GithubSource(RepositoryUrl, null, false)));

    private UpdateManager Manager => _manager.Value;

    public bool IsInstalled
    {
        get
        {
            try
            {
                return Manager.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<UpdateInfo?> CheckForUpdatesAsync() =>
        IsInstalled ? Manager.CheckForUpdatesAsync() : Task.FromResult<UpdateInfo?>(null);

    public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken) =>
        Manager.DownloadUpdatesAsync(update, progress, cancellationToken);

    public void ApplyAndRestart(UpdateInfo update) =>
        Manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
}
