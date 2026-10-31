namespace AirClean.Core.Trash;

public readonly record struct TrashSample(long Bytes, int ItemCount);

/// <summary>
/// OS trash integration. All members are synchronous on purpose: callers run them on a
/// worker thread, and the shell APIs are blocking anyway.
/// </summary>
public interface ITrashService
{
    /// <summary>False on platforms where AirClean cannot reach a trash can.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Current trash size. Pass a volume root (e.g. <c>D:\</c>) for one volume, or null
    /// for every volume.
    /// </summary>
    TrashSample? Probe(string? rootPath = null);

    /// <summary>Moves one file or directory to the trash. Returns false and a reason on failure.</summary>
    bool MoveToTrash(string path, out string? error);

    /// <summary>Empties the trash (one volume, or all when null). Returns the bytes removed.</summary>
    long Empty(string? rootPath = null);

    /// <summary>Opens the trash in the system file manager.</summary>
    void OpenTrash();
}
