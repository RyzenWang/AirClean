namespace AirClean.Core.Suggestions;

/// <summary>
/// Something worth showing the user even though AirClean must never clean it: Windows
/// owns these files itself. The UI displays the fact and, at most, a command the user can
/// copy and run deliberately; nothing here can enter a deletion plan.
/// </summary>
/// <param name="Label">Localization key or already localized label.</param>
/// <param name="Bytes">
/// Known size in bytes, or <c>null</c> when it cannot be measured. Unknown is not zero:
/// callers must not substitute 0 for a missing measurement.
/// </param>
/// <param name="FactKey">Localization key or already localized explanation of what it is and why it is off limits.</param>
/// <param name="SuggestedCommand">Optional command to copy; AirClean never runs it.</param>
public sealed record ReadOnlySuggestion(
    string Label,
    long? Bytes,
    string FactKey,
    string? SuggestedCommand = null);
