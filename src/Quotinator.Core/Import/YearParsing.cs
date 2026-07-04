namespace Quotinator.Core.Import;

/// <summary>Normalises a raw upstream year value to Quotinator's canonical <c>date</c> string, or <c>null</c> if out of range or unparseable. Ported verbatim from the historical <c>scripts/seed.csx</c> algorithm.</summary>
public static class YearParsing
{
    /// <summary>Validates a numeric year is between 1900 and 2100 (exclusive), returning it as a string, or <c>null</c> otherwise.</summary>
    public static string? CleanYear(int? year)
    {
        if (year is null) return null;
        return year is > 1900 and < 2100 ? year.Value.ToString() : null;
    }

    /// <summary>Trims and parses a string year, validating it is between 1900 and 2100 (exclusive), or returns <c>null</c> if unparseable/out of range.</summary>
    public static string? CleanYear(string? year)
    {
        if (year is null) return null;
        var s = year.Trim();
        return int.TryParse(s, out var parsed) && parsed is > 1900 and < 2100 ? parsed.ToString() : null;
    }
}
