using System.Runtime.InteropServices;
using AirClean.Core.Security;

namespace AirClean.Platform.Identity;

/// <summary>
/// Unix identity: <c>st_dev</c> plus <c>st_ino</c>. Only the leading fields of the native
/// <c>struct stat</c> are read (device at offset 0, inode at offset 8); the rest is opaque
/// padding so libc can write its whole struct without overflowing our buffer. The layout is
/// the 64-bit glibc/musl and Darwin one; on any other shape the call fails or the values are
/// rejected and <c>false</c> is returned instead of a guessed identity. This code is compiled
/// on Windows but only executed on Unix.
///
/// Darwin needs two symbols: <c>stat$INODE64</c> is the 64-bit-inode ABI on Intel, while
/// arm64 only ever shipped the 64-bit layout and exposes it as plain <c>stat</c>. The
/// provider therefore tries the suffixed symbol first and falls back to the plain one on
/// arm64 only; an Intel Mac whose libSystem lacks the suffixed symbol reports "no identity"
/// rather than reading a legacy layout at the wrong offsets.
/// </summary>
internal sealed class UnixFileIdentityProvider : IFileIdentityProvider
{
    public static UnixFileIdentityProvider Instance { get; } = new();

    // struct stat on both target families is well under 512 bytes; the exact tail differs
    // per libc version, so the buffer is deliberately oversized and treated as opaque.
    private const int StatBufferSize = 512;
    private const uint FileTypeMask = 0xF000;
    private const uint RegularFile = 0x8000;
    private const uint Directory = 0x4000;

    public bool TryGet(string path, out FileIdentity identity)
    {
        identity = default;

        try
        {
            if (OperatingSystem.IsLinux())
            {
                return TryGetLinux(path, out identity);
            }

            if (OperatingSystem.IsMacOS())
            {
                return TryGetMac(path, out identity);
            }
        }
        catch
        {
            // Missing libc symbol or a struct shape this build does not know: no identity.
        }

        return false;
    }

    private static bool TryGetLinux(string path, out FileIdentity identity)
    {
        identity = default;

        // 32-bit inode_t would need a different (EOVERFLOW-prone) ABI; decline instead of
        // returning a truncated inode.
        if (!Environment.Is64BitProcess)
        {
            return false;
        }

        if (LinuxStat(path, out var stat) != 0 || stat.Inode == 0)
        {
            return false;
        }

        identity = new FileIdentity(stat.Device, stat.Inode);
        return true;
    }

    private static bool TryGetMac(string path, out FileIdentity identity)
    {
        identity = default;

        if (!Environment.Is64BitProcess)
        {
            return false;
        }

        // Darwin packs st_dev(4), st_mode(2), st_nlink(2) before the 64-bit st_ino. Checking
        // the file-type bits catches a libc that returned a legacy (32-bit inode) structure,
        // where the inode would sit at a different offset.
        if (MacStat(path, out var stat) != 0 ||
            stat.Inode == 0 ||
            (stat.Mode & FileTypeMask) is not (RegularFile or Directory))
        {
            return false;
        }
        identity = new FileIdentity(stat.Device, stat.Inode);
        return true;
    }

    /// <summary>64-bit Linux <c>struct stat</c> prefix: st_dev at offset 0, st_ino at offset 8.</summary>
    [StructLayout(LayoutKind.Sequential, Size = StatBufferSize)]
    private struct LinuxStatBuffer
    {
        public ulong Device;

        public ulong Inode;
    }

    /// <summary>64-bit Darwin <c>struct stat</c> prefix: st_dev, st_mode, st_nlink, st_ino.</summary>
    [StructLayout(LayoutKind.Sequential, Size = StatBufferSize)]
    private struct MacStatBuffer
    {
        public uint Device;

        public ushort Mode;

        public ushort LinkCount;

        public ulong Inode;
    }

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int LinuxStat(string path, out LinuxStatBuffer buffer);

    [DllImport("libc", EntryPoint = "stat$INODE64", SetLastError = true)]
    private static extern int MacStatInode64(string path, out MacStatBuffer buffer);

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int MacStatPlainName(string path, out MacStatBuffer buffer);

    private static int MacStat(string path, out MacStatBuffer buffer)
    {
        try
        {
            return MacStatInode64(path, out buffer);
        }
        catch (EntryPointNotFoundException) when (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            // arm64 Darwin never shipped the legacy layout, so the plain symbol is the same
            // function there. On Intel this would be a guess, so it is not attempted.
            return MacStatPlainName(path, out buffer);
        }
    }
}
