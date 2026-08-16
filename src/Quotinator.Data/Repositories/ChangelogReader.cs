using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Changelog.Models;
using Quotinator.Changelog.Services;
using Quotinator.Data.Enums;
using Quotinator.Data.Logging;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <inheritdoc/>
/// <remarks>Initialises the reader with the changelog database's own join repository, the JSON-backed fallback service, and a logger.</remarks>
/// <param name="joinRepository">Reads every <c>Changelog</c>/<c>ChangelogLine</c> row, flattened (ADR 017 — never a hand-rolled query).</param>
/// <param name="changelogService">Fallback used when the changelog database's tables don't exist yet or the query otherwise fails.</param>
/// <param name="logger">Logger for the fallback warning.</param>
public sealed class ChangelogReader(
    JoinQueryRepository<ChangelogLineRow> joinRepository,
    IChangelogService changelogService,
    ILogger<ChangelogReader> logger) : IChangelogReader
{
    /// <inheritdoc/>
    public async Task<ChangelogDocument?> GetDocumentAsync(string? culture)
    {
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

        // An empty database (e.g. the background import hasn't finished yet — #309's Program.cs
        // wiring deliberately doesn't block startup on it) is the same "not ready" case as a missing
        // table — fall back rather than returning an empty/null document.
        if (rows.Count == 0)
            return changelogService.GetForCulture(culture);

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
