using System.Buffers.Binary;
using System.Text;

namespace AirClean.Core.Platform.Windows;

/// <summary>
/// Parser for the <c>$I</c> metadata files Windows keeps next to every <c>$R</c> item in
/// the recycle bin. Format 1 stores a fixed 260-character path; format 2 stores a
/// variable-length path. Both are read defensively: a malformed file returns null instead
/// of throwing, because the recycle bin is not ours to trust.
/// </summary>
public sealed record RecycleBinEntry(int Version, long OriginalSize, DateTimeOffset DeletedAt, string OriginalPath)
{
    private const int HeaderSize = 8 + 8 + 8;
    private const int PathLengthOffset = 24;
    private const int PathOffset = 28;
    private const int VersionOnePathLength = 260;
    private const int MaximumPathLength = 32_768;

    public static RecycleBinEntry? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < PathOffset)
        {
            return null;
        }

        var version = (int)BinaryPrimitives.ReadInt64LittleEndian(data[..8]);
        var originalSize = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(8, 8));
        var fileTime = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(16, 8));

        if (version is not (1 or 2) || originalSize < 0)
        {
            return null;
        }

        DateTimeOffset deletedAt;
        try
        {
            deletedAt = new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }

        var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(PathLengthOffset, 4));
        var characters = version == 1 ? VersionOnePathLength : declaredLength;

        if (characters <= 0 || characters > MaximumPathLength)
        {
            return null;
        }

        var byteCount = characters * 2;
        if (data.Length < PathOffset + byteCount)
        {
            return null;
        }

        var path = Encoding.Unicode
            .GetString(data.Slice(PathOffset, byteCount))
            .TrimEnd('\0', '\uFFFD');

        if (string.IsNullOrWhiteSpace(path) || !IsAbsoluteWindowsPath(path))
        {
            return null;
        }

        return new RecycleBinEntry(version, originalSize, deletedAt, path);
    }

    /// <summary>
    /// Checked with Windows' own rules rather than <see cref="Path.IsPathFullyQualified"/>:
    /// this parser is unit-tested on Linux and macOS too, where a path such as
    /// <c>C:\Temp\a.bin</c> is not "rooted".
    /// </summary>
    private static bool IsAbsoluteWindowsPath(string path)
    {
        if (path.Length >= 3 &&
            char.IsAsciiLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 2;
    }

    public static RecycleBinEntry? TryParseFile(string path)
    {
        try
        {
            return TryParse(File.ReadAllBytes(path));
        }
        catch
        {
            return null;
        }
    }
}
