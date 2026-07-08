using Quotinator.Core.Import;
using Quotinator.Data.Import;

namespace Quotinator.Engine.Database;

/// <summary>
/// Converts a <see cref="SourceQuote"/> to and from the field-name → value representation that
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
    /// Maps the mergeable fields of a <see cref="SourceQuote"/> to a field-name → value dictionary.
    /// <c>Id</c> and <c>Translations</c> are deliberately excluded — <c>Id</c> is the join key (both
    /// sides always share it), and per-language translation merging is a distinct, unspecced feature;
    /// the merged quote always carries the incoming side's translations, unconditionally, same as
    /// the existing newest-wins path already does.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ToFieldMap(SourceQuote q) => new Dictionary<string, object?>
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

    /// <summary>Builds a merged <see cref="SourceQuote"/> from <paramref name="merged"/>'s resolved field values, keeping <paramref name="incoming"/>'s <c>Id</c> and <c>Translations</c>.</summary>
    public static SourceQuote ApplyMergedFields(IReadOnlyDictionary<string, object?> merged, SourceQuote incoming) => new()
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
}
