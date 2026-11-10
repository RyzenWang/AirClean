using AirClean.App.Services;
using AirClean.Platform.Admin;
using Avalonia;
using Velopack;

namespace AirClean.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) &&
            AdminHelperHost.IsHelperCommand(args))
        {
            Environment.ExitCode = AdminHelperHost.Run(args);
            return;
        }

        // Crash logging must exist before anything else can fail; Velopack hooks must run
        // before any window is created.
        CrashLog.Install();
        VelopackApp.Build().Run();

        using var singleInstance = SingleInstance.Acquire();
        if (!singleInstance.IsFirst)
        {
            singleInstance.SignalExisting();
            return;
        }

        App.SharedInstance = singleInstance;
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
