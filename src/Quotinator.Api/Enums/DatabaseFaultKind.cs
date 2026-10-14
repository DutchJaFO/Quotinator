namespace Quotinator.Api.Enums;

/// <summary>
/// Which kind of fault left the database degraded (#350), and therefore whether a live query written
/// against this build's schema can be expected to execute at all.
/// <para>
/// Every fault that existed before #350 is <see cref="ExpectedSchemaMissing"/>: an unopenable data
/// directory, a failed initialisation, a migration refused for want of a backup. A schema-version
/// overshoot is the first one where the schema holds everything this build expects and more, so the
/// diagnostic surfaces an operator reads can still query it.
/// </para>
/// </summary>
public enum DatabaseFaultKind
{
    /// <summary>
    /// What this build expects of the schema is not guaranteed to be there, so a query naming its own
    /// tables or columns may throw. No live read is attempted while this is the recorded fault.
    /// </summary>
    ExpectedSchemaMissing,

    /// <summary>
    /// The recorded schema version is ahead of this build, so the schema holds everything this build
    /// expects plus changes it does not know about. Queries execute; their content is not trusted as a
    /// source, which is why the only remedies are a database Reset or a restore from backup.
    /// </summary>
    SchemaAheadOfThisBuild,
}
