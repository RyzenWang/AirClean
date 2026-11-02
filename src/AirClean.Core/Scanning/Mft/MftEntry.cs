namespace AirClean.Core.Scanning.Mft;

/// <summary>
/// The parts of one in-use NTFS FILE record that a fast large-file scan needs:
/// where the name lives, how big the unnamed data stream is, and when it changed.
/// </summary>
internal sealed class MftEntry
{
    public required uint RecordNumber { get; init; }

    public required uint ParentRecord { get; init; }

    public required string Name { get; init; }

    public required bool IsDirectory { get; init; }

    public required bool IsReparsePoint { get; init; }

    public required bool InUse { get; init; }

    public required bool IsExtension { get; init; }

    public required long Size { get; init; }

    public required DateTime LastWriteUtc { get; init; }

    /// <summary>Only populated for record 0, whose unnamed $DATA attribute holds the MFT itself.</summary>
    public MftDataRun[]? DataRuns { get; init; }
}
