using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Core.Database;

/// <summary>
/// Converts a <see cref="SourceQuoteDto"/> to and from the field-name → value representation that
/// <see cref="FieldMergeResolver"/> (in <c>Quotinator.Data</c>, which has no dependency on
/// <c>Quotinator.Core</c>'s quote schema) operates over.
/// </summary>
internal static class QuoteFieldMerge
{
    private const string QuoteTextField        = "quoteText";
    private const string OriginalLanguageField = "originalLanguage";
    private const string SourceField           = "source";
    private const string DateField             = "date";
    private const string CharacterField        = "character";
    private const string AuthorField           = "author";
    private const string TypeField             = "type";
    private const string GenresField           = "genres";

    /// <summary>
    /// The quote-content fields where a difference in letter case alone is a genuine, ambiguous
    /// difference rather than this project's usual case-insensitive-by-default equality (#374,
    /// developer decision 2026-09-04) — a case-only change could be a correction (an upstream typo
    /// finally fixed) or an unwanted downgrade (a lower-quality source overwriting a curated
    /// correction), and only a human can tell which.
    /// <para>
    /// <see cref="SourceField"/> is deliberately excluded, despite being named directly in the
    /// developer's own wording ("quote, title or character") — found live, 2026-09-04, against the real
    /// bundled corpus: this field is never independently persisted per quote (<c>Sql.Quotes.SelectRawById</c>
    /// builds it from <c>s.Title AS Source</c>, a join to the Source row a quote has already resolved
    /// to, matched case-insensitively per this project's identity-matching convention). Two quote lines
    /// for the same film routinely spell its title with different, inconsequential casing in real
    /// upstream data — measured 14 such cases in the bundled NikhilNamal17 corpus alone (e.g. "The Dark
    /// Knight" vs "the dark knight") — and every one of them resolves to the identical, correct Source
    /// regardless. Making this field case-sensitive turned every one of those into a permanent false
    /// "needs review" conflict with nothing genuine to decide, which is the opposite of what the
    /// developer's decision was for. The genuine case this decision targets — the same quote's own
    /// content disagreeing with itself in a way that could be a real correction — is exactly what
    /// <see cref="QuoteTextField"/> and <see cref="CharacterField"/> already cover, since both are
    /// stored per quote, never derived from a join.
    /// </para>
    /// <para>
    /// <see cref="OriginalLanguageField"/>, <see cref="DateField"/>, <see cref="AuthorField"/>,
    /// <see cref="TypeField"/>, and <see cref="GenresField"/> are also excluded — none of them carry
    /// free-text content where casing itself is part of what the field means to a reader.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> CaseSensitiveContentFields =
        new HashSet<string> { QuoteTextField, CharacterField };

    /// <summary>
    /// Maps the mergeable fields of a <see cref="SourceQuoteDto"/> to a field-name → value dictionary.
    /// <c>Id</c> and <c>Translations</c> are deliberately excluded — <c>Id</c> is the join key (both
    /// sides always share it), and per-language translation merging is a distinct, unspecced feature;
    /// the merged quote always carries the incoming side's translations, unconditionally, same as
    /// the existing newest-wins path already does.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ToFieldMap(SourceQuoteDto q) => new Dictionary<string, object?>
    {
        [QuoteTextField]        = q.QuoteText,
        [OriginalLanguageField] = q.OriginalLanguage,
        [SourceField]           = q.Source,
        [DateField]             = q.Date,
        [CharacterField]        = q.Character,
        [AuthorField]           = q.Author,
        [TypeField]             = q.Type.ToString().ToLowerInvariant(),
        [GenresField]           = q.Genres.ToList(),
    };

    /// <summary>Builds a merged <see cref="SourceQuoteDto"/> from <paramref name="merged"/>'s resolved field values, keeping <paramref name="incoming"/>'s <c>Id</c> and <c>Translations</c>.</summary>
    public static SourceQuoteDto ApplyMergedFields(IReadOnlyDictionary<string, object?> merged, SourceQuoteDto incoming) => new()
    {
        Id               = incoming.Id,
        QuoteText        = (string)merged[QuoteTextField]!,
        OriginalLanguage = (string)merged[OriginalLanguageField]!,
        Source           = (string)merged[SourceField]!,
        Date             = (string?)merged[DateField],
        Character        = (string?)merged[CharacterField],
        Author           = (string?)merged[AuthorField],
        Type             = QuoteSeedWriter.ParseQuoteType((string)merged[TypeField]!),
        Genres           = (List<string>)merged[GenresField]!,
        Translations     = incoming.Translations,
    };

    /// <summary>Maps the mergeable fields of a <see cref="SourceQuoteDto"/> directly to a <see cref="QuoteConflictFieldsDto"/> (#154's staged-payload shape, unlike <see cref="ToFieldMap(SourceQuoteDto)"/>'s generic dictionary).</summary>
    public static QuoteConflictFieldsDto ToDto(SourceQuoteDto q) => new()
    {
        QuoteText        = q.QuoteText,
        OriginalLanguage = q.OriginalLanguage,
        Source           = q.Source,
        Date             = q.Date,
        Character        = q.Character,
        Author           = q.Author,
        Type             = q.Type,
        Genres           = [.. q.Genres],
    };

    /// <summary>Rebuilds a <see cref="QuoteConflictFieldsDto"/> from a field-name → value map (the reverse of <see cref="ToFieldMap(QuoteConflictFieldsDto)"/>).</summary>
    public static QuoteConflictFieldsDto ToDto(IReadOnlyDictionary<string, object?> fields) => new()
    {
        QuoteText        = (string?)fields[QuoteTextField],
        OriginalLanguage = (string?)fields[OriginalLanguageField],
        Source           = (string?)fields[SourceField],
        Date             = (string?)fields[DateField],
        Character        = (string?)fields[CharacterField],
        Author           = (string?)fields[AuthorField],
        Type             = fields[TypeField] is string t ? QuoteSeedWriter.ParseQuoteType(t) : null,
        Genres           = (List<string>)fields[GenresField]!,
    };

    /// <summary>Maps a <see cref="QuoteConflictFieldsDto"/> to a field-name → value dictionary — the reverse of <see cref="ToDto(IReadOnlyDictionary{string, object})"/>, needed when re-validating a manually decided action via <see cref="FieldMergeResolver"/>.</summary>
    public static IReadOnlyDictionary<string, object?> ToFieldMap(QuoteConflictFieldsDto dto) => new Dictionary<string, object?>
    {
        [QuoteTextField]        = dto.QuoteText,
        [OriginalLanguageField] = dto.OriginalLanguage,
        [SourceField]           = dto.Source,
        [DateField]             = dto.Date,
        [CharacterField]        = dto.Character,
        [AuthorField]           = dto.Author,
        [TypeField]             = dto.Type?.ToString().ToLowerInvariant(),
        [GenresField]           = dto.Genres,
    };
}
