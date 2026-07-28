namespace Quotinator.Data.Database;

/// <summary>
/// #155: the second (and, for now, only remaining) Data-owned migration — every Data-owned migration
/// shipped since v1.7.2's single frozen migration (<see cref="AuditMigrations.CreateAuditEntriesTable"/>,
/// <c>DataOwnedMigrations</c> version 1). None of the 12 migrations this replaces had ever reached a
/// real release, so squashing them into one atomic step is safe under this project's migration policy
/// (which protects only migrations that have shipped) — see the #155 plan doc for the full reasoning.
/// </summary>
public static class DataConsolidatedMigrations
{
    /// <summary>
    /// Literal C# const concatenation of the 12 migrations that shipped since v1.7.2, in their
    /// original application order — not a copy of their SQL text. Each original constant stays the
    /// single source of truth for its own fragment and remains independently referenceable (e.g.
    /// <c>SourceFileOverrideRegistryTests</c> executes
    /// <see cref="SourceFileOverrideMigrations.CreateSourceFileOverridesTable"/> directly to build a
    /// minimal repository-test fixture, unrelated to migration replay). Combining previously-separate
    /// transactions into one is strictly safer (fully atomic), never less so.
    /// </summary>
    public const string SinceV172 =
        AuditMigrations.RenameAuditEntriesToSystemAuditEntries +
        ImportConflictMigrations.CreateImportConflictsTable +
        ChangeLogMigrations.CreateChangeLogTable +
        AuditMigrations.MigrateToRecordBase +
        ImportConflictMigrations.MigrateToRecordBase +
        ImportConflictMigrations.AddExistingBatchId +
        ImportActionMigrations.CreateImportActionsTable +
        ImportConflictMigrations.AddStatusCheckConstraint +
        ImportActionMigrations.AddBlockedStatusAndMarkCompletenessAs +
        ImportActionMigrations.AddOriginalDecision +
        ImportActionMigrations.AddStaleStatus +
        SourceFileOverrideMigrations.CreateSourceFileOverridesTable;
}
