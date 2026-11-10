using System.Text;
using AirClean.Core.Services;

namespace AirClean.App;

/// <summary>
/// Records unhandled exceptions to a bounded local file. A WinExe that crashes otherwise
/// leaves nothing behind, and a user report without a stack is hard to act on. Nothing is
/// uploaded and the log never contains file contents.
/// </summary>
internal static class CrashLog
{
    internal const long MaxBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static string? _path;

    public static string Path => _path ??= System.IO.Path.Combine(
        SettingsService.GetDefaultDirectory(),
        "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
            Write("TaskScheduler.UnobservedTaskException", args.Exception);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Environment.ExitCode != 0)
            {
                Write($"ProcessExit({Environment.ExitCode})", null);
            }
        };
    }

    public static void Write(string source, Exception? exception) => WriteTo(Path, source, exception);

    internal static void WriteTo(string file, string source, Exception? exception)
    {
        lock (Gate)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var rotated = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(file) ?? string.Empty,
                        System.IO.Path.GetFileNameWithoutExtension(file) + ".1" + System.IO.Path.GetExtension(file));
                    File.Move(file, rotated, overwrite: true);
                }

                var version = typeof(CrashLog).Assembly.GetName().Version?.ToString() ?? "unknown";
                var text = new StringBuilder()
                    .Append(DateTimeOffset.UtcNow.ToString("O"))
                    .Append(" v").Append(version)
                    .Append(' ').Append(Environment.OSVersion.VersionString)
                    .Append(' ').Append(source)
                    .AppendLine()
                    .AppendLine(exception?.ToString() ?? "(no exception object)")
                    .ToString();

                File.AppendAllText(file, text);
            }
            catch
            {
                // Logging must never become a second failure.
            }
        }
    }
}
