namespace Quotinator.Data.Models;

/// <summary>Dapper row shape for a <c>MIN(...)/MAX(...)</c> date-range query — #249's date-range discovery endpoint.</summary>
internal sealed class DateRangeRow
{
    /// <summary>The earliest matching timestamp, or <c>null</c> when the table is empty.</summary>
    public DateTime? Earliest { get; init; }

    /// <summary>The latest matching timestamp, or <c>null</c> when the table is empty.</summary>
    public DateTime? Latest { get; init; }
}
