using AirClean.Core.Formatting;

namespace AirClean.Core.Volumes;

public sealed record VolumeInfo(
    string Root,
    string Label,
    string FileSystem,
    long TotalBytes,
    long FreeBytes,
    bool IsSystem,
    bool IsRemovable)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedRatio => TotalBytes <= 0 ? 0 : Math.Clamp((double)UsedBytes / TotalBytes, 0, 1);

    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Label) ? Root : $"{Label} ({Root})";
            var free = ByteSize.Format(FreeBytes);
            return $"{name} · {free} free";
        }
    }
}

/// <summary>
/// Enumerates mounted volumes a cleaner can work with. Network shares and optical media
/// are deliberately excluded: moving to the trash across a network boundary is a copy.
/// </summary>
public static class VolumeService
{
    public static IReadOnlyList<VolumeInfo> List()
    {
        try
        {
            var systemRoot = OperatingSystem.IsWindows()
                ? Path.GetPathRoot(Environment.SystemDirectory)
                : "/";

            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady &&
                                drive.DriveType is DriveType.Fixed or DriveType.Removable)
                .Select(drive => Create(drive, systemRoot))
                .OrderByDescending(volume => volume.IsSystem)
                .ThenBy(volume => volume.Root, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static VolumeInfo Create(DriveInfo drive, string? systemRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName) + Path.DirectorySeparatorChar;

        string label;
        try
        {
            label = drive.VolumeLabel;
        }
        catch
        {
            label = string.Empty;
        }

        long total = 0;
        long free = 0;

        try
        {
            total = drive.TotalSize;
            free = drive.AvailableFreeSpace;
        }
        catch
        {
            // Some removable volumes throw while a card is being mounted.
        }

        var isSystem = string.Equals(
            Path.TrimEndingDirectorySeparator(root),
            Path.TrimEndingDirectorySeparator(systemRoot ?? "/"),
            StringComparison.OrdinalIgnoreCase);

        return new VolumeInfo(root, label, drive.DriveFormat, total, free, isSystem, drive.DriveType == DriveType.Removable);
    }
}
