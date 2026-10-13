using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>
/// One migration that actually ran during this startup, and what it did (#371).
/// </summary>
/// <remarks>
/// Suffixed <c>Result</c> per ADR 016: the in-process outcome of applying one migration, handed back
/// to the caller, crossing no boundary of its own. Nothing about it is persisted, because the
/// description is derivable from this build's own migration list by version number, and an
/// append-only frozen list means a database migrated by an older build resolves correctly against a
/// newer one's.
/// </remarks>
/// <param name="Phase">Which of the two migration lists this migration came from.</param>
/// <param name="Version">The migration's own version number within that list.</param>
/// <param name="Description">What the migration did, copied from <see cref="SchemaMigration.Description"/>.</param>
public readonly record struct AppliedMigrationResult(MigrationPhase Phase, int Version, string Description);
