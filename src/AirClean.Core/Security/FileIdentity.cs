namespace AirClean.Core.Security;

/// <summary>
/// Stable identity of one file system object. Two paths with the same identity refer to the
/// same data: Windows uses the volume serial number plus the 128-bit file id from
/// <c>FILE_ID_INFO</c> (kept as <see cref="Id"/> and <see cref="IdHigh"/>), Unix uses
/// <c>st_dev</c> plus <c>st_ino</c>. Comparing identities is what lets a cleanup notice a
/// replaced file and lets a duplicate scan fold hard links into one entry.
/// </summary>
/// <param name="Volume">Volume serial number (Windows) or device id (Unix).</param>
/// <param name="Id">Low 64 bits of the file id (Windows) or the inode number (Unix).</param>
/// <param name="IdHigh">High 64 bits of the Windows file id; always zero on Unix.</param>
public readonly record struct FileIdentity(ulong Volume, ulong Id, ulong IdHigh = 0);
