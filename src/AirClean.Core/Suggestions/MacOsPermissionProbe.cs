namespace AirClean.Core.Suggestions;

/// <summary>
/// macOS only: when the app cannot read <c>~/Library/Containers</c>, the scan silently sees
/// fewer files than exist. That is a Full Disk Access problem, not a cleanup problem, so it
/// is reported as a suggestion with the settings pane command instead of a skip count.
///
/// The probe is deliberately narrow: a missing folder is not a permission problem, and a
/// readable folder costs one directory enumeration, never a walk.
/// </summary>
public sealed class MacOsPermissionProbe : IReadOnlySuggestionProbe
{
    public const string Label = "L.SuggestionFullDiskAccessLabel";
    public const string Fact = "L.SuggestionFullDiskAccessFact";

    /// <summary>Opens System Settings at Privacy &amp; Security → Full Disk Access.</summary>
    public const string Command =
        "open \"x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles\"";

    private readonly string _containersPath;
    private readonly Func<string, bool> _canEnumerate;

    public MacOsPermissionProbe()
        : this(DefaultContainersPath(), CanEnumerate)
    {
    }

    /// <summary>Test seam: lets a unit test describe the access result instead of owning ACLs.</summary>
    internal MacOsPermissionProbe(string containersPath, Func<string, bool> canEnumerate)
    {
        _containersPath = containersPath;
        _canEnumerate = canEnumerate;
    }

    /// <summary>Returns the probe on macOS and nothing elsewhere.</summary>
    public static IReadOnlyList<IReadOnlySuggestionProbe> CreateForCurrentPlatform() =>
        OperatingSystem.IsMacOS() ? [new MacOsPermissionProbe()] : [];

    public IEnumerable<ReadOnlySuggestion> Probe()
    {
        if (string.IsNullOrWhiteSpace(_containersPath))
        {
            yield break;
        }

        bool exists;

        try
        {
            exists = Directory.Exists(_containersPath);
        }
        catch
        {
            yield break;
        }

        if (!exists)
        {
            // Nothing installed yet: no permission advice to give.
            yield break;
        }

        bool readable;

        try
        {
            readable = _canEnumerate(_containersPath);
        }
        catch
        {
            readable = false;
        }

        if (readable)
        {
            yield break;
        }

        yield return new ReadOnlySuggestion(Label, null, Fact, Command);
    }

    private static string DefaultContainersPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? string.Empty
            : Path.Combine(home, "Library", "Containers");
    }

    /// <summary>One entry is enough to know whether the folder is readable.</summary>
    private static bool CanEnumerate(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return entries.MoveNext() || true;
        }
        catch
        {
            return false;
        }
    }
}
