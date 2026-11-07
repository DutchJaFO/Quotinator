using Quotinator.Data.Enums;

namespace Quotinator.Data.Schema;

/// <summary>One way an actual schema differs from the one its migration path defines (#305).</summary>
/// <param name="Kind">Whether the object is missing, different or extra.</param>
/// <param name="ObjectKind">What kind of object differs.</param>
/// <param name="ObjectName">The object's name, qualified by its table where it belongs to one.</param>
/// <param name="Aspect">The compared property that differs, or <see langword="null"/> when the whole object is missing or extra.</param>
/// <param name="Expected">The value the migration path defines, or <see langword="null"/> when there is none.</param>
/// <param name="Actual">The value the database holds, or <see langword="null"/> when there is none.</param>
public sealed record SchemaDifference(
    SchemaDifferenceKind Kind,
    SchemaObjectKind ObjectKind,
    string ObjectName,
    string? Aspect,
    string? Expected,
    string? Actual);
