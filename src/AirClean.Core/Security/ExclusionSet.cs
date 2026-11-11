namespace AirClean.Core.Security;

/// <summary>
/// A normalized set of folders the user never wants scanned or deleted. The set is
/// mutable so the settings window can replace it without rebuilding the scanners; readers
/// grab the current array once and compare against that snapshot.
/// </summary>
public sealed class ExclusionSet
{
    private static readonly StringComparison Comparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    private string[] _roots = [];

    public ExclusionSet(IEnumerable<string>? paths = null) => Replace(paths);

    public bool IsEmpty => _roots.Length == 0;

    public void Replace(IEnumerable<string>? paths)
    {
        var normalized = new List<string>();

        foreach (var raw in paths ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim());
            }
            catch
            {
                continue;
            }

            var trimmed = full.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            normalized.Add(trimmed.Length == 0 ? full : trimmed);
        }

        var comparer = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

        _roots = normalized
            .Distinct(comparer)
            .Take(200)
            .ToArray();
    }

    public bool Contains(string path)
    {
        var roots = _roots;
        if (roots.Length == 0 || string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var root in roots)
        {
            if (root is "/" or "\\")
            {
                return true;
            }

            if (string.Equals(path, root, Comparison))
            {
                return true;
            }

            if (path.Length > root.Length &&
                path.StartsWith(root, Comparison) &&
                path[root.Length] is '\\' or '/')
            {
                return true;
            }
        }

        return false;
    }
}
