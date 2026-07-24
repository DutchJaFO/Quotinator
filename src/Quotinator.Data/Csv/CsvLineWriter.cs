using System.Text;

namespace Quotinator.Data.Csv;

/// <summary>
/// Minimal RFC 4180 CSV writer — the write-side counterpart to <see cref="CsvLineParser"/>. Quotes a
/// field only when it contains a comma, double-quote, or newline (the minimum RFC 4180 requires),
/// escaping an embedded quote by doubling it. Rows are terminated with <c>\r\n</c>.
/// </summary>
public static class CsvLineWriter
{
    /// <summary>Writes <paramref name="rows"/> as CSV text. A <c>null</c> field is written as an empty (unquoted) field — the reverse of <see cref="CsvLineParser.Parse"/>, which never distinguishes an empty field from a missing one.</summary>
    public static string Write(IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            WriteRow(sb, row);
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static void WriteRow(StringBuilder sb, IEnumerable<string?> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first) sb.Append(',');
            first = false;
            WriteField(sb, field);
        }
    }

    private static void WriteField(StringBuilder sb, string? field)
    {
        if (field is null) return;

        if (field.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            sb.Append(field);
            return;
        }

        sb.Append('"');
        sb.Append(field.Replace("\"", "\"\""));
        sb.Append('"');
    }
}
