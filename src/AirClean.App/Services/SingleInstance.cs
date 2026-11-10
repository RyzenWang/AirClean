using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AirClean.Core.Services;

namespace AirClean.App.Services;

/// <summary>
/// One instance per user, implemented with two small files in the user's config folder:
/// a lock file holding the owner's process id, and a signal file the second launch
/// writes. The first process polls the signal file and brings its window back. File-based
/// state is used instead of named mutexes/events because their behaviour differs across
/// Windows, macOS and Linux; this works the same everywhere and a crashed process only
/// leaves a lock file that the next launch can reclaim.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly string? _lockPath;
    private readonly string? _signalPath;
    private readonly Timer? _timer;

    private SingleInstance(string? lockPath, string? signalPath, bool isFirst)
    {
        _lockPath = lockPath;
        _signalPath = signalPath;
        IsFirst = isFirst;

        if (isFirst && signalPath is not null)
        {
            // A stale signal from a previous run must not pop the window immediately.
            TryDelete(signalPath);
            _timer = new Timer(_ => PollSignal(), state: null, dueTime: 200, period: 200);
        }
    }

    public bool IsFirst { get; }

    /// <summary>Raised on a worker thread when another launch asks for the window.</summary>
    public event EventHandler? Activated;

    public static SingleInstance Acquire(string? keyOverride = null)
    {
        var key = keyOverride ?? UserKey();
        var directory = SettingsService.GetDefaultDirectory();
        var lockPath = Path.Combine(directory, $"single-instance-{key}.lock");
        var signalPath = Path.Combine(directory, $"activate-{key}.signal");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch
        {
            // If the folder is not writable, fall through and run as a single process.
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (TryWriteLock(lockPath))
            {
                return new SingleInstance(lockPath, signalPath, isFirst: true);
            }

            if (IsLockAlive(lockPath))
            {
                return new SingleInstance(lockPath, signalPath, isFirst: false);
            }

            // The previous owner crashed or left an empty file; reclaim it.
            TryDelete(lockPath);
        }

        return new SingleInstance(null, null, isFirst: true);
    }

    public void SignalExisting()
    {
        try
        {
            if (_signalPath is not null)
            {
                File.WriteAllText(_signalPath, DateTimeOffset.UtcNow.ToString("O"));
            }
        }
        catch
        {
            // The other instance may have exited between the check and the signal.
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();

        if (IsFirst)
        {
            TryDelete(_signalPath);
            TryDelete(_lockPath);
        }
    }

    private void PollSignal()
    {
        try
        {
            if (_signalPath is null || !File.Exists(_signalPath))
            {
                return;
            }

            File.Delete(_signalPath);
            Activated?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A transient file error only delays activation to the next tick.
        }
    }

    private static bool TryWriteLock(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(Environment.ProcessId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLockAlive(string path)
    {
        try
        {
            var text = File.ReadAllText(path).Trim();
            if (!int.TryParse(text, out var pid) || pid <= 0)
            {
                return false;
            }

            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // No process with that id: the previous owner is gone.
            return false;
        }
        catch
        {
            // Unreadable for another reason: assume alive rather than allow two windows.
            return true;
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }

    private static string UserKey()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(profile));
        return Convert.ToHexString(hash)[..16];
    }
}
