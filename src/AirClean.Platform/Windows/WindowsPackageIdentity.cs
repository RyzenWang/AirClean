using System.Runtime.InteropServices;

namespace AirClean.Platform.Windows;

/// <summary>
/// Whether this process runs inside an MSIX package (the Microsoft Store build). Packaged
/// apps update through the Store and cannot start an elevated child process, so the app
/// asks this once and turns those two paths off instead of failing at them later.
/// </summary>
public static class WindowsPackageIdentity
{
    /// <summary><c>APPMODEL_ERROR_NO_PACKAGE</c>: the process has no package identity.</summary>
    private const int NoPackage = 15700;

    /// <summary><c>ERROR_INSUFFICIENT_BUFFER</c>: a package identity exists.</summary>
    private const int InsufficientBuffer = 122;

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, null);

            // A packaged process answers "buffer too small" and tells us the needed length;
            // an unpackaged one answers that there is no package at all.
            return result == InsufficientBuffer ||
                   (result == 0 && length > 0);
        }
        catch
        {
            // Older Windows or a restricted host: treat it as unpackaged.
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
