using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AirClean.Core.Models;
using AirClean.Core.Scanning;
using AirClean.Core.Scanning.Mft;
using AirClean.Core.Volumes;

namespace AirClean.Platform.Admin;

/// <summary>
/// The elevated half of the administrator helper. AirClean re-launches itself with
/// <c>--admin-helper</c> through ShellExecute's <c>runas</c> verb; this process connects
/// back to a one-shot named pipe and performs a read-only MFT scan. It never accepts a
/// path list or deletes anything.
/// </summary>
public static class AdminHelperHost
{
    public const string Switch = "--admin-helper";
    public const string ScanLargeFilesCommand = "scan-large-files";

    public static bool IsHelperCommand(IReadOnlyList<string> args) =>
        args.Any(arg => string.Equals(arg, Switch, StringComparison.Ordinal));

    [SupportedOSPlatform("windows")]
    public static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 2;
        }

        var pipeName = GetOption(args, "--pipe");
        var token = GetOption(args, "--token");

        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(token))
        {
            return 2;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(30_000);

            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true
            };

            var requestLine = reader.ReadLine();

            if (requestLine is null)
            {
                return 3;
            }

            var request = JsonSerializer.Deserialize<AdminRequest>(requestLine, AdminJson.Options);

            if (request is null ||
                !TokensEqual(token, request.Token) ||
                !string.Equals(request.Command, ScanLargeFilesCommand, StringComparison.Ordinal))
            {
                WriteError(writer, "Invalid helper request.");
                return 3;
            }

            if (!IsAdministrator())
            {
                WriteError(writer, "The helper is not running elevated.");
                return 3;
            }

            var root = NormalizeVolumeRoot(request.VolumeRoot);

            if (root is null)
            {
                WriteError(writer, "The helper only scans a drive root.");
                return 3;
            }

            var volume = new VolumeInfo(root, string.Empty, "NTFS", 0, 0, IsSystem: false, IsRemovable: false);

            var progress = new InlineProgress<ScanProgress>(value => writer.WriteLine(JsonSerializer.Serialize(
                new AdminEvent
                {
                    Kind = "progress",
                    FilesSeen = value.FilesSeen,
                    MatchedBytes = value.BytesFound,
                    MatchedCount = (int)Math.Min(value.ItemsFound, int.MaxValue),
                    Message = value.CurrentTarget
                },
                AdminJson.Options)));

            var usedMft = MftLargeFileScanner.TryScan(
                volume,
                Math.Max(0, request.ThresholdBytes),
                progress,
                CancellationToken.None,
                out var result);

            if (!usedMft || result is null)
            {
                WriteError(writer, "The MFT fast path was unavailable.");
                return 1;
            }

            writer.WriteLine(JsonSerializer.Serialize(
                new AdminEvent
                {
                    Kind = "result",
                    MatchedBytes = result.MatchedBytes,
                    MatchedCount = result.MatchedCount,
                    Items = result.Items
                        .Select(item => new AdminFile
                        {
                            Path = item.Path,
                            Size = item.Size,
                            LastWriteUtc = item.LastWriteUtc
                        })
                        .ToList()
                },
                AdminJson.Options));

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static void WriteError(StreamWriter writer, string message)
    {
        writer.WriteLine(JsonSerializer.Serialize(
            new AdminEvent { Kind = "error", Message = message },
            AdminJson.Options));
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string? NormalizeVolumeRoot(string value)
    {
        var root = value.TrimEnd('\\');

        return root.Length == 2 && char.IsLetter(root[0]) && root[1] == ':'
            ? root + "\\"
            : null;
    }

    private static bool TokensEqual(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
