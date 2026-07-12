using Dapper.Contrib.Extensions;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>A single quote entry in the database.</summary>
[Table("Quotes")]
public sealed class QuoteEntity : RecordBase
{
    /// <summary>The verbatim quote text in its original language.</summary>
    public string QuoteText        { get; init; } = string.Empty;

    /// <summary>ISO 639-1 code of the language in which the quote was originally recorded.</summary>
    public string OriginalLanguage { get; init; } = "en";

    /// <summary>The source (film, series, book, etc.) from which the quote is drawn.</summary>
    public Guid   SourceId         { get; init; }

    /// <summary>The fictional character who delivers the quote. Null for person-type entries.</summary>
    public Guid?  CharacterId      { get; init; }

    /// <summary>The real person who said or wrote the quote. Null for fictional sources.</summary>
    public Guid?  PersonId         { get; init; }

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid?  ImportBatchId    { get; init; }

    /// <summary>
    /// Whether the record's fields are known to be fully populated and reviewed (#55/#165).
    /// <see cref="Quotinator.Data.Entities.CompletenessStatus.Complete"/> is human-set only.
    /// </summary>
    public SafeValue<CompletenessStatus?> CompletenessStatus { get; init; } = SafeValue<CompletenessStatus?>.Empty;

    /// <summary>Field names confirmed to have no findable value (e.g. <c>"date"</c>, <c>"character"</c>) — enrichment must not attempt these.</summary>
    public IReadOnlyList<string> NoValueKnown { get; init; } = [];
}
