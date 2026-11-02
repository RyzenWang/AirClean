using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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
/// back to a one-shot named pipe and serves a small command loop. It accepts fixed rule
/// IDs and drive roots only — never arbitrary paths — and never deletes outside the rules
/// compiled into this assembly.
/// </summary>
public static class AdminHelperHost
{
    public const string Switch = "--admin-helper";
    public const string ScanLargeFilesCommand = "scan-large-files";
    public const string ScanAdminCommand = "scan-admin";
    public const string CleanAdminCommand = "clean-admin";

    public static bool IsHelperCommand(IReadOnlyList<string> args) =>
        args.Any(arg => string.Equals(arg, Switch, StringComparison.Ordinal));

    public static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
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

            if (!IsAdministrator())
            {
                WriteError(writer, "The helper is not running elevated.");
                return 3;
            }

            while (true)
            {
                var requestLine = reader.ReadLine();

                if (requestLine is null)
                {
                    break;
                }

                var request = JsonSerializer.Deserialize<AdminRequest>(requestLine, AdminJson.Options);

                if (request is null ||
                    !TokensEqual(token, request.Token) ||
                    string.IsNullOrWhiteSpace(request.Command))
                {
                    WriteError(writer, "Invalid helper request.");
                    break;
                }

                var handled = request.Command switch
                {
                    ScanLargeFilesCommand => ScanLargeFiles(writer, request),
                    ScanAdminCommand => ScanAdmin(writer, request),
                    CleanAdminCommand => CleanAdmin(writer, request),
                    _ => Error(writer, "Unknown helper command.")
                };

                if (!handled)
                {
                    break;
                }
            }

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static bool ScanLargeFiles(StreamWriter writer, AdminRequest request)
    {
        var root = NormalizeVolumeRoot(request.VolumeRoot);

        if (root is null)
        {
            return Error(writer, "The helper only scans a drive root.");
        }

        var volume = new VolumeInfo(root, string.Empty, "NTFS", 0, 0, IsSystem: false, IsRemovable: false);

        var progress = new InlineProgress<ScanProgress>(value => Write(
            writer,
            new AdminEvent
            {
                Kind = "progress",
                FilesSeen = value.FilesSeen,
                MatchedBytes = value.BytesFound,
                MatchedCount = (int)Math.Min(value.ItemsFound, int.MaxValue),
                Message = value.CurrentTarget
            }));

        var usedMft = MftLargeFileScanner.TryScan(
            volume,
            Math.Max(0, request.ThresholdBytes),
            progress,
            CancellationToken.None,
            out var result);

        if (!usedMft || result is null)
        {
            return Error(writer, "The MFT fast path was unavailable.");
        }

        Write(
            writer,
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
            });

        return true;
    }

    private static bool ScanAdmin(StreamWriter writer, AdminRequest request)
    {
        if (AdminRules.Find(request.RuleId ?? string.Empty) is not { } rule)
        {
            return Error(writer, "Unknown administrator rule.");
        }

        if (rule.Kind == AdminRuleKind.Journal)
        {
            var journalBytes = MeasureJournal();

            Write(
                writer,
                new AdminEvent
                {
                    Kind = "admin-scan",
                    RuleId = rule.Id,
                    MatchedBytes = journalBytes,
                    MatchedCount = journalBytes > 0 ? 1 : 0
                });

            return true;
        }

        var (bytes, count) = Measure(rule.Root);

        Write(
            writer,
            new AdminEvent
            {
                Kind = "admin-scan",
                RuleId = rule.Id,
                MatchedBytes = bytes,
                MatchedCount = count
            });

        return true;
    }

    private static bool CleanAdmin(StreamWriter writer, AdminRequest request)
    {
        if (AdminRules.Find(request.RuleId ?? string.Empty) is not { } rule)
        {
            return Error(writer, "Unknown administrator rule.");
        }

        if (rule.Kind == AdminRuleKind.Journal)
        {
            return CleanJournal(writer, rule);
        }

        var files = EnumerateFiles(rule.Root).ToList();
        var total = files.Count;
        var done = 0;
        var deleted = 0;
        var skipped = 0;
        long bytes = 0;

        foreach (var file in files)
        {
            try
            {
                if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }

                var length = file.Length;
                file.Delete();
                bytes += length;
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }

            done++;

            if (done % 32 == 0 || done == total)
            {
                Write(
                    writer,
                    new AdminEvent
                    {
                        Kind = "progress",
                        RuleId = rule.Id,
                        FilesSeen = done,
                        MatchedBytes = bytes,
                        MatchedCount = total
                    });
            }
        }

        PruneEmptyDirectories(rule.Root);

        Write(
            writer,
            new AdminEvent
            {
                Kind = "admin-clean",
                RuleId = rule.Id,
                MatchedBytes = bytes,
                MatchedCount = deleted,
                Skipped = skipped
            });

        return true;
    }

    private static bool CleanJournal(StreamWriter writer, AdminRule rule)
    {
        var journalctl = FindJournalctl();

        if (journalctl is null)
        {
            return Error(writer, "journalctl was not found.");
        }

        var before = MeasureJournal();

        RunJournalctl(journalctl, "--no-pager --vacuum-time=14d");
        RunJournalctl(journalctl, "--no-pager --vacuum-size=500M");

        var after = MeasureJournal();
        var freed = Math.Max(0, before - after);

        Write(
            writer,
            new AdminEvent
            {
                Kind = "admin-clean",
                RuleId = rule.Id,
                MatchedBytes = freed,
                MatchedCount = freed > 0 ? 1 : 0,
                Skipped = 0
            });

        return true;
    }

    private static long MeasureJournal()
    {
        long bytes = 0;

        foreach (var root in new[] { "/var/log/journal", "/run/log/journal" })
        {
            foreach (var file in EnumerateFiles(root))
            {
                try
                {
                    bytes += file.Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A rotated file may disappear while we are measuring it.
                }
            }
        }

        return bytes;
    }

    private static bool RunJournalctl(string executable, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort.
                }

                return false;
            }

            _ = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindJournalctl()
    {
        foreach (var path in new[] { "/usr/bin/journalctl", "/bin/journalctl" })
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static (long Bytes, int Count) Measure(string root)
    {
        long bytes = 0;
        var count = 0;

        foreach (var file in EnumerateFiles(root))
        {
            try
            {
                bytes += file.Length;
                count++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file that disappears during the scan simply contributes nothing.
            }
        }

        return (bytes, count);
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var entries = Enumerate(directory);

            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    pending.Push(child.FullName);
                    continue;
                }

                if (entry is FileInfo file)
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<FileSystemInfo> Enumerate(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void PruneEmptyDirectories(string root)
    {
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var entry in Enumerate(directory))
            {
                if (entry is DirectoryInfo child &&
                    (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    directories.Add(child.FullName);
                    pending.Push(child.FullName);
                }
            }
        }

        foreach (var directory in directories.OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Locked or still in use; leave it for the next run.
            }
        }
    }

    private static void Write(StreamWriter writer, AdminEvent value) =>
        writer.WriteLine(JsonSerializer.Serialize(value, AdminJson.Options));

    private static bool Error(StreamWriter writer, string message)
    {
        WriteError(writer, message);
        return false;
    }

    private static void WriteError(StreamWriter writer, string message) =>
        Write(writer, new AdminEvent { Kind = "error", Message = message });

    private static bool IsAdministrator()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        return OperatingSystem.IsLinux() && GetEffectiveUserId() == 0;
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    private static uint GetEffectiveUserId() => geteuid();

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
