using AirClean.Core.Models;

namespace AirClean.Core.Services;

/// <summary>How reading <c>settings.json</c> actually went.</summary>
public enum SettingsLoadStatus
{
    /// <summary>The file was read and deserialized.</summary>
    Ok,

    /// <summary>There is no file yet; this is a first run.</summary>
    FirstRun,

    /// <summary>The file was unusable but the previous copy in <c>settings.json.bak</c> was not.</summary>
    RecoveredFromBackup,

    /// <summary>The file exists but its JSON is malformed.</summary>
    Corrupt,

    /// <summary>The file exists but cannot be read (permissions, another process, I/O).</summary>
    Unreadable
}

/// <summary>
/// The outcome of <see cref="SettingsService.LoadResult"/>. The excluded-folder list is a
/// safety control, not a preference: a caller that cannot read it must not treat it as
/// "the user excluded nothing" and go ahead with a cleanup.
/// </summary>
public sealed record SettingsLoadResult(
    SettingsLoadStatus Status,
    AppSettings Settings,
    string? Detail = null)
{
    /// <summary>True when <see cref="Settings"/> carries the user's real excluded folders.</summary>
    public bool ExclusionsTrusted =>
        Status is SettingsLoadStatus.Ok
            or SettingsLoadStatus.FirstRun
            or SettingsLoadStatus.RecoveredFromBackup;

    /// <summary>True when destructive work has to wait until settings can be read again.</summary>
    public bool BlocksCleanup => !ExclusionsTrusted;
}
