namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL scripts for the <c>Import_FileResource</c>/<c>Import_FileResourceLine</c>/
/// <c>Import_FileResourceBatch</c> tables (#251). Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns version numbers and determines
/// the sequence.
/// </summary>
public static class FileResourceMigrations
{
    /// <summary>
    /// Creates all three tables directly under their final, domain-prefixed shape — introduced fresh
    /// after ADR 015/016 were already established, so no create-then-rename pair is needed, matching
    /// <see cref="SourceFileOverrideMigrations"/>'s own precedent. <c>Origin</c> is backed by
    /// <see cref="Enums.FileResourceOrigin"/> — a closed set, so per ADR 008 it carries a matching
    /// CHECK constraint from creation. <c>Import_FileResource</c> is deduplicated by
    /// <c>ContentHash</c> (unique index) — re-capturing unchanged content updates <c>LastSeenAtUtc</c>
    /// rather than inserting a new row. <c>Import_FileResourceLine</c> stores the file's own content,
    /// one row per literal line; reconstruction fidelity (<c>LineEnding</c>/<c>EndsWithTrailingNewline</c>)
    /// is recorded once per file on the parent row, not per line — this project's own confirmed
    /// assumption is that line endings are uniform within a single file. Both
    /// <c>Import_FileResourceLine</c> and <c>Import_FileResourceBatch</c> carry a full <c>RecordBase</c>
    /// shape (surrogate <c>Id</c> plus the natural key enforced via a separate <c>UNIQUE</c> constraint)
    /// per ADR 002 ("RecordBase applies to all tables without exception") — a junction/child-row table
    /// is explicitly the case that ADR calls out as not exempt, matching
    /// <c>Quotinator_CharacterSource</c>/<c>Quotinator_QuoteGenre</c>'s own shape, not a bare
    /// composite-primary-key design. References <c>Import_Batch</c> (#253's renamed table, created by
    /// Quotinator.Core's own migration phase, which always runs immediately after this one within the
    /// same FK-enforcement-off window — see <see cref="DatabaseInitializer.ApplyMigrationsAsync"/>'s own
    /// <c>PRAGMA foreign_keys</c> toggling) — safe as a forward reference despite <c>Import_Batch</c> not
    /// existing yet at the moment this migration runs.
    /// <para>
    /// <b>Correction (2026-08-02, found before any code shipped):</b> the initial version of this
    /// migration captured only a file's raw content — never the converter (if any) and converter
    /// options that were used to interpret it, so a captured raw file alone couldn't tell you *how*
    /// it was turned into quotes. <c>Converter</c>/<c>ConverterOptions</c> were added directly to this
    /// migration (not a new one) since nothing had shipped yet. <c>ConverterOptions</c> mirrors
    /// <c>SeedFile.ConverterOptions</c>/<c>SourceImportSettingsDto.ConverterOptions</c>'s own
    /// "opaque, undeserialized payload" treatment — stored as raw JSON text, never parsed by this
    /// project. On a content-hash dedup hit, both columns are overwritten with the latest capture's
    /// values (alongside <c>LastSeenAtUtc</c>) rather than frozen at first capture — consistent with
    /// <c>LastSeenAtUtc</c>'s own "reflects the most recent occurrence" semantics, so a row never goes
    /// stale if the same raw bytes are later reimported under different converter settings.
    /// </para>
    /// </summary>
    public const string CreateFileResourceTables = """
        CREATE TABLE IF NOT EXISTS Import_FileResource (
            Id                      TEXT    NOT NULL PRIMARY KEY,
            FileName                TEXT    NOT NULL,
            OriginalFolderPath      TEXT,
            Origin                  TEXT    NOT NULL
                                    CHECK (Origin IN ('Bundled', 'UserImports', 'Uploaded')),
            ContentHash             TEXT    NOT NULL,
            LineEnding              TEXT    NOT NULL
                                    CHECK (LineEnding IN ('LF', 'CRLF', 'CR')),
            EndsWithTrailingNewline INTEGER NOT NULL,
            Converter               TEXT,
            ConverterOptions        TEXT,
            FirstSeenAtUtc          TEXT    NOT NULL,
            LastSeenAtUtc           TEXT    NOT NULL,
            DateCreated             TEXT    NOT NULL,
            DateModified            TEXT,
            DateDeleted             TEXT,
            IsDeleted               INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_FileResource_ContentHash ON Import_FileResource (ContentHash);
        CREATE INDEX IF NOT EXISTS IX_Import_FileResource_FileName ON Import_FileResource (FileName);

        CREATE TABLE IF NOT EXISTS Import_FileResourceLine (
            Id             TEXT    NOT NULL PRIMARY KEY,
            FileResourceId TEXT    NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
            LineNumber     INTEGER NOT NULL,
            Text           TEXT    NOT NULL,
            DateCreated    TEXT    NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            UNIQUE (FileResourceId, LineNumber)
        );

        CREATE TABLE IF NOT EXISTS Import_FileResourceBatch (
            Id             TEXT    NOT NULL PRIMARY KEY,
            FileResourceId TEXT    NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
            ImportBatchId  TEXT    NOT NULL REFERENCES Import_Batch(Id),
            ImportedAt     TEXT    NOT NULL,
            DateCreated    TEXT    NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            UNIQUE (FileResourceId, ImportBatchId)
        );
        """;
}
