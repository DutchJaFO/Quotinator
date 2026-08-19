using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quotinator.Changelog.Models;
using Quotinator.Changelog.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Logging;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Import;

/// <summary>
/// Refreshes the separate changelog database's <c>Changelog</c>/<c>ChangelogLine</c> tables from
/// <see cref="IChangelogService"/>'s already-parsed <c>changelog.*.json</c> content (#309, ADR 018).
/// Deliberately concrete, not a generic "system content importer" abstraction — this issue is the
/// pattern's first real consumer; a shared abstraction is extracted only once a second consumer
/// (e.g. Genre, #310) actually needs one.
/// </summary>
/// <remarks>Initialises the importer with the changelog database's own keyed connection factory, the JSON-backed source service, the repository to write through, and a logger.</remarks>
/// <param name="factory">The keyed <see cref="IDbConnectionFactory"/> for the changelog database (see <see cref="DatabaseConnectionKeys.Changelog"/>) — used only to clear existing content before a fresh import.</param>
/// <param name="changelogService">Provides the already-parsed content for every loaded language.</param>
/// <param name="repository">Writes each release/unreleased entry and its lines atomically.</param>
/// <param name="logger">Logger for startup diagnostics.</param>
public sealed class ChangelogSystemContentImporter(
    [FromKeyedServices(DatabaseConnectionKeys.Changelog)] IDbConnectionFactory factory,
    IChangelogService changelogService,
    ChangelogRepository repository,
    ILogger<ChangelogSystemContentImporter> logger)
{
    /// <summary>
    /// Clears any existing content and re-imports every loaded language's document — releases plus,
    /// where present, the <c>unreleased</c> entry. Safe to call more than once per process (e.g. in
    /// tests): the clear step means a re-run overwrites rather than duplicates or violates the
    /// <c>Changelog</c> table's <c>(Language, Version)</c> uniqueness constraints.
    /// </summary>
    public async Task RefreshAsync()
    {
        int entryCount = 0;

        // The clear and every insert are one transaction, so a reader never observes the rebuild
        // in progress. Found live (#309, step 17): with each statement committing on its own, a
        // startup read landed mid-rebuild and was served 615 of an eventual 2292 rows — a partial
        // changelog, indistinguishable from a complete one, which the what's-new producer then chose
        // its announcements from. Atomicity is the fix; the readiness signal only covers the
        // separate case of a database that is empty because nothing has been written yet.
        await TransactionScope.ExecuteAsync(factory, async unitOfWork =>
        {
            // Connection/Transaction are internal to this assembly, which is how every repository here
            // reaches them. The raw clears have to run on the same transaction as the inserts below or
            // the atomicity this scope exists for would not include them.
            SqliteUnitOfWork sqliteUnitOfWork = (SqliteUnitOfWork)unitOfWork;

            await sqliteUnitOfWork.Connection.ExecuteAsync(
                Sql.ChangelogContent.ClearLines, transaction: sqliteUnitOfWork.Transaction);
            await sqliteUnitOfWork.Connection.ExecuteAsync(
                Sql.ChangelogContent.ClearChangelogs, transaction: sqliteUnitOfWork.Transaction);

            foreach (string language in changelogService.AvailableLanguages)
            {
                ChangelogDocument? document = changelogService.GetForCulture(language);
                if (document is null) continue;

                if (document.Unreleased is not null)
                {
                    await WriteEntryAsync(document.Language, version: null, date: null, document.MachineTranslated, document.Unreleased, quote: null, unitOfWork);
                    entryCount++;
                }

                foreach (ChangelogRelease release in document.Releases)
                {
                    await WriteEntryAsync(document.Language, release.Version, release.Date, document.MachineTranslated, release, release.Quote, unitOfWork);
                    entryCount++;
                }
            }
        });

        logger.LogChangelogContentRefreshed(entryCount, changelogService.AvailableLanguages.Count);
    }

    private async Task WriteEntryAsync(
        string language, string? version, string? date, bool machineTranslated, ChangelogUnreleased content, ChangelogQuote? quote,
        IUnitOfWork unitOfWork)
    {
        ChangelogEntryEntity entity = new ChangelogEntryEntity
        {
            Language          = language,
            Version           = version,
            Date              = date,
            MachineTranslated = machineTranslated,
            QuoteText         = quote?.Text,
            QuoteAttribution  = quote?.Attribution,
        };

        await repository.InsertWithLinesAsync(entity, BuildLines(entity.Id, content), unitOfWork);
    }

    private static List<ChangelogLineEntity> BuildLines(Guid changelogId, ChangelogUnreleased content)
    {
        List<ChangelogLineEntity> lines = [];

        AddOrderedLines(lines, changelogId, ChangelogLineKind.Highlight, content.Highlights);
        AddOrderedLines(lines, changelogId, ChangelogLineKind.Added, content.Added);
        AddOrderedLines(lines, changelogId, ChangelogLineKind.Changed, content.Changed);
        AddOrderedLines(lines, changelogId, ChangelogLineKind.Fixed, content.Fixed);
        AddOrderedLines(lines, changelogId, ChangelogLineKind.Removed, content.Removed);
        AddOrderedLines(lines, changelogId, ChangelogLineKind.Issue, content.Issues.Select(i => i.ToString()));
        AddOrderedLines(lines, changelogId, ChangelogLineKind.Cve, content.Cves);

        foreach ((string? audienceKey, List<string>? values) in content.AudienceHighlights)
            AddOrderedLines(lines, changelogId, ChangelogLineKind.AudienceHighlight, values, audienceKey);

        return lines;
    }

    // SortOrder restarts at 0 for every (Kind, AudienceKey) list — it preserves that one list's own
    // original order, not a global write order across every kind.
    private static void AddOrderedLines(
        List<ChangelogLineEntity> lines, Guid changelogId, ChangelogLineKind kind, IEnumerable<string> values, string? audienceKey = null)
    {
        int sortOrder = 0;
        foreach (string value in values)
        {
            lines.Add(new ChangelogLineEntity
            {
                ChangelogEntryId = changelogId,
                Kind        = new SafeValue<ChangelogLineKind?>(kind.ToString(), kind),
                AudienceKey = audienceKey,
                Value       = value,
                SortOrder   = sortOrder++,
            });
        }
    }
}
