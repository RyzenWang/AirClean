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
/// Starts the same AirClean executable with the <c>runas</c> verb and talks to it over a
/// one-shot named pipe. The helper mode is handled before Avalonia starts, so no second
/// window is created.
/// </summary>
public sealed class AdminHelperClient(string? helperPath = null) : IAdminHelper
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool Enabled { get; set; }

    public LargeFileScanResult? TryScan(
        VolumeInfo volume,
        long thresholdBytes,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Enabled || !IsAvailable)
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

        Process? process = null;

        try
        {
            var pipeName = "airclean-" + Guid.NewGuid().ToString("N");
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var executable = helperPath ?? Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executable))
            {
                return null;
            }

            var arguments = BuildArguments(executable, pipeName, token);

            if (arguments is null)
            {
                return null;
            }

            using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            });

            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = process.WaitForExitAsync(timeout.Token);
            var completed = Task.WhenAny(connected, exited).GetAwaiter().GetResult();

            if (completed == exited && !pipe.IsConnected)
            {
                return null;
            }

            connected.GetAwaiter().GetResult();

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

            writer.WriteLine(JsonSerializer.Serialize(
                new AdminRequest
                {
                    Token = token,
                    Command = AdminHelperHost.ScanLargeFilesCommand,
                    VolumeRoot = volume.Root,
                    ThresholdBytes = thresholdBytes
                },
                AdminJson.Options));

            while (true)
            {
                var line = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();

                if (line is null)
                {
                    return null;
                }

                var value = JsonSerializer.Deserialize<AdminEvent>(line, AdminJson.Options);

                if (value is null)
                {
                    continue;
                }

                switch (value.Kind)
                {
                    case "progress":
                        progress?.Report(new ScanProgress(
                            value.FilesSeen,
                            value.MatchedBytes,
                            value.MatchedCount,
                            value.Message));
                        break;

                    case "error":
                        return null;

                    case "result":
                        return new LargeFileScanResult(
                            value.Items?
                                .Select(item => new CleanItem(
                                    item.Path,
                                    volume.Root,
                                    item.Size,
                                    item.LastWriteUtc))
                                .ToList() ?? [],
                            value.MatchedBytes,
                            value.MatchedCount);
                }
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.WaitForExit(2_000))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // The helper may still be showing the UAC prompt; leaving it alone is
                    // safer than failing the scan.
                }

                process.Dispose();
            }
        }
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
