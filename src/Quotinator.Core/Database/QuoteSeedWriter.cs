using System.Text.Json;
using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Helpers;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;
using Quotinator.Core.Queries;

namespace Quotinator.Core.Database;

/// <summary>
/// Shared insert/merge primitives for writing a <see cref="SourceQuote"/> (and its Source/Character/
/// Person/Translation/Genre rows) into the database, plus conflict logging and existing-row lookup.
/// Used by both <see cref="QuotinatorDatabaseInitializer"/>'s startup seeding and the
/// <c>POST /api/v1/import</c> endpoint's live import service — one copy of this logic, not two.
/// </summary>
internal static class QuoteSeedWriter
{
    // Reverse of InputValidation.GenreApiToDb — DB enum name (e.g. "SciFi") back to the wire-format
    // tag (e.g. "sci-fi"), needed to rebuild a QuoteFieldMerge-compatible field map from stored rows.
    private static readonly IReadOnlyDictionary<string, string> GenreDbToApi =
        InputValidation.GenreApiToDb.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Bundles the change-log writer and initiator identity shared across every Source/Character/Person/Quote write within one seeding run or one import call (#56).</summary>
    internal readonly record struct ChangeLogContext(ISystemChangeLogWriter Writer, InitiatorType InitiatedByType, string? InitiatedById);

    /// <summary>
    /// Writes one <see cref="SystemChangeLog"/> for a Created/Modified operation on a domain entity.
    /// <paramref name="oldValue"/>/<paramref name="newValue"/> are serialised as whole-record JSON
    /// snapshots (per #56's Scope changes — one row per operation, not one per field); pass <c>null</c>
    /// for whichever side doesn't apply (e.g. <paramref name="oldValue"/> for a brand-new row).
    /// </summary>
    internal static async Task LogChangeAsync(
        ChangeLogContext changeLog, string entityType, string entityId, ChangeAction action,
        object? oldValue, object? newValue, SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        await changeLog.Writer.LogAsync(new SystemChangeLog
        {
            EntityType      = entityType,
            EntityId        = entityId,
            InitiatedByType = new SafeValue<InitiatorType?>(changeLog.InitiatedByType.ToString(), changeLog.InitiatedByType),
            InitiatedById   = changeLog.InitiatedById,
            Action          = new SafeValue<ChangeAction?>(action.ToString(), action),
            OldValue        = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValue        = newValue is null ? null : JsonSerializer.Serialize(newValue),
            OccurredAt      = DateTime.UtcNow,
        }, connection, transaction);
    }

    /// <summary>
    /// Gets the existing Source row for <paramref name="q"/>'s title+type, or inserts a new one.
    /// Checks the database itself (not only <paramref name="index"/>) on a cache miss — <paramref name="index"/>
    /// alone is only a safe existence check when the caller knows the database started empty (the
    /// startup seeder's guarantee); a live import against an already-populated database needs the
    /// real check, or it would try to insert a second row for a title+type that already exists and
    /// violate the unique constraint.
    /// </summary>
    internal static async Task<Guid> GetOrCreateSourceAsync(
        SqliteConnection connection, SourceQuote q, Dictionary<string, Guid> index, Guid importBatchId,
        ChangeLogContext changeLog, SqliteTransaction? transaction = null)
    {
        var typeStr = q.Type.ToString();
        var key     = $"{q.Source}|{typeStr}";
        if (index.TryGetValue(key, out var existing)) return existing;

        var existingId = await connection.ExecuteScalarAsync<Guid?>(
            Sql.Sources.SelectIdByTitleAndType, new { title = q.Source, type = typeStr }, transaction);
        if (existingId is { } foundId)
        {
            index[key] = foundId;
            return foundId;
        }

        var id = Guid.NewGuid();
        await connection.InsertAsync(new Source
        {
            Id            = id,
            Title         = q.Source,
            Type          = new SafeValue<QuoteType?>(typeStr, q.Type),
            Date          = string.IsNullOrEmpty(q.Date) ? SafeDateValue.Empty : new SafeValue<DateTime?>(q.Date, null),
            ImportBatchId = importBatchId
        }, transaction);

        await LogChangeAsync(changeLog, "source", id.ToString(), ChangeAction.Created,
            oldValue: null, newValue: new { title = q.Source, type = typeStr, date = q.Date }, connection, transaction);

        index[key] = id;
        return id;
    }

    /// <summary>
    /// Gets the existing Person row for <paramref name="q"/>'s author name, or inserts a new one.
    /// Returns <c>null</c> when <paramref name="q"/> has no author. Checks the database on a cache
    /// miss — see <see cref="GetOrCreateSourceAsync"/> for why.
    /// </summary>
    internal static async Task<Guid?> GetOrCreatePersonAsync(
        SqliteConnection connection, SourceQuote q, Dictionary<string, Guid> index, Guid importBatchId,
        ChangeLogContext changeLog, SqliteTransaction? transaction = null)
    {
        if (string.IsNullOrWhiteSpace(q.Author)) return null;

        if (index.TryGetValue(q.Author, out var existing)) return existing;

        var existingId = await connection.ExecuteScalarAsync<Guid?>(
            Sql.People.SelectIdByName, new { name = q.Author }, transaction);
        if (existingId is { } foundId)
        {
            index[q.Author] = foundId;
            return foundId;
        }

        var id = Guid.NewGuid();
        await connection.InsertAsync(new Person
        {
            Id            = id,
            Name          = q.Author,
            ImportBatchId = importBatchId
        }, transaction);

        await LogChangeAsync(changeLog, "person", id.ToString(), ChangeAction.Created,
            oldValue: null, newValue: new { name = q.Author }, connection, transaction);

        index[q.Author] = id;
        return id;
    }

    /// <summary>Inserts every translation entry (and, when new, its source-title translation) for <paramref name="q"/>.</summary>
    internal static async Task InsertTranslationsAsync(
        SqliteConnection connection, SourceQuote q, Guid quoteId, Guid sourceId, string now,
        SqliteTransaction? transaction = null)
    {
        foreach (var (lang, t) in q.Translations)
        {
            await connection.ExecuteAsync(
                Sql.QuoteTranslations.Insert,
                new
                {
                    Id        = Guid.NewGuid().ToString(),
                    QuoteId   = quoteId.ToString(),
                    Language  = lang,
                    QuoteText = t.QuoteText,
                    DateCreated = now
                }, transaction);

            if (t.Source is not null)
            {
                var exists = await connection.ExecuteScalarAsync<int>(
                    Sql.SourceTranslations.CountForSource,
                    new { sid = sourceId, lang }, transaction);
                if (exists == 0)
                    await connection.InsertAsync(new SourceTranslation
                    {
                        SourceId = sourceId,
                        Language = lang,
                        Title    = t.Source
                    }, transaction);
            }
        }
    }

    /// <summary>Inserts one QuoteGenres row per recognised genre tag on <paramref name="q"/>, skipping unrecognised tags.</summary>
    internal static async Task InsertGenresAsync(
        SqliteConnection connection, SourceQuote q, Guid quoteId, string now, SqliteTransaction? transaction = null)
    {
        foreach (var genre in q.Genres)
        {
            if (TryNormaliseGenre(genre, out var g))
            {
                await connection.ExecuteAsync(
                    Sql.QuoteGenres.Insert,
                    new { Id = Guid.NewGuid().ToString(), QuoteId = quoteId.ToString(), Genre = g.ToString(), DateCreated = now },
                    transaction);
            }
        }
    }

    /// <summary>Result of <see cref="TryGetExistingFieldsAsync"/> — the existing quote's field map, the batch that originally created it, and its current completeness status.</summary>
    internal readonly record struct ExistingQuoteFields(IReadOnlyDictionary<string, object?> Fields, string? ImportBatchId, CompletenessStatus CompletenessStatus);

    /// <summary>
    /// Looks up an existing quote by Id and returns its current field values as a
    /// <c>QuoteFieldMerge</c>-compatible map (raw, untranslated — never a translated view), plus the
    /// batch that originally created it, or <c>null</c> when no such quote exists. Backed by
    /// <see cref="Sql.Quotes.SelectRawById()"/> (not <see cref="Sql.Quotes.SelectById()"/>, which
    /// COALESCEs in translated content).
    /// </summary>
    internal static async Task<ExistingQuoteFields?> TryGetExistingFieldsAsync(
        SqliteConnection connection, string id, SqliteTransaction? transaction = null)
    {
        var row = await connection.QueryFirstOrDefaultAsync<RawQuoteRow>(
            Sql.Quotes.SelectRawById(), new { id }, transaction);
        if (row is null) return null;

        var genreRows = await connection.QueryAsync<string>(Sql.QuoteGenres.LoadForQuote, new { id }, transaction);
        var genres    = genreRows.Select(g => GenreDbToApi.TryGetValue(g, out var tag) ? tag : g.ToLowerInvariant()).ToList();

        var fields = new Dictionary<string, object?>
        {
            ["quoteText"]        = row.QuoteText,
            ["originalLanguage"] = row.OriginalLanguage,
            ["source"]           = row.Source,
            ["date"]             = row.Date,
            ["character"]        = row.Character,
            ["author"]           = row.Author,
            ["type"]             = row.Type.Parsed?.ToString().ToLowerInvariant() ?? row.Type.Raw.ToLowerInvariant(),
            ["genres"]           = genres,
        };

        return new ExistingQuoteFields(fields, row.ImportBatchId, row.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete);
    }

    /// <summary>Maps a raw upstream <c>type</c> value to the DB enum's string name (e.g. <c>"Movie"</c>), used as part of the source dedup index key.</summary>
    internal static string NormaliseType(string raw) => ParseQuoteType(raw).ToString();

    /// <summary>Parses a raw upstream <c>type</c> value to <see cref="QuoteType"/>, falling back to <see cref="QuoteType.Unknown"/>.</summary>
    internal static QuoteType ParseQuoteType(string raw)
        => Enum.TryParse<QuoteType>(raw, ignoreCase: true, out var t) ? t : QuoteType.Unknown;

    /// <summary>Maps an API genre tag (e.g. <c>"sci-fi"</c>) to the <see cref="Genre"/> enum, via the shared <see cref="InputValidation.GenreApiToDb"/> table. Returns <c>false</c> for unrecognised tags.</summary>
    internal static bool TryNormaliseGenre(string raw, out Genre result)
    {
        if (InputValidation.GenreApiToDb.TryGetValue(raw, out var dbName) &&
            Enum.TryParse<Genre>(dbName, out result))
            return true;
        result = default;
        return false;
    }

    private sealed class RawQuoteRow
    {
        public string Id { get; init; } = string.Empty;
        public string QuoteText { get; init; } = string.Empty;
        public string OriginalLanguage { get; init; } = "en";
        public string Source { get; init; } = string.Empty;
        public string? Date { get; init; }
        public SafeValue<QuoteType?> Type { get; init; } = SafeValue<QuoteType?>.Empty;
        public string? Character { get; init; }
        public string? Author { get; init; }
        public string? ImportBatchId { get; init; }
        public SafeValue<CompletenessStatus?> CompletenessStatus { get; init; } = SafeValue<CompletenessStatus?>.Empty;
    }
}
