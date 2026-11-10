using AirClean.Core.Trash;

namespace AirClean.Core.Tests;

internal sealed class TestTrash : ITrashService
{
    public TrashSample? Sample { get; set; }

    public List<string> Moved { get; } = [];

    public List<string> Restored { get; } = [];

    public bool FailMoves { get; set; }

    public bool FailRestores { get; set; }

    /// <summary>Optional mapping from original path to a fake trash path.</summary>
    public Func<string, string?>? TrashPathFactory { get; set; }

    public bool IsSupported => true;

    public TrashSample? Probe(string? rootPath = null) => Sample;

    public TrashMoveResult MoveToTrash(string path)
    {
        if (FailMoves)
        {
            return TrashMoveResult.Failed("test failure");
        }

        Moved.Add(path);
        return TrashMoveResult.Moved(TrashPathFactory?.Invoke(path));
    }

    public bool TryRestore(string? trashPath, string originalPath, out string? error)
    {
        if (FailRestores)
        {
            error = "test restore failure";
            return false;
        }

        error = null;
        Restored.Add(originalPath);
        return true;
    }

    public long Empty(string? rootPath = null)
    {
        var bytes = Sample?.Bytes ?? 0;
        Sample = new TrashSample(0, 0);
        return bytes;
    }

    public void OpenTrash()
    {
    }
}
