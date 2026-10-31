using System.Globalization;

namespace AirClean.Core.Formatting;

/// <summary>
/// Volumes are shown in decimal units (1 GB = 10^9 bytes), matching Finder and the way
/// drive capacities are advertised. One decimal below 10, none above, so the label stays
/// three or four characters wide.
/// </summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static (string Number, string Unit) Split(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        double value = bytes;
        var unit = 0;

        while (value >= 1000 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        var format = unit == 0 ? "0" : value < 100 ? "0.0" : "0";
        return (value.ToString(format, CultureInfo.InvariantCulture), Units[unit]);
    }

    public static string Format(long bytes)
    {
        var (number, unit) = Split(bytes);
        return number + " " + unit;
    }
}
