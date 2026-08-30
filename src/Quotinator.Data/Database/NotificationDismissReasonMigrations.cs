namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL recording *why* a notification stopped being active (#304). Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the version number.
/// </summary>
public static class NotificationDismissReasonMigrations
{
    /// <summary>
    /// Adds the nullable <c>DismissReason</c> column with its CHECK inline.
    /// <para>
    /// An <c>ALTER TABLE ... ADD COLUMN</c> rather than a table rebuild: SQLite permits a <c>CHECK</c>
    /// on an added column (confirmed against sqlite.org, and the basis of ADR 008's checklist point 2).
    /// Only *widening an existing* CHECK needs the rebuild that migration 15 had to do.
    /// </para>
    /// <para>
    /// Nullable, and existing dismissed rows are deliberately left <see langword="null"/>: they were
    /// dismissed before anything recorded a reason, and writing one now would invent history. The read
    /// side treats a dismissed row with no reason exactly as it did before this column existed.
    /// </para>
    /// </summary>
    public const string AddDismissReasonColumn = """
        ALTER TABLE System_Notification ADD COLUMN DismissReason TEXT
            CHECK (DismissReason IS NULL OR DismissReason IN ('Dismissed', 'Resolved'));
        """;
}
