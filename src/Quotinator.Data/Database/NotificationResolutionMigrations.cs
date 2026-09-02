namespace Quotinator.Data.Database;

/// <summary>
/// Pre-defined migration SQL adding <c>System_Notification.Resolution</c> for #308 — how a
/// notification's own action settled it, alongside the existing <c>DismissReason</c> saying only that
/// it settled. Consumed by <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which assigns the
/// version number.
/// </summary>
public static class NotificationResolutionMigrations
{
    /// <summary>
    /// Adds the nullable <c>Resolution</c> column with its CHECK inline.
    /// <para>
    /// An <c>ALTER TABLE ... ADD COLUMN</c> rather than a table rebuild, following migration 16's
    /// precedent exactly: SQLite permits a <c>CHECK</c> on an added column (confirmed against
    /// sqlite.org, and the basis of ADR 008's checklist point 2). Only *widening an existing* CHECK
    /// needs a rebuild.
    /// </para>
    /// <para>
    /// Nullable, and every existing row is deliberately left <see langword="null"/> — including rows
    /// already dismissed as <c>Resolved</c>. The choice that settled them was never stored, so writing
    /// one now would invent history; the read side renders no resolution line for them, exactly as it
    /// did before this column existed. That is the same reasoning migration 16 applied to
    /// <c>DismissReason</c> itself.
    /// </para>
    /// </summary>
    public const string AddResolutionColumn = """
        ALTER TABLE System_Notification ADD COLUMN Resolution TEXT
            CHECK (Resolution IS NULL OR Resolution IN ('KeptExisting', 'TookIncoming', 'Reseeded', 'Reset'));
        """;
}
