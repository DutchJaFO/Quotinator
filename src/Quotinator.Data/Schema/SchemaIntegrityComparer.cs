namespace Quotinator.Data.Schema;

/// <summary>
/// Compares a database's actual schema with the one its migration path defines (#305). Pure and
/// engine-neutral (ADR 003, design goal 5): it sees only <see cref="SchemaSnapshot"/> values.
/// </summary>
public static class SchemaIntegrityComparer
{
    /// <summary>Lists every way <paramref name="actual"/> differs from <paramref name="expected"/>.</summary>
    /// <param name="expected">The schema the database's migration path defines.</param>
    /// <param name="actual">The schema the database holds.</param>
    /// <returns>Every missing, different and extra object, sorted.</returns>
    public static SchemaIntegrityResult Compare(SchemaSnapshot expected, SchemaSnapshot actual) =>
        throw new NotImplementedException();
}
