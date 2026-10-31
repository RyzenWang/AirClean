using AirClean.Core.Platform;
using AirClean.Core.Trash;

namespace AirClean.Platform;

public static class PlatformServices
{
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
