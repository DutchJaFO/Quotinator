namespace Quotinator.Data.Schema;

/// <summary>
/// A database that verifies its schema against its own migration path (#305). Every database the
/// application builds performs the check, since each has its own migration path;
/// <c>RepositoryStructureTests.EveryDatabaseWithMigrations_PerformsTheIntegrityCheck</c> holds every
/// type that declares migrations to it.
/// </summary>
public interface IIntegrityCheckedDatabase
{
    /// <summary>The name the database is reported under.</summary>
    string DatabaseName { get; }

    /// <summary>
    /// The result of the check the database last ran during its own initialisation or reset, or
    /// <see langword="null"/> when it has not run one.
    /// </summary>
    SchemaIntegrityResult? LastIntegrityResult { get; }

    /// <summary>Runs the check now and returns its result. Writes nothing.</summary>
    Task<SchemaIntegrityResult> CheckIntegrityAsync();
}
