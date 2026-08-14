namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL for the separate changelog database's schema (#309). Consumed by
/// <see cref="ChangelogDatabaseInitializer"/>. No <c>System_</c>/domain prefix on either table — a
/// dedicated, single-purpose database has nothing to disambiguate from (ADR 015's own prefix
/// convention exists specifically for SQLite's lack of schema qualification within one database).
/// </summary>
/// <remarks>
/// Named <c>ChangelogContentMigrations</c>, not <c>ChangelogMigrations</c> — deliberately distinct
/// from the pre-existing, unrelated <see cref="ChangeLogMigrations"/> (<c>System_ChangeLog</c>, the
/// audit-trail field-change-tracking table) in more than just casing. On this filesystem, a filename
/// differing only by case collides with an existing one; this class was first written as
/// <c>ChangelogMigrations.cs</c> and silently overwrote <c>ChangeLogMigrations.cs</c> on disk before
/// being caught by a build failure and restored from git.
/// </remarks>
public static class ChangelogContentMigrations
{
    /// <summary>
    /// Creates <c>Changelog</c> (one row per Language/Version, <c>Version IS NULL</c> for that
    /// language's <c>unreleased</c> entry) and <c>ChangelogLine</c> (one row per list item across
    /// every list-shaped field, discriminated by <c>Kind</c>) — #309's master/detail schema, built on
    /// <see cref="Repositories.AggregateRepository{TParent,TChild}"/> (#75). Both tables carry a full
    /// <c>RecordBase</c> shape per ADR 002 ("RecordBase applies to all tables without exception") even
    /// though this database has no audit trail of its own to reference them from — RecordBase's
    /// soft-delete/timestamp columns are still meaningful on their own (e.g. distinguishing a
    /// genuinely-removed row from one the importer simply hasn't re-written yet).
    /// </summary>
    public const string CreateChangelogTables = """
        CREATE TABLE IF NOT EXISTS Changelog (
            Id                TEXT NOT NULL PRIMARY KEY,
            Language          TEXT NOT NULL,
            Version           TEXT,
            Date              TEXT,
            MachineTranslated INTEGER NOT NULL DEFAULT 0,
            QuoteText         TEXT,
            QuoteAttribution  TEXT,
            DateCreated       TEXT NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Changelog_Language_Version
            ON Changelog (Language, Version) WHERE Version IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Changelog_Language_Unreleased
            ON Changelog (Language) WHERE Version IS NULL;

        CREATE TABLE IF NOT EXISTS ChangelogLine (
            Id           TEXT    NOT NULL PRIMARY KEY,
            ChangelogId  TEXT    NOT NULL REFERENCES Changelog(Id),
            Kind         TEXT    NOT NULL
                         CHECK (Kind IN ('Highlight', 'Added', 'Changed', 'Fixed', 'Removed', 'Issue', 'Cve', 'AudienceHighlight')),
            AudienceKey  TEXT,
            Value        TEXT    NOT NULL,
            SortOrder    INTEGER NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_ChangelogLine_ChangelogId ON ChangelogLine (ChangelogId);
        """;
}
