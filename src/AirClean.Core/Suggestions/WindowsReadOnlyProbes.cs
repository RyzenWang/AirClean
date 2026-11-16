namespace AirClean.Core.Suggestions;

/// <summary>
/// Read-only Windows items that are large but belong to the operating system. Sizes are
/// metadata reads only; the component store is never walked, because measuring it takes
/// minutes and that is exactly what DISM's analysis command is for.
/// </summary>
public sealed class WindowsReadOnlyProbes : IReadOnlySuggestionProbe
{
    public const string HibernateLabel = "L.SuggestionHibernateLabel";
    public const string HibernateFact = "L.SuggestionHibernateFact";
    public const string HibernateCommand = "powercfg /h off";

    public const string PageFileLabel = "L.SuggestionPagefileLabel";
    public const string PageFileFact = "L.SuggestionPagefileFact";

    public const string ComponentStoreLabel = "L.SuggestionWinSxSLabel";
    public const string ComponentStoreFact = "L.SuggestionWinSxSFact";
    public const string ComponentStoreCommand = "DISM /Online /Cleanup-Image /AnalyzeComponentStore";

    /// <summary>Returns the Windows probe on Windows and nothing elsewhere.</summary>
    public static IReadOnlyList<IReadOnlySuggestionProbe> CreateForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? [new WindowsReadOnlyProbes()] : [];

    public IEnumerable<ReadOnlySuggestion> Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");

        if (!string.IsNullOrWhiteSpace(systemDrive))
        {
            var root = Path.TrimEndingDirectorySeparator(systemDrive) + Path.DirectorySeparatorChar;

            if (TryGetFileSize(Path.Combine(root, "hiberfil.sys"), out var hibernationBytes))
            {
                yield return new ReadOnlySuggestion(
                    HibernateLabel,
                    hibernationBytes,
                    HibernateFact,
                    HibernateCommand);
            }

            if (TryGetFileSize(Path.Combine(root, "pagefile.sys"), out var pageFileBytes))
            {
                yield return new ReadOnlySuggestion(
                    PageFileLabel,
                    pageFileBytes,
                    PageFileFact);
            }
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            var componentStoreExists = false;

            try
            {
                componentStoreExists = Directory.Exists(Path.Combine(windows, "WinSxS"));
            }
            catch
            {
                // A denied or malformed Windows path only costs us this line.
            }

            if (componentStoreExists)
            {
                // Deliberately unknown: walking WinSxS would make every scan slow.
                yield return new ReadOnlySuggestion(
                    ComponentStoreLabel,
                    null,
                    ComponentStoreFact,
                    ComponentStoreCommand);
            }
        }
    }

    private static bool TryGetFileSize(string path, out long bytes)
    {
        bytes = 0;

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return false;
            }

            bytes = file.Length;
            return bytes > 0;
        }
        catch
        {
            // Hidden system files can deny metadata access to non-admins: stay silent.
            return false;
        }
    }
}
