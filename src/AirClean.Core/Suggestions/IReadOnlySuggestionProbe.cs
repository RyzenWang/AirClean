namespace AirClean.Core.Suggestions;

/// <summary>
/// Something that can observe a read-only system item. Implementations must never throw:
/// a failed, denied or timed-out probe returns an empty sequence or omits that entry,
/// because a wrong number is worse than no number.
/// </summary>
public interface IReadOnlySuggestionProbe
{
    IEnumerable<ReadOnlySuggestion> Probe();
}
