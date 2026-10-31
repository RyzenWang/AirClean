namespace AirClean.Core.Models;

public enum CleanCategory
{
    SystemTemp,
    AppCache,
    LogsDiagnostics,
    Trash,
    DeveloperCache,
    LargeFiles,
    Duplicates
}

/// <summary>
/// How much care a category needs. <see cref="Safe"/> categories are selected after a
/// scan; <see cref="Caution"/> ones are not, and <see cref="Protected"/> ones are never
/// offered at all.
/// </summary>
public enum Safety
{
    Safe,
    Caution,
    Protected
}

public enum DeleteMode
{
    Trash,
    Permanent
}

public enum SkipReason
{
    InUse,
    Permission,
    ChangedSinceScan,
    NotFound,
    Protected,
    ReparsePoint
}
