using System.Globalization;
using System.Text;
using AirClean.Core.Models;

namespace AirClean.Core.Deleting;

/// <summary>
/// Builds the CSV a user can export from the completion page. The format is deliberately
/// machine-readable: reason values stay as their enum names, and paths are quoted only
/// when they need it. Callers write the string with a UTF-8 BOM so Excel opens CJK paths.
/// </summary>
public static class SkipReportCsv
{
    public static string Build(IReadOnlyList<SkippedItem> items, DateTimeOffset at)
    {
        var builder = new StringBuilder("path,reason,at\r\n");
        var timestamp = at.ToString("O", CultureInfo.InvariantCulture);

        foreach (var item in items)
        {
            builder
                .Append(Escape(item.Path))
                .Append(',')
                .Append(item.Reason)
                .Append(',')
                .Append(timestamp)
                .Append("\r\n");
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
