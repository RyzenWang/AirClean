namespace AirClean.Core.Trash;

public readonly record struct TrashSample(long Bytes, int ItemCount);

/// <summary>
/// Outcome of moving one item to the trash. <see cref="TrashPath"/> is the location
/// inside the trash when the platform can report it; Windows' shell API cannot, so it
/// stays <c>null</c> there and restore resolves the item by its original path.
/// </summary>
public readonly record struct TrashMoveResult(bool Success, string? TrashPath, string? Error)
{
    public static TrashMoveResult Moved(string? trashPath = null) => new(true, trashPath, null);

    public static TrashMoveResult Failed(string error) => new(false, null, error);
}

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

    /// <summary>
    /// Moves one file or directory to the trash. Never throws; a failure comes back as a
    /// result with <see cref="TrashMoveResult.Error"/> set.
    /// </summary>
    TrashMoveResult MoveToTrash(string path);

    /// <summary>
    /// Moves an item back to <paramref name="originalPath"/>. <paramref name="trashPath"/>
    /// may be null when the platform did not report it (Windows); the implementation then
    /// looks the item up inside the trash by its original path. Returns false and an
    /// English reason when the item is gone, the target exists, or the move fails.
    /// </summary>
    bool TryRestore(string? trashPath, string originalPath, out string? error);

    /// <summary>Empties the trash (one volume, or all when null). Returns the bytes removed.</summary>
    long Empty(string? rootPath = null);

    /// <summary>Opens the trash in the system file manager.</summary>
    void OpenTrash();
}
