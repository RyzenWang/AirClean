using AirClean.Core.Platform;
using AirClean.Core.Trash;

namespace AirClean.Platform;

public static class PlatformServices
{
    /// <summary>
    /// True when the app was installed from the Microsoft Store. Updates come from the Store
    /// and the elevation helper is unavailable, so the app skips both instead of failing.
    /// </summary>
    public static bool IsStoreManaged => OperatingSystem.IsWindows() && Windows.WindowsPackageIdentity.IsPackaged;

    public static ITrashService CreateTrashService()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Windows.WindowsTrashService();
        }

        return new Unix.UnixTrashService();
    }

    public static IRevealService CreateRevealService() => new Shell.ShellRevealService();
}
