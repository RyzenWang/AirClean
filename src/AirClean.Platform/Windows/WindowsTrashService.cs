using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AirClean.Core.Trash;

namespace AirClean.Platform.Windows;

/// <summary>
/// Recycle Bin integration through the shell. Long paths beyond MAX_PATH are not handled
/// yet; the design doc lists <c>IFileOperation</c> as the follow-up.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTrashService : ITrashService
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoConfirmMkdir = 0x0200;
    private const ushort FofNoErrorUi = 0x0400;

    private const uint SherbNoConfirmation = 0x0001;
    private const uint SherbNoProgressUi = 0x0002;
    private const uint SherbNoSound = 0x0004;

    public bool IsSupported => true;

    public TrashSample? Probe(string? rootPath = null)
    {
        try
        {
            var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
            return SHQueryRecycleBin(rootPath, ref info) == 0
                ? new TrashSample(info.SizeBytes, (int)info.ItemCount)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public TrashMoveResult MoveToTrash(string path)
    {
        if (!IsRecyclableVolume(path))
        {
            return TrashMoveResult.Failed("The volume does not have a recycle bin.");
        }

        try
        {
            var operation = new ShFileOpStruct
            {
                Function = FoDelete,
                From = path + "\0",
                Flags = FofAllowUndo | FofNoConfirmation | FofNoConfirmMkdir | FofSilent | FofNoErrorUi
            };

            var result = SHFileOperation(ref operation);

            if (result == 0 && !operation.Aborted)
            {
                // SHFileOperation does not report the recycle-bin path. Restore resolves
                // the item by its original path instead (see RecycleBin.TryRestore).
                return TrashMoveResult.Moved();
            }

            return TrashMoveResult.Failed($"SHFileOperation failed (0x{result:X}).");
        }
        catch (Exception exception)
        {
            return TrashMoveResult.Failed(exception.Message);
        }
    }

    public bool TryRestore(string? trashPath, string originalPath, out string? error) =>
        RecycleBin.TryRestore(trashPath, originalPath, out error);

    public long Empty(string? rootPath = null)
    {
        var before = Probe(rootPath)?.Bytes ?? 0;

        try
        {
            SHEmptyRecycleBin(IntPtr.Zero, rootPath, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
        }
        catch
        {
            // Reported through the measurement below.
        }

        var after = Probe(rootPath)?.Bytes ?? 0;
        return Math.Max(0, before - after);
    }

    public void OpenTrash()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening the trash is a convenience only.
        }
    }

    private static bool IsRecyclableVolume(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            var drive = DriveInfo.GetDrives()
                .FirstOrDefault(candidate => string.Equals(
                    Path.TrimEndingDirectorySeparator(candidate.RootDirectory.FullName),
                    Path.TrimEndingDirectorySeparator(root),
                    StringComparison.OrdinalIgnoreCase));

            return drive is not null && drive.DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long SizeBytes;
        public long ItemCount;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Window;

        public uint Function;

        public string From;

        public string? To;

        public ushort Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Aborted;

        public IntPtr NameMappings;

        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref ShFileOpStruct operation);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr window, string? rootPath, uint flags);
}
