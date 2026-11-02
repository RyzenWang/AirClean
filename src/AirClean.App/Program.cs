using AirClean.Platform.Admin;
using Avalonia;
using Velopack;

namespace AirClean.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsWindows() && AdminHelperHost.IsHelperCommand(args))
        {
            Environment.ExitCode = AdminHelperHost.Run(args);
            return;
        }

        // Velopack must handle install/update hooks before any UI starts.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

#if DEBUG
        builder.LogToTrace();
#endif

        return builder;
    }
}
