namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL for #252's generalization of <c>Import_FileResource.Origin</c>. Consumed
/// by <see cref="DatabaseInitializer.DataOwnedMigrations"/> as version 7, appended after version 6
/// (<see cref="FileResourceMigrations.CreateFileResourceTables"/>) rather than folded into it — version
/// 6 had already applied to this project's own local development database (during #251's T1
/// verification) by the time this generalization was decided, so per ADR 015's corrected policy (see
/// its own "Revision — issue #254" section) it is frozen and must not be edited in place, even though it
/// had not yet shipped in a tagged release.
/// </summary>
public static class FileResourceOriginGeneralizationMigrations
{
    /// <summary>
    /// Rebuilds <c>Import_FileResource</c> only — <c>Import_FileResourceLine</c>/
    /// <c>Import_FileResourceBatch</c> don't reference <c>Origin</c>, so they're untouched. Renames the
    /// enum-backed <c>Origin</c> CHECK constraint's allowed values (<c>Bundled</c>/<c>UserImports</c>/
    /// <c>Uploaded</c> → <c>System</c>/<c>User</c>/<c>Upload</c>, matching <see cref="Enums.FileResourceOrigin"/>'s
    /// own rename) and adds the new <c>HomeDirectoryKey</c> column, backfilling existing rows from their
    /// remapped <c>Origin</c> (<c>System</c> → <c>"sources"</c>, <c>User</c> → <c>"imports"</c>,
    /// <c>Upload</c> → <c>NULL</c>) — the only two local directories any write path has ever captured
    /// from before this migration existed, so this is a correct backfill, not a guess.
    /// </summary>
    public const string GeneralizeOrigin = """
        CREATE TABLE IF NOT EXISTS Import_FileResource_New (
            Id                      TEXT    NOT NULL PRIMARY KEY,
            FileName                TEXT    NOT NULL,
            OriginalFolderPath      TEXT,
            Origin                  TEXT    NOT NULL
                                    CHECK (Origin IN ('System', 'User', 'Upload')),
            HomeDirectoryKey        TEXT,
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

        INSERT INTO Import_FileResource_New (Id, FileName, OriginalFolderPath, Origin, HomeDirectoryKey, ContentHash, LineEnding, EndsWithTrailingNewline, Converter, ConverterOptions, FirstSeenAtUtc, LastSeenAtUtc, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT
            Id, FileName, OriginalFolderPath,
            CASE Origin
                WHEN 'Bundled'     THEN 'System'
                WHEN 'UserImports' THEN 'User'
                WHEN 'Uploaded'    THEN 'Upload'
                ELSE Origin
            END,
            CASE Origin
                WHEN 'Bundled'     THEN 'sources'
                WHEN 'UserImports' THEN 'imports'
                ELSE NULL
            END,
            ContentHash, LineEnding, EndsWithTrailingNewline, Converter, ConverterOptions, FirstSeenAtUtc, LastSeenAtUtc, DateCreated, DateModified, DateDeleted, IsDeleted
        FROM Import_FileResource;

        DROP TABLE Import_FileResource;

        ALTER TABLE Import_FileResource_New RENAME TO Import_FileResource;

        CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_FileResource_ContentHash ON Import_FileResource (ContentHash);
        CREATE INDEX IF NOT EXISTS IX_Import_FileResource_FileName ON Import_FileResource (FileName);
        """;
}
