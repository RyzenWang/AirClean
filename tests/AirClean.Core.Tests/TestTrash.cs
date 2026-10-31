using AirClean.Core.Trash;

namespace AirClean.Core.Tests;

internal sealed class TestTrash : ITrashService
{
    public TrashSample? Sample { get; set; }

    public List<string> Moved { get; } = [];

    public bool FailMoves { get; set; }

    public bool IsSupported => true;

    public TrashSample? Probe(string? rootPath = null) => Sample;

    public bool MoveToTrash(string path, out string? error)
    {
        error = null;

        if (FailMoves)
        {
            error = "test failure";
            return false;
        }

        Moved.Add(path);
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
