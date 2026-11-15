using Quotinator.Data.Enums;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// What one layer of the database check found (#490, ADR 028).
/// </summary>
/// <remarks>Suffixed <c>Result</c> per ADR 016: part of the in-process outcome of one check.</remarks>
public sealed class DatabaseCheckLayerResult
{
    /// <summary>The layer this result is for.</summary>
    public required DatabaseCheckLayer Layer { get; init; }

    /// <summary>What the layer found.</summary>
    public required DatabaseCheckLayerStatus Status { get; init; }

    /// <summary>
    /// The engine's own diagnostic, or the reason a layer was inconclusive or does not apply.
    /// <see langword="null"/> when there is nothing to say.
    /// <para>
    /// Carried verbatim to be logged and shown, and never parsed: no decision is taken from its text
    /// (ADR 027, ADR 028). It may name the engine, which is why nothing outside <c>Quotinator.Data</c>
    /// reads meaning into it.
    /// </para>
    /// </summary>
    public string? Finding { get; init; }

    /// <summary>
    /// How long the layer took, or <see langword="null"/> when it did not run. An indicator for locating a
    /// slow layer and for reporting progress, never a threshold anything is tested against.
    /// </summary>
    public TimeSpan? Elapsed { get; init; }
}
