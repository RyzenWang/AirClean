namespace AirClean.Core.Suggestions;

/// <summary>
/// Linux only: the package manager cache is real space, but removing it needs root. Without
/// elevation AirClean cannot offer it as a cleanup category, so it appears as a read-only
/// suggestion with the command to run instead of pretending the space is not there.
/// </summary>
public sealed class LinuxPackageCacheProbe : IReadOnlySuggestionProbe
{
    public const string Label = "L.SuggestionPackageCacheLabel";
    public const string Fact = "L.SuggestionPackageCacheFact";
    public const string Command = "sudo apt clean";

    private const int MaxEntries = 20_000;

    private readonly string _cachePath;

    public LinuxPackageCacheProbe()
        : this("/var/cache/apt/archives")
    {
    }

    /// <summary>Test seam: the real path needs root to be interesting.</summary>
    internal LinuxPackageCacheProbe(string cachePath)
    {
        _cachePath = cachePath;
    }

    /// <summary>Returns the probe on Linux and nothing elsewhere.</summary>
    public static IReadOnlyList<IReadOnlySuggestionProbe> CreateForCurrentPlatform() =>
        OperatingSystem.IsLinux() ? [new LinuxPackageCacheProbe()] : [];

    public IEnumerable<ReadOnlySuggestion> Probe()
    {
        if (string.IsNullOrWhiteSpace(_cachePath) || !SafeExists(_cachePath))
        {
            yield break;
        }

        var bytes = SumPackageFiles(_cachePath);
        if (bytes <= 0)
        {
            yield break;
        }

        yield return new ReadOnlySuggestion(Label, bytes, Fact, Command);
    }

    private static bool SafeExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds up <c>.deb</c> files at the top of the cache. Enumeration is capped and every
    /// failure is treated as "cannot measure", because a wrong number is worse than none.
    /// </summary>
    private static long SumPackageFiles(string path)
    {
        long total = 0;
        var seen = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                if (++seen > MaxEntries)
                {
                    break;
                }

                if (!file.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // A file that vanished or is unreadable simply does not count.
                }
            }
        }
        catch
        {
            return 0;
        }

        return total;
    }
}
