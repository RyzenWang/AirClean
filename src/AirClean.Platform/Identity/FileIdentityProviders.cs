using AirClean.Core.Security;

namespace AirClean.Platform.Identity;

/// <summary>
/// Entry point for the identity provider that matches the current OS. The app hands the
/// result to the scanners and the deletion service; unsupported platforms get
/// <see cref="NullFileIdentityProvider"/>, which keeps the previous size/mtime behaviour.
/// </summary>
public static class FileIdentityProviders
{
    public static IFileIdentityProvider Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsFileIdentityProvider.Instance;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return UnixFileIdentityProvider.Instance;
        }

        return NullFileIdentityProvider.Instance;
    }
}
