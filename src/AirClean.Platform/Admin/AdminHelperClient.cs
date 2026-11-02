using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AirClean.Core.Models;
using AirClean.Core.Scanning;
using AirClean.Core.Volumes;

namespace AirClean.Platform.Admin;

/// <summary>
/// Starts the same AirClean executable with the <c>runas</c> verb and keeps one helper
/// session for the lifetime of the app. All requests share a one-shot named pipe, a random
/// token, and a sequential command loop, so a scan asks for UAC once instead of once per
/// operation. The helper mode is handled before Avalonia starts, so no second window is
/// created.
/// </summary>
public sealed class AdminHelperClient(string? helperPath = null) : IAdminHelper, IDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private string? _token;
    private bool _enabled;
    private bool _failed;

    public bool IsAvailable => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;

            if (value)
            {
                _failed = false;
            }
        }
    }

    public LargeFileScanResult? TryScan(
        VolumeInfo volume,
        long thresholdBytes,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!CanUse())
        {
            return null;
        }

        var root = volume.Root.TrimEnd('\\');

        if (!string.Equals(volume.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase) ||
            root.Length != 2 ||
            !char.IsLetter(root[0]) ||
            root[1] != ':')
        {
            return null;
        }

        var value = Execute(
            new AdminRequest
            {
                Command = AdminHelperHost.ScanLargeFilesCommand,
                VolumeRoot = volume.Root,
                ThresholdBytes = thresholdBytes
            },
            progress is null
                ? null
                : item => progress.Report(new ScanProgress(
                    item.FilesSeen,
                    item.MatchedBytes,
                    item.MatchedCount,
                    item.Message)),
            cancellationToken);

        if (value?.Kind != "result")
        {
            return null;
        }

        return new LargeFileScanResult(
            value.Items?
                .Select(item => new CleanItem(item.Path, volume.Root, item.Size, item.LastWriteUtc))
                .ToList() ?? [],
            value.MatchedBytes,
            value.MatchedCount);
    }

    public AdminScanResult? Scan(string ruleId, CancellationToken cancellationToken)
    {
        if (!CanUse())
        {
            return null;
        }

        var value = Execute(
            new AdminRequest { Command = AdminHelperHost.ScanAdminCommand, RuleId = ruleId },
            onProgress: null,
            cancellationToken);

        return value?.Kind == "admin-scan"
            ? new AdminScanResult(ruleId, value.MatchedBytes, value.MatchedCount)
            : null;
    }

    public AdminCleanupReport? Clean(
        string ruleId,
        IProgress<DeletionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!CanUse())
        {
            return null;
        }

        var value = Execute(
            new AdminRequest { Command = AdminHelperHost.CleanAdminCommand, RuleId = ruleId },
            progress is null
                ? null
                : item => progress.Report(new DeletionProgress(
                    (int)Math.Min(item.FilesSeen, int.MaxValue),
                    item.MatchedCount,
                    null)),
            cancellationToken);

        return value?.Kind == "admin-clean"
            ? new AdminCleanupReport(value.MatchedBytes, value.MatchedCount, value.Skipped)
            : null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            ResetSession();
        }
    }

    private bool CanUse() => Enabled && IsAvailable && !_failed;

    private AdminEvent? Execute(
        AdminRequest request,
        Action<AdminEvent>? onProgress,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!EnsureSession(cancellationToken))
            {
                return null;
            }

            try
            {
                request.Token = _token!;
                _writer!.WriteLine(JsonSerializer.Serialize(request, AdminJson.Options));

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));

                while (true)
                {
                    var line = _reader!.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();

                    if (line is null)
                    {
                        ResetSession();
                        return null;
                    }

                    var value = JsonSerializer.Deserialize<AdminEvent>(line, AdminJson.Options);

                    if (value is null)
                    {
                        continue;
                    }

                    if (string.Equals(value.Kind, "progress", StringComparison.Ordinal))
                    {
                        onProgress?.Invoke(value);
                        continue;
                    }

                    if (string.Equals(value.Kind, "error", StringComparison.Ordinal))
                    {
                        ResetSession();
                        return null;
                    }

                    return value;
                }
            }
            catch
            {
                ResetSession();
                return null;
            }
        }
    }

    private bool EnsureSession(CancellationToken cancellationToken)
    {
        if (_pipe?.IsConnected == true)
        {
            return true;
        }

        ResetSession();

        var pipeName = "airclean-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var executable = helperPath ?? Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(executable))
        {
            _failed = true;
            return false;
        }

        var arguments = BuildArguments(executable, pipeName, token);

        if (arguments is null)
        {
            _failed = true;
            return false;
        }

        try
        {
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _pipe = pipe;

            var process = StartHelper(executable, arguments);
            _process = process;

            if (process is null)
            {
                ResetSession();
                _failed = true;
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = process.WaitForExitAsync(timeout.Token);
            var completed = Task.WhenAny(connected, exited).GetAwaiter().GetResult();

            if (completed == exited && !pipe.IsConnected)
            {
                ResetSession();
                _failed = true;
                return false;
            }

            connected.GetAwaiter().GetResult();

            _reader = new StreamReader(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            _writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true
            };

            _token = token;
            return true;
        }
        catch
        {
            ResetSession();
            _failed = true;
            return false;
        }
    }

    private void ResetSession()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            _reader?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.WaitForExit(2_000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The helper may still be showing the UAC prompt.
            }

            _process.Dispose();
        }

        _writer = null;
        _reader = null;
        _pipe = null;
        _process = null;
        _token = null;
    }

    private static Process? StartHelper(string executable, string arguments)
    {
        if (OperatingSystem.IsWindows())
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            });
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = "pkexec",
            Arguments = $"\"{executable}\" {arguments}",
            UseShellExecute = false
        });
    }

    private static string? BuildArguments(string executable, string pipeName, string token)
    {
        var helperArguments = $"{AdminHelperHost.Switch} --pipe {pipeName} --token {token}";

        if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return helperArguments;
        }

        // dotnet run / dotnet AirClean.dll: elevate dotnet itself and point it at the app.
        var assembly = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.EndsWith("AirClean.dll", StringComparison.OrdinalIgnoreCase));

        return assembly is null
            ? null
            : $"exec \"{assembly}\" {helperArguments}";
    }
}
