using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AirClean.Core.Security;
using Microsoft.Win32.SafeHandles;

namespace AirClean.Platform.Identity;

/// <summary>
/// Windows identity: volume serial number plus the 128-bit file id that
/// <c>GetFileInformationByHandleEx(FileIdInfo)</c> reports. The handle is opened with
/// <c>FILE_READ_ATTRIBUTES</c>, full sharing and <c>FILE_FLAG_BACKUP_SEMANTICS</c>: the
/// latter is what lets a directory be opened too, and it is only available through
/// <c>CreateFileW</c> because .NET's <see cref="FileOptions"/> does not expose it. Every
/// failure is reported as <c>false</c>; identity binding must never break a scan or delete.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsFileIdentityProvider : IFileIdentityProvider
{
    private const int FileIdInfoClass = 18;

    private const uint FileReadAttributes = 0x0080;
    private const uint ShareReadWriteDelete = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    public static WindowsFileIdentityProvider Instance { get; } = new();

    public bool TryGet(string path, out FileIdentity identity)
    {
        identity = default;

        try
        {
            using var handle = CreateFile(
                path,
                FileReadAttributes,
                ShareReadWriteDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics,
                IntPtr.Zero);

            if (handle.IsInvalid ||
                !GetFileInformationByHandleEx(
                    handle,
                    FileIdInfoClass,
                    out var info,
                    (uint)Marshal.SizeOf<FileIdInfo>()))
            {
                return false;
            }

            identity = new FileIdentity(info.VolumeSerialNumber, info.FileId.Low, info.FileId.High);
            return true;
        }
        catch
        {
            // Missing, locked, permission-denied or an unexpected Win32 shape: no identity.
            return false;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileIdInfo fileInformation,
        uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;

        public FileId128 FileId;
    }

    /// <summary>Layout-compatible with the Win32 <c>FILE_ID_128</c> (16 raw bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileId128
    {
        public ulong Low;

        public ulong High;
    }
}
