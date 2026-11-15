namespace Quotinator.Data.Enums;

/// <summary>
/// One layer of the database check (#490, ADR 028), in the order the check runs them: from the cheapest to
/// the most thorough.
/// <para>
/// Each member names <em>what</em> is checked, never how an engine checks it, so the rest of the
/// application, the degraded reasons and the integrity endpoint speak in layers rather than in one engine's
/// commands. ADR 028's engine section says how SQLite answers each one.
/// </para>
/// </summary>
public enum DatabaseCheckLayer
{
    /// <summary>Whether the file is there and can be read, and whether its directory can be written.</summary>
    Storage,

    /// <summary>Whether the file is a database of this engine, by the engine's documented file format.</summary>
    Format,

    /// <summary>
    /// Whether the engine's write-ahead file, when there is one, is valid by its documented format. A
    /// database with no write-ahead file passes.
    /// </summary>
    Journal,

    /// <summary>Whether every page can be read and every record is well-formed.</summary>
    Pages,

    /// <summary>Whether every index agrees with its table, and every unique constraint holds.</summary>
    Indexes,

    /// <summary>Whether every declared reference points at a row that exists.</summary>
    References,

    /// <summary>Whether the schema matches the one its recorded version defines (#305).</summary>
    Schema,
}
