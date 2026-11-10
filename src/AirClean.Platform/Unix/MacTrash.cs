using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AirClean.Platform.Unix;

/// <summary>
/// Minimal Objective-C bridge for the one call the trash needs:
/// <c>[NSFileManager trashItemAtURL:resultingItemURL:error:]</c>. Using the system API
/// gives us the resulting URL (so restore knows where the item went) and lets macOS put
/// items on an external volume's own <c>.Trashes</c> instead of always using the home one.
/// Any failure falls back to the old File.Move path, so a bridge problem never blocks a
/// cleanup.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacTrash
{
    private const string Library = "/usr/lib/libobjc.A.dylib";

    public static bool TryMove(string path, out string? trashPath, out string? error)
    {
        trashPath = null;
        error = null;

        var pool = objc_autoreleasePoolPush();

        try
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(path);

            try
            {
                var nsString = SendPtr(Class("NSString"), Selector("stringWithUTF8String:"), utf8);
                if (nsString == IntPtr.Zero)
                {
                    error = "NSString returned null.";
                    return false;
                }

                var url = SendPtr(Class("NSURL"), Selector("fileURLWithPath:"), nsString);
                if (url == IntPtr.Zero)
                {
                    error = "NSURL returned null.";
                    return false;
                }

                var manager = Send(Class("NSFileManager"), Selector("defaultManager"));
                var moved = SendTrash(
                    manager,
                    Selector("trashItemAtURL:resultingItemURL:error:"),
                    url,
                    out var resulting,
                    out var nsError);

                if (!moved)
                {
                    error = Describe(nsError) ?? "NSFileManager refused to move the item.";
                    return false;
                }

                trashPath = PathOf(resulting);
                if (string.IsNullOrEmpty(trashPath))
                {
                    error = "NSFileManager returned no destination URL.";
                    return false;
                }

                return true;
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Selector(string name) => sel_registerName(name);

    private static string? PathOf(IntPtr url)
    {
        if (url == IntPtr.Zero)
        {
            return null;
        }

        var nsString = Send(url, Selector("path"));
        return nsString == IntPtr.Zero ? null : Utf8(nsString);
    }

    private static string? Describe(IntPtr nsError)
    {
        if (nsError == IntPtr.Zero)
        {
            return null;
        }

        var description = Send(nsError, Selector("localizedDescription"));
        return description == IntPtr.Zero ? null : Utf8(description);
    }

    private static string? Utf8(IntPtr nsString)
    {
        var pointer = Send(nsString, Selector("UTF8String"));
        return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
    }

    [DllImport(Library)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Library)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Library)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(Library)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendTrash(
        IntPtr receiver,
        IntPtr selector,
        IntPtr url,
        out IntPtr resulting,
        out IntPtr error);
}
