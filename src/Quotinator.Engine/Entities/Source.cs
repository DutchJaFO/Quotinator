using Dapper.Contrib.Extensions;
using Quotinator.Core.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>A film, television series, book, or other source from which quotes are drawn.</summary>
[Table("Sources")]
public sealed class Source : RecordBase
{
    /// <summary>The title of the source in its original language.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Media category stored as TEXT (enum name). <see cref="SafeValue{T}.Raw"/> is preserved if the stored value is unrecognised.</summary>
    public SafeValue<QuoteType?> Type { get; init; } = SafeValue<QuoteType?>.Empty;

    /// <summary>Publication or release date. Imprecise ISO 8601 text (e.g. "1994", "1994-06"). Separate from audit timestamps.</summary>
    public SafeValue<DateTime?> Date { get; init; } = SafeDateValue.Empty;

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid? ImportBatchId { get; init; }

    /// <summary>
    /// Whether the record's fields are known to be fully populated and reviewed (#55/#165).
    /// <see cref="Quotinator.Data.Entities.CompletenessStatus.Complete"/> is human-set only.
    /// </summary>
    public SafeValue<CompletenessStatus?> CompletenessStatus { get; init; } = SafeValue<CompletenessStatus?>.Empty;

    /// <summary>Field names confirmed to have no findable value — enrichment must not attempt these.</summary>
    public IReadOnlyList<string> NoValueKnown { get; init; } = [];
}
