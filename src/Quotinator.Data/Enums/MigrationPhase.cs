namespace Quotinator.Data.Enums;

/// <summary>
/// Which of the two independently-tracked migration lists a migration belongs to (#371).
/// <para>
/// A closed set owned by <c>Quotinator.Data</c>, which is why it is an enum rather than the phase
/// name string <c>ApplyMigrationPhaseAsync</c> took before: no consuming project invents a third
/// phase. Each member's name is the word the startup banner and the log already print, so rendering
/// one costs nothing beyond <c>ToString()</c>.
/// </para>
/// </summary>
public enum MigrationPhase
{
    /// <summary>
    /// <c>Quotinator.Data</c>'s own migrations, tracked in <c>System_SchemaVersion</c>.
    /// </summary>
    Data,

    /// <summary>
    /// The consuming project's own domain migrations, tracked in
    /// <c>System_ConsumerSchemaVersion</c>. Named <c>App</c> rather than <c>Consumer</c> because that
    /// is the word already recorded in the version table and printed in the startup banner.
    /// </summary>
    App
}
