namespace Quotinator.Data.Database;

/// <summary>
/// #155/#289: every Data-owned migration shipped since v1.7.2's single frozen migration
/// (<see cref="AuditMigrations.CreateAuditEntriesTable"/>, <c>DataOwnedMigrations</c> version 1) is
/// consolidated here, one constant per released-from version. None of the migrations either constant
/// replaces had ever reached a tagged release, so squashing them into one atomic step is safe under
/// this project's migration policy (which protects only migrations that have shipped) — see the #155
/// plan doc for the full reasoning, and ADR 015's revision (from #254) plus #289's own plan doc for
/// why "shipped" means "applied to any real database, including a developer's own local one," not
/// only a git-tagged release.
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

    /// <summary>
    /// #289: reference concatenation of the 6 Data-owned migrations added since v1.8.2 (versions 3-8),
    /// in their original application order. Each original constant stays in place, unedited and
    /// independently referenceable — several are executed directly by tests to build fixtures
    /// (<c>DatabaseInitializerOwnershipTests</c>, <c>NotificationReaderTests</c>,
    /// <c>NotificationWriterTests</c>), matching <see cref="SinceV172"/>'s own precedent.
    /// </summary>
    public const string SinceV182 =
        ImportConflictMigrations.AddAppliedPolicyCheckConstraint +
        ImportActionMigrations.AddAppliedPolicyCheckConstraint +
        DomainPrefixRenameMigrations.RenameDataOwnedTables +
        FileResourceMigrations.CreateFileResourceTables +
        FileResourceOriginGeneralizationMigrations.GeneralizeOrigin +
        NotificationMigrations.CreateNotificationTable;
}
