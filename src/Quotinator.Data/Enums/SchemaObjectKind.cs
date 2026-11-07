namespace Quotinator.Data.Enums;

/// <summary>The kinds of schema object the integrity check compares (#305).</summary>
public enum SchemaObjectKind
{
    /// <summary>A table.</summary>
    Table,

    /// <summary>A column of a table.</summary>
    Column,

    /// <summary>A foreign key declared on a table.</summary>
    ForeignKey,

    /// <summary>An index on a table.</summary>
    Index,

    /// <summary>A view.</summary>
    View,

    /// <summary>A trigger.</summary>
    Trigger,
}
