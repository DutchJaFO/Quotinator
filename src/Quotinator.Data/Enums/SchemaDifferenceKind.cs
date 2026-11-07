namespace Quotinator.Data.Enums;

/// <summary>
/// How an actual schema object differs from the one its migration path defines (#305). Missing and
/// different objects make a database not intact; an extra object is reported without doing so, since it
/// means a migration may have been missed rather than that the schema is incomplete.
/// </summary>
public enum SchemaDifferenceKind
{
    /// <summary>The migration path defines the object and the database does not have it.</summary>
    Missing,

    /// <summary>The database has the object, but one of its compared properties differs.</summary>
    Different,

    /// <summary>The database has an object the migration path does not define.</summary>
    Extra,
}
