namespace Quotinator.Data.Database;

/// <summary>
/// #373: widens <c>Import_Action.ActionType</c>'s CHECK constraint to admit <c>Unchanged</c>, the
/// outcome for a record an import would leave exactly as it is. Consumed by
/// <see cref="DatabaseInitializer.DataOwnedMigrations"/>, which owns the version number.
/// </summary>
/// <remarks>
/// A full table rebuild, because SQLite cannot widen an inline CHECK constraint — the same technique
/// <c>ImportActionMigrations.AddAppliedPolicyCheckConstraint</c> used for the same table, and the shape
/// ADR 008 requires whenever an enum-backed column gains a member.
/// <para>
/// The copy is a straight column-for-column carry: nothing about existing rows changes, and no value
/// is rewritten. Only the constraint widens, so every row that was valid before is valid after.
/// </para>
/// </remarks>
public static class ImportActionUnchangedMigrations
{
    /// <summary>Rebuilds <c>Import_Action</c> with <c>Unchanged</c> accepted by <c>ActionType</c>.</summary>
    public const string WidenActionTypeForUnchanged = """
        CREATE TABLE Import_Action_New (
            Id                 TEXT    NOT NULL PRIMARY KEY,
            BatchId            TEXT    NOT NULL,
            ActionType         TEXT    NOT NULL
                               CHECK (ActionType IN ('Add', 'Modify', 'Unchanged')),
            EntityType         TEXT    NOT NULL,
            EntityId           TEXT    NOT NULL,
            ExistingBatchId    TEXT,
            ExistingValue      TEXT,
            IncomingValue      TEXT    NOT NULL,
            AppliedPolicy      TEXT
                               CHECK (AppliedPolicy IS NULL OR AppliedPolicy IN ('Skip', 'NewestWins', 'MergeOurs', 'MergeTheirs', 'Review')),
            Status             TEXT    NOT NULL
                               CHECK (Status IN ('Pending', 'Decided', 'Applied', 'Discarded', 'Blocked', 'Stale')),
            MergedFields       TEXT,
            MarkCompletenessAs TEXT
                               CHECK (MarkCompletenessAs IS NULL OR MarkCompletenessAs IN ('Incomplete', 'NeedsReview', 'Complete')),
            DetectedAt         TEXT    NOT NULL,
            AppliedAt          TEXT,
            DiscardedAt        TEXT,
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0,
            OriginalDecision   TEXT
        );

        INSERT INTO Import_Action_New (
            Id, BatchId, ActionType, EntityType, EntityId, ExistingBatchId, ExistingValue,
            IncomingValue, AppliedPolicy, Status, MergedFields, MarkCompletenessAs, DetectedAt,
            AppliedAt, DiscardedAt, DateCreated, DateModified, DateDeleted, IsDeleted, OriginalDecision)
        SELECT
            Id, BatchId, ActionType, EntityType, EntityId, ExistingBatchId, ExistingValue,
            IncomingValue, AppliedPolicy, Status, MergedFields, MarkCompletenessAs, DetectedAt,
            AppliedAt, DiscardedAt, DateCreated, DateModified, DateDeleted, IsDeleted, OriginalDecision
        FROM Import_Action;

        DROP TABLE Import_Action;

        ALTER TABLE Import_Action_New RENAME TO Import_Action;

        CREATE INDEX IF NOT EXISTS IX_Import_Action_BatchId ON Import_Action (BatchId);
        CREATE INDEX IF NOT EXISTS IX_Import_Action_Status ON Import_Action (Status);
        """;
}
