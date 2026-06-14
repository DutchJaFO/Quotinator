using Dapper.Contrib.Extensions;
using Quotinator.Core.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Core.Data.Entities;

[Table("Sources")]
public sealed class Source : RecordBase
{
    public string Title { get; init; } = string.Empty;

    /// <summary>QuoteType stored as TEXT (enum name). Parsed safely on read; <see cref="SafeValue{T}.Raw"/> preserved if unrecognised.</summary>
    public SafeValue<QuoteType?> Type { get; init; } = SafeValue<QuoteType?>.Empty;

    /// <summary>Publication / release date. Imprecise ISO 8601 text (e.g. "1994", "1994-06"). Separate from audit timestamps.</summary>
    public SafeValue<DateTime?> Date { get; init; } = SafeDateValue.Empty;
}
