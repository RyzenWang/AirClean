namespace AirClean.Core.Suggestions;

/// <summary>
/// Collects read-only suggestions from every probe, drops noise and orders what remains.
/// A missing size is deliberately kept: unknown does not mean zero, and hiding a large
/// component store because it could not be measured would be the worst omission.
/// </summary>
public sealed class SuggestionScanner
{
    /// <summary>
    /// Entries with a known size below this are dropped. Windows' own files are much
    /// larger; a small measured entry is not worth a line in the card.
    /// </summary>
    public const long MinimumKnownBytes = 200L * 1024 * 1024;

    private readonly IReadOnlyList<IReadOnlySuggestionProbe> _probes;

    public SuggestionScanner(IEnumerable<IReadOnlySuggestionProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);
        _probes = probes.ToArray();
    }

    public IReadOnlyList<ReadOnlySuggestion> Scan()
    {
        var suggestions = new List<ReadOnlySuggestion>();

        foreach (var probe in _probes)
        {
            try
            {
                foreach (var suggestion in probe.Probe())
                {
                    if (string.IsNullOrWhiteSpace(suggestion.Label) ||
                        string.IsNullOrWhiteSpace(suggestion.FactKey))
                    {
                        continue;
                    }

                    // Known and small: noise. Unknown: keep it, since it may be huge.
                    if (suggestion.Bytes is { } bytes && bytes < MinimumKnownBytes)
                    {
                        continue;
                    }

                    suggestions.Add(suggestion);
                }
            }
            catch
            {
                // A probe is allowed to fail (permissions, timeouts, odd filesystems).
                // Losing one line is acceptable; throwing would hide the whole card.
            }
        }

        return suggestions
            .DistinctBy(suggestion => suggestion.Label, StringComparer.Ordinal)
            .OrderByDescending(suggestion => suggestion.Bytes.HasValue)
            .ThenByDescending(suggestion => suggestion.Bytes ?? 0)
            .ThenBy(suggestion => suggestion.Label, StringComparer.Ordinal)
            .ToArray();
    }
}
