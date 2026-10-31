namespace AirClean.Core.Models;

public sealed record DuplicateFile(string Path, long Size, DateTime LastWriteUtc);

/// <summary>Files with identical content. The oldest copy is kept by default.</summary>
public sealed class DuplicateGroup
{
    public required string Key { get; init; }

    public required long FileSize { get; init; }

    public required IReadOnlyList<DuplicateFile> Files { get; init; }

    public DuplicateFile Keeper => Files[0];

    public IReadOnlyList<DuplicateFile> Removable => Files.Skip(1).ToArray();

    public long RemovableBytes => FileSize * (Files.Count - 1);
}

public readonly record struct DuplicateScanProgress(
    int FilesSeen,
    int Candidates,
    long BytesHashed,
    int GroupsFound,
    string? CurrentPath);
