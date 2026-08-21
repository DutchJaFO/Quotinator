using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Changelog.Models;
using Quotinator.Changelog.Services;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Logging;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <inheritdoc/>
/// <remarks>Initialises the reader with the changelog database's own join repository, the JSON-backed fallback service, and a logger.</remarks>
/// <param name="joinRepository">Reads every <c>Changelog</c>/<c>ChangelogLine</c> row, flattened (ADR 017 — never a hand-rolled query).</param>
/// <param name="changelogService">Fallback used when the changelog database's tables don't exist yet or the query otherwise fails.</param>
/// <param name="importReadiness">Tells an empty-database read whether the import has concluded, so emptiness is only ever interpreted once it means something.</param>
/// <param name="logger">Logger stating which source answered each read — the database itself, or whichever fallback condition applied.</param>
public sealed class ChangelogReader(
    JoinQueryRepository<ChangelogLineRow> joinRepository,
    IChangelogService changelogService,
    IChangelogImportReadiness importReadiness,
    ILogger<ChangelogReader> logger) : IChangelogReader
{
    /// <inheritdoc/>
    public async Task<ChangelogDocument?> GetDocumentAsync(string? culture)
    {
        // Wait *before* reading, not after finding the result empty. Whatever is in the database before
        // this process's import concludes belongs to a previous run: complete, perhaps, but stale. On an
        // upgrade that stale copy has none of the new release's content, and which copy a read sees is
        // decided by a race between two detached startup tasks. Waiting first gives one stateable
        // invariant — a read reflects this process's own import — and removes the race rather than
        // narrowing it. Once an outcome is known this returns immediately, so only startup-window reads
        // pay anything at all.
        ChangelogImportOutcome outcome = await importReadiness.WaitAsync();

        switch (outcome)
        {
            case ChangelogImportOutcome.Failed:
                logger.LogChangelogImportFailedFallingBackToFile();
                return changelogService.GetForCulture(culture);

            case ChangelogImportOutcome.TimedOut:
                logger.LogChangelogImportWaitTimedOut();
                return changelogService.GetForCulture(culture);
        }

        IReadOnlyList<ChangelogLineRow> rows;
        try
        {
            rows = await joinRepository.QueryAsync();
        }
        catch (SqliteException ex) when (IsMissingChangelogTable(ex))
        {
            logger.LogChangelogTableMissingFallingBackToFile(ex);
            return changelogService.GetForCulture(culture);
        }

        // Entries, not rows: `rows` is the LEFT JOIN's own shape — roughly one per changelog *line* —
        // which no reader of the log can reconcile with the importer's "refreshed N entries". Counting
        // entries makes the two lines directly comparable.
        //
        // Counted once, and both decisions below read that one value. `rows` is already materialised by
        // QueryAsync, so this is an in-memory pass rather than a query. It also subsumes the previous
        // rows.Count check without changing behaviour: a non-empty list always yields at least one
        // distinct entry id, so entryCount == 0 exactly when rows is empty.
        int entryCount = rows.Select(row => row.ChangelogEntryId).Distinct().Count();

        // Empty after a successful import: the database is authoritative and there is genuinely nothing
        // to show. A new application has no changelog yet — that is an answer, not a fault, so it is
        // neither a warning nor a reason to consult the JSON files. About.razor already renders a null
        // document by omitting the changelog section entirely.
        if (entryCount == 0)
        {
            logger.LogChangelogDatabaseHasNoEntries();
            return null;
        }

        logger.LogChangelogServedFromDatabase(entryCount);

        Dictionary<string, ChangelogDocument> documents = AssembleDocuments(rows);
        string code = Normalise(culture);

        if (documents.TryGetValue(code, out ChangelogDocument? document))
            return document;

        return documents.GetValueOrDefault("en");
    }

    // Mirrors ChangelogService.Normalise exactly — kept as a private copy rather than a shared helper
    // since the two-letter-prefix rule is a two-line one-off, not worth a new shared abstraction for.
    private static string Normalise(string? culture) =>
        culture is { Length: > 2 } ? culture[..2] : culture ?? "en";

    private static Dictionary<string, ChangelogDocument> AssembleDocuments(IReadOnlyList<ChangelogLineRow> rows)
    {
        Dictionary<string, ChangelogDocument> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, ChangelogLineRow> languageGroup in rows.GroupBy(r => r.Language, StringComparer.OrdinalIgnoreCase))
        {
            ChangelogUnreleased? unreleased = null;
            List<ChangelogRelease> releases = [];
            bool machineTranslated = false;

            foreach (IGrouping<Guid, ChangelogLineRow> changelogGroup in languageGroup.GroupBy(r => r.ChangelogEntryId))
            {
                ChangelogLineRow first = changelogGroup.First();
                machineTranslated = first.MachineTranslated;
                ChangelogUnreleased content = BuildContent(changelogGroup);

                if (first.Version is null)
                {
                    unreleased = content;
                    continue;
                }

                releases.Add(new ChangelogRelease
                {
                    Version            = first.Version,
                    Date               = first.Date ?? string.Empty,
                    Issues             = content.Issues,
                    Cves               = content.Cves,
                    Highlights         = content.Highlights,
                    Added              = content.Added,
                    Changed            = content.Changed,
                    Fixed              = content.Fixed,
                    Removed            = content.Removed,
                    AudienceHighlights = content.AudienceHighlights,
                    Quote              = first.QuoteText is null
                        ? null
                        : new ChangelogQuote { Text = first.QuoteText, Attribution = first.QuoteAttribution },
                });
            }

            result[languageGroup.Key] = new ChangelogDocument
            {
                Language          = languageGroup.Key,
                MachineTranslated = machineTranslated,
                Unreleased        = unreleased,
                // Newest first, matching ChangelogDocument.Releases' own contract — ISO 8601 dates
                // sort correctly as plain strings, the same assumption this project already relies on
                // for RecordBase's own string-stored timestamps.
                Releases          = [.. releases.OrderByDescending(r => r.Date, StringComparer.Ordinal)],
            };
        }

        return result;
    }

    private static ChangelogUnreleased BuildContent(IEnumerable<ChangelogLineRow> lines)
    {
        List<string> highlights = [];
        List<string> added = [];
        List<string> changed = [];
        List<string> removedFields = [];
        List<string> fixedFields = [];
        List<int> issues = [];
        List<string> cves = [];
        Dictionary<string, List<string>> audienceHighlights = [];

        foreach (ChangelogLineRow line in lines.Where(l => l.Kind is not null).OrderBy(l => l.SortOrder))
        {
            if (line.Kind == nameof(ChangelogLineKind.Highlight))
                highlights.Add(line.Value!);
            else if (line.Kind == nameof(ChangelogLineKind.Added))
                added.Add(line.Value!);
            else if (line.Kind == nameof(ChangelogLineKind.Changed))
                changed.Add(line.Value!);
            else if (line.Kind == nameof(ChangelogLineKind.Fixed))
                fixedFields.Add(line.Value!);
            else if (line.Kind == nameof(ChangelogLineKind.Removed))
                removedFields.Add(line.Value!);
            else if (line.Kind == nameof(ChangelogLineKind.Issue))
                issues.Add(int.Parse(line.Value!));
            else if (line.Kind == nameof(ChangelogLineKind.Cve))
                cves.Add(line.Value!);
            else if (line.Kind == nameof(ChangelogLineKind.AudienceHighlight) && line.AudienceKey is not null)
            {
                if (!audienceHighlights.TryGetValue(line.AudienceKey, out List<string>? list))
                    audienceHighlights[line.AudienceKey] = list = [];
                list.Add(line.Value!);
            }
        }

        return new ChangelogUnreleased
        {
            Highlights         = highlights,
            Added              = added,
            Changed            = changed,
            Fixed              = fixedFields,
            Removed            = removedFields,
            Issues             = issues,
            Cves               = cves,
            AudienceHighlights = audienceHighlights,
        };
    }

    // Matches #293's NotificationReader.IsMissingNotificationTable idiom exactly: SqliteErrorCode 1
    // (SQLITE_ERROR) covers many message shapes, so the message text is also checked to stay narrowly
    // scoped to this one table. ChangelogLine is created in the same migration/baseline as Changelog,
    // so checking for Changelog alone (the query's driving table) covers the realistic failure mode.
    private static bool IsMissingChangelogTable(SqliteException ex) =>
        ex.SqliteErrorCode == 1
        && ex.Message.Contains("no such table: Changelog", StringComparison.Ordinal);
}
