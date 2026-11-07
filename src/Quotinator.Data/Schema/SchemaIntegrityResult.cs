using Quotinator.Data.Enums;

namespace Quotinator.Data.Schema;

/// <summary>
/// The outcome of comparing a database's schema with the one its migration path defines (#305).
/// </summary>
/// <remarks>
/// A difference is detectable by a check, so it is returned as this outcome rather than thrown (ADR 022).
/// Suffixed <c>Result</c> per ADR 016: the in-process outcome of one operation.
/// </remarks>
public sealed class SchemaIntegrityResult
{
    /// <summary>Every difference found, sorted so the same schema always reports the same way.</summary>
    public required IReadOnlyList<SchemaDifference> Differences { get; init; }

    /// <summary>
    /// Whether nothing is missing or different. Extra objects leave a schema intact: they are reported, not
    /// a failure.
    /// </summary>
    public bool IsIntact => !Differences.Any(d => d.Kind != SchemaDifferenceKind.Extra);

    /// <summary>A comparison that found no difference at all.</summary>
    public static SchemaIntegrityResult Intact { get; } = new() { Differences = [] };
}
