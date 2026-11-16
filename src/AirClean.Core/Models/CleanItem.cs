using AirClean.Core.Security;

namespace AirClean.Core.Models;

/// <summary>
/// One file that a clean rule matched. Directories are deleted implicitly once empty.
/// <see cref="Identity"/> is filled only by scanners that already inspect each item closely
/// enough to afford the extra handle; deletion can then detect a same-size, same-mtime swap.
/// </summary>
public sealed record CleanItem(
    string Path,
    string Root,
    long Size,
    DateTime LastWriteUtc,
    FileIdentity? Identity = null);
