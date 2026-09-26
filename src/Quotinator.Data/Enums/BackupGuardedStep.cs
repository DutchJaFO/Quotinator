namespace Quotinator.Data.Enums;

/// <summary>
/// The steps that write to the database and never run without a backup unless the operator overrides
/// (#348). A refusal names which one it was, because each is reported differently: a refused migration
/// leaves the schema behind the build, so the application degrades; a refused content load leaves an
/// intact schema, so the application stays healthy and says what it did not load.
/// </summary>
public enum BackupGuardedStep
{
    /// <summary>Applying pending schema migrations at startup.</summary>
    Migration,

    /// <summary>Loading the configured content into a database that has none, at startup.</summary>
    ContentLoad,

    /// <summary>Dropping and rebuilding the whole database.</summary>
    Reset,
}
