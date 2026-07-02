namespace Quotinator.Tools.DbInspector;

/// <summary>Renders a Dapper dynamic result set as an aligned, column-padded text table.</summary>
internal static class TableFormatter
{
    /// <summary>Returns "(no rows)" for an empty result set, otherwise a header line followed by one line per row, columns padded to the widest value in each column.</summary>
    internal static string Format(IEnumerable<IDictionary<string, object>> rows)
    {
        var rowList = rows.ToList();
        if (rowList.Count == 0)
            return "(no rows)";

        var columns    = rowList[0].Keys.ToList();
        var widths     = columns.ToDictionary(c => c, c => c.Length);
        var cellValues = rowList
            .Select(row => columns.ToDictionary(c => c, c => row[c]?.ToString() ?? "NULL"))
            .ToList();

        foreach (var row in cellValues)
            foreach (var col in columns)
                widths[col] = Math.Max(widths[col], row[col].Length);

        var lines = new List<string> { string.Join("  ", columns.Select(c => c.PadRight(widths[c]))) };
        lines.AddRange(cellValues.Select(row => string.Join("  ", columns.Select(c => row[c].PadRight(widths[c])))));

        return string.Join(Environment.NewLine, lines);
    }
}
