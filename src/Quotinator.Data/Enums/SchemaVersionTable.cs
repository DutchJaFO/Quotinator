namespace Quotinator.Data.Enums;

/// <summary>
/// A table that records how far a database's migrations have run (#490): the database check's Schema layer
/// reads it to tell whether comparing the schema means anything yet.
/// </summary>
public enum SchemaVersionTable
{
    /// <summary><c>System_SchemaVersion</c>: Quotinator.Data's own migrations in the main database.</summary>
    Data,

    /// <summary><c>System_ConsumerSchemaVersion</c>: the consuming project's migrations in the main database.</summary>
    Consumer,

    /// <summary><c>Changelog_SchemaVersion</c>: the changelog database's migrations.</summary>
    Changelog,
}
