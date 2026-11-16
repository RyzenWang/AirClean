namespace AirClean.Core.Security;

/// <summary>
/// Reads the stable identity of an existing file or directory. Implementations must never
/// throw and must return <c>false</c> when the platform or the item cannot be inspected:
/// identity binding is an extra safety check, not a requirement for scanning or deleting.
/// </summary>
public interface IFileIdentityProvider
{
    bool TryGet(string path, out FileIdentity identity);
}

/// <summary>
/// Always-unsupported provider. Used by tests and on platforms without an identity
/// implementation; consumers then keep their previous size/mtime behaviour.
/// </summary>
public sealed class NullFileIdentityProvider : IFileIdentityProvider
{
    public static NullFileIdentityProvider Instance { get; } = new();

    public bool TryGet(string path, out FileIdentity identity)
    {
        identity = default;
        return false;
    }
}
