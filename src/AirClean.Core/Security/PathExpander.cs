using System.Text.RegularExpressions;

namespace AirClean.Core.Security;

/// <summary>
/// Expands <c>%VAR%</c>, <c>$VAR</c>, <c>~</c> and single <c>*</c> directory segments in a
/// rule root. A root that references an unset variable is dropped entirely — expanding it
/// to an empty string would turn <c>$XDG_CACHE_HOME/foo</c> into <c>/foo</c>.
/// </summary>
public static partial class PathExpander
{
    private const int MaxExpandedRoots = 64;

    [GeneratedRegex("%([A-Za-z_][A-Za-z0-9_]*)%")]
    private static partial Regex PercentVariable();

    [GeneratedRegex(@"\$([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex DollarVariable();

    public static string? ExpandVariables(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value == "~" || value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
        {
            value = Home() + value[1..];
        }

        var missing = false;

        value = PercentVariable().Replace(value, match =>
        {
            var replacement = Environment.GetEnvironmentVariable(match.Groups[1].Value);
            if (string.IsNullOrEmpty(replacement))
            {
                missing = true;
                return match.Value;
            }

            return replacement;
        });

        value = DollarVariable().Replace(value, match =>
        {
            var replacement = Environment.GetEnvironmentVariable(match.Groups[1].Value);
            if (string.IsNullOrEmpty(replacement))
            {
                missing = true;
                return match.Value;
            }

            return replacement;
        });

        return missing ? null : value;
    }

    public static IReadOnlyList<string> Expand(string pattern)
    {
        var expanded = ExpandVariables(pattern);

        if (string.IsNullOrWhiteSpace(expanded))
        {
            return [];
        }

        if (!expanded.Contains('*') && !expanded.Contains('?'))
        {
            try
            {
                return [Path.GetFullPath(expanded)];
            }
            catch
            {
                return [];
            }
        }

        var root = Path.GetPathRoot(expanded) ?? string.Empty;
        if (root.Length == 0)
        {
            return [];
        }

        var segments = expanded[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        var current = new List<string> { root };

        foreach (var segment in segments)
        {
            var next = new List<string>();

            foreach (var parent in current)
            {
                if (!segment.Contains('*') && !segment.Contains('?'))
                {
                    next.Add(Path.Combine(parent, segment));
                    continue;
                }

                try
                {
                    if (Directory.Exists(parent))
                    {
                        next.AddRange(Directory.EnumerateDirectories(
                            parent,
                            segment,
                            new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }));
                    }
                }
                catch
                {
                    // Unreadable parents simply contribute nothing.
                }
            }

            current = next;

            if (current.Count == 0 || current.Count > MaxExpandedRoots)
            {
                break;
            }
        }

        return current
            .Take(MaxExpandedRoots)
            .Select(path =>
            {
                try
                {
                    return Path.GetFullPath(path);
                }
                catch
                {
                    return null;
                }
            })
            .OfType<string>()
            .Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? "~" : home;
    }
}
