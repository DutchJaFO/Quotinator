using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Data.Entities;

namespace Quotinator.Core.Database;

/// <summary>
/// Maps between <see cref="ImportActionFieldRow"/> (#163's flat export/bulk-decide format) and
/// <see cref="ConflictDecisionRequest"/> — the reverse direction of the per-entity <c>ToFieldMap</c>
/// methods in <see cref="Quotinator.Core.Services.SqliteImportActionService"/>, which already produce
/// the same camelCase field-name vocabulary used here for <c>GET /import/actions</c>' own
/// <c>ExistingFields</c>/<c>IncomingFields</c>/<c>AmbiguousFields</c>.
/// </summary>
public static class ImportActionFieldRowMapper
{
    /// <summary>Delimiter for a list-valued field (Quote's <c>genres</c>) encoded as plain text.</summary>
    public const char GenresSeparator = ';';

    /// <summary>
    /// The currently-decidable field names for each entity type — every member of
    /// <see cref="ImportActionEntityTypes.All"/> is decidable as of #163. Field names are scoped to
    /// their entity type: the same name can mean something different on another type (e.g. <c>"name"</c>
    /// on <c>Person</c> versus <c>Character</c>), so validity is always checked as an
    /// (EntityType, Field) pair, never Field alone.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DecidableFieldsByEntityType =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [ImportActionEntityTypes.Quote]          = ["quoteText", "originalLanguage", "source", "date", "character", "author", "type", "genres"],
            [ImportActionEntityTypes.Source]         = ["title", "type", "date", "seriesId"],
            [ImportActionEntityTypes.Person]         = ["name", "dateOfBirth", "dateOfDeath"],
            [ImportActionEntityTypes.Character]      = ["name"],
            [ImportActionEntityTypes.Series]         = ["name", "universeId"],
            [ImportActionEntityTypes.Universe]       = ["name"],
            [ImportActionEntityTypes.StageDirection] = ["text", "imageUrl"],
            [ImportActionEntityTypes.SoundCue]       = ["text", "soundFileUrl", "imageUrl"],
            [ImportActionEntityTypes.Conversation]   = ["description"],
        };

    /// <summary>Encodes a genre list as a <see cref="GenresSeparator"/>-delimited string, or <c>null</c> for a <c>null</c> list.</summary>
    public static string? EncodeGenres(IReadOnlyList<string>? genres) =>
        genres is null ? null : string.Join(GenresSeparator, genres);

    /// <summary>Decodes a <see cref="GenresSeparator"/>-delimited string back to a genre list — the reverse of <see cref="EncodeGenres"/>.</summary>
    public static List<string>? DecodeGenres(string? encoded) =>
        encoded is null ? null : encoded.Length == 0 ? [] : [.. encoded.Split(GenresSeparator)];

    /// <summary>
    /// Builds a <see cref="ConflictDecisionRequest"/> from one action's flat field rows (#163's
    /// bulk-decide direction). <paramref name="rows"/> must all share the same <c>ActionId</c> and
    /// <paramref name="entityType"/> — the caller groups rows by <c>ActionId</c> before calling this.
    /// A row whose <see cref="ImportActionFieldRow.Decision"/> is <c>null</c> supplies no override for
    /// that field, matching <see cref="ConflictDecisionRequest"/>'s own null-means-auto-resolve
    /// contract. <see cref="ConflictDecisionRequest.MarkCompletenessAs"/> is taken from the first row
    /// that carries a non-null value, per #163's "one value per ActionId group, repeated on every row"
    /// contract.
    /// </summary>
    /// <exception cref="ImportActionUnknownEntityTypeException"><paramref name="entityType"/> is not a recognised entity type.</exception>
    /// <exception cref="ImportActionUnknownFieldException">A row's <c>Field</c> is not decidable for <paramref name="entityType"/>.</exception>
    public static ConflictDecisionRequest BuildRequest(string entityType, IReadOnlyList<ImportActionFieldRow> rows)
    {
        if (!DecidableFieldsByEntityType.TryGetValue(entityType, out var validFields))
            throw new ImportActionUnknownEntityTypeException(entityType);

        FieldDecision? quoteText = null, originalLanguage = null, source = null, date = null, character = null, author = null, type = null;
        GenresFieldDecision? genres = null;
        FieldDecision? sourceTitle = null, sourceType = null, sourceDate = null, sourceSeriesId = null;
        FieldDecision? personName = null, personDateOfBirth = null, personDateOfDeath = null;
        FieldDecision? characterName = null;
        FieldDecision? seriesName = null, seriesUniverseId = null;
        FieldDecision? universeName = null;
        FieldDecision? stageDirectionText = null, stageDirectionImageUrl = null;
        FieldDecision? soundCueText = null, soundCueSoundFileUrl = null, soundCueImageUrl = null;
        FieldDecision? conversationDescription = null;
        CompletenessStatus? markCompletenessAs = null;

        foreach (var row in rows)
        {
            if (!validFields.Contains(row.Field))
                throw new ImportActionUnknownFieldException(entityType, row.Field);

            markCompletenessAs ??= row.MarkCompletenessAs;

            if (row.Decision is null) continue;

            if (row.Field == "genres")
            {
                genres = new GenresFieldDecision { Choice = row.Decision.Value, Value = DecodeGenres(row.CustomValue) };
                continue;
            }

            var decision = new FieldDecision { Choice = row.Decision.Value, Value = row.CustomValue };

            switch (entityType, row.Field)
            {
                case (ImportActionEntityTypes.Quote, "quoteText"):          quoteText = decision; break;
                case (ImportActionEntityTypes.Quote, "originalLanguage"):   originalLanguage = decision; break;
                case (ImportActionEntityTypes.Quote, "source"):             source = decision; break;
                case (ImportActionEntityTypes.Quote, "date"):               date = decision; break;
                case (ImportActionEntityTypes.Quote, "character"):          character = decision; break;
                case (ImportActionEntityTypes.Quote, "author"):             author = decision; break;
                case (ImportActionEntityTypes.Quote, "type"):               type = decision; break;
                case (ImportActionEntityTypes.Source, "title"):             sourceTitle = decision; break;
                case (ImportActionEntityTypes.Source, "type"):              sourceType = decision; break;
                case (ImportActionEntityTypes.Source, "date"):              sourceDate = decision; break;
                case (ImportActionEntityTypes.Source, "seriesId"):          sourceSeriesId = decision; break;
                case (ImportActionEntityTypes.Person, "name"):              personName = decision; break;
                case (ImportActionEntityTypes.Person, "dateOfBirth"):       personDateOfBirth = decision; break;
                case (ImportActionEntityTypes.Person, "dateOfDeath"):       personDateOfDeath = decision; break;
                case (ImportActionEntityTypes.Character, "name"):           characterName = decision; break;
                case (ImportActionEntityTypes.Series, "name"):              seriesName = decision; break;
                case (ImportActionEntityTypes.Series, "universeId"):        seriesUniverseId = decision; break;
                case (ImportActionEntityTypes.Universe, "name"):            universeName = decision; break;
                case (ImportActionEntityTypes.StageDirection, "text"):      stageDirectionText = decision; break;
                case (ImportActionEntityTypes.StageDirection, "imageUrl"):  stageDirectionImageUrl = decision; break;
                case (ImportActionEntityTypes.SoundCue, "text"):            soundCueText = decision; break;
                case (ImportActionEntityTypes.SoundCue, "soundFileUrl"):    soundCueSoundFileUrl = decision; break;
                case (ImportActionEntityTypes.SoundCue, "imageUrl"):        soundCueImageUrl = decision; break;
                case (ImportActionEntityTypes.Conversation, "description"): conversationDescription = decision; break;
            }
        }

        return new ConflictDecisionRequest
        {
            QuoteText               = quoteText,
            OriginalLanguage        = originalLanguage,
            Source                  = source,
            Date                    = date,
            Character               = character,
            Author                  = author,
            Type                    = type,
            Genres                  = genres,
            SourceTitle             = sourceTitle,
            SourceType              = sourceType,
            SourceDate              = sourceDate,
            SourceSeriesId          = sourceSeriesId,
            PersonName              = personName,
            PersonDateOfBirth       = personDateOfBirth,
            PersonDateOfDeath       = personDateOfDeath,
            CharacterName           = characterName,
            SeriesName              = seriesName,
            SeriesUniverseId        = seriesUniverseId,
            UniverseName            = universeName,
            StageDirectionText      = stageDirectionText,
            StageDirectionImageUrl  = stageDirectionImageUrl,
            SoundCueText            = soundCueText,
            SoundCueSoundFileUrl    = soundCueSoundFileUrl,
            SoundCueImageUrl        = soundCueImageUrl,
            ConversationDescription = conversationDescription,
            MarkCompletenessAs      = markCompletenessAs,
        };
    }
}
