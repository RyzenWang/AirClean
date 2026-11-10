using System.Security.Cryptography;
using System.Text;

namespace AirClean.App.Services;

/// <summary>
/// One instance per user session. The first process owns a named mutex and waits on a
/// named event; a later launch signals the event and exits, and the first process brings
/// its window back. Object names are scoped to the session (<c>Local\</c>) and include a
/// hash of the user profile, so two users never block each other.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _activate;
    private readonly RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex? mutex, EventWaitHandle? activate, bool isFirst)
    {
        _mutex = mutex;
        _activate = activate;
        IsFirst = isFirst;

        if (isFirst && activate is not null)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(
                activate,
                (_, _) => Activated?.Invoke(this, EventArgs.Empty),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
    }

    public bool IsFirst { get; }

    /// <summary>Raised on a worker thread when another launch asks for the window.</summary>
    public event EventHandler? Activated;

    public static SingleInstance Acquire(string? keyOverride = null)
    {
        var key = keyOverride ?? UserKey();
        var mutexName = $@"Local\AirClean.{key}.SingleInstance";
        var activateName = $@"Local\AirClean.{key}.Activate";

        Mutex? mutex = null;
        EventWaitHandle? activate = null;

        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out var isFirst);
            activate = new EventWaitHandle(false, EventResetMode.AutoReset, activateName);
            return new SingleInstance(mutex, activate, isFirst);
        }
        catch
        {
            // If the OS refuses the named objects, start normally rather than refusing to
            // run; the worst case is the old behaviour (two instances).
            mutex?.Dispose();
            activate?.Dispose();
            return new SingleInstance(null, null, isFirst: true);
        }
    }

    public void SignalExisting()
    {
        try
        {
            _activate?.Set();
        }
        catch
        {
            // The other instance may have exited between the check and the signal.
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);

        if (_mutex is not null)
        {
            if (IsFirst)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // Released by process exit anyway.
                }
            }

            _mutex.Dispose();
        }

        _activate?.Dispose();
    }

    private static string UserKey()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(profile));
        return Convert.ToHexString(hash)[..16];
    }
}
