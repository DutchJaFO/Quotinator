using System.Text;

namespace Quotinator.Data.Csv;

/// <summary>
/// Minimal RFC 4180 CSV parser — handles quoted fields containing commas, newlines, and escaped
/// (<c>""</c>) quotes. No external dependency is justified for a flat, fixed-column format. Shared
/// between <c>Quotinator.Converters.Csv</c> (source-file import) and #163's bulk-decide export/import
/// (developer decision: DRY, extracted rather than duplicated) — moved here from
/// <c>Quotinator.Converters.Csv</c> so it is reachable from that project, <c>Quotinator.Core</c>, and
/// <c>Quotinator.Api</c> alike.
/// </summary>
public static class CsvLineParser
{
    /// <summary>Parses <paramref name="content"/> into rows of raw string fields. The first row is not treated specially — callers decide which row is the header.</summary>
    public static List<List<string>> Parse(string content)
    {
        var rows      = new List<List<string>>();
        var row       = new List<string>();
        var field     = new StringBuilder();
        var inQuotes  = false;
        var i         = 0;

        while (i < content.Length)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    i++;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    i++;
                    break;
                case '\r':
                    i++;
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    i++;
                    break;
                default:
                    field.Append(c);
                    i++;
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
