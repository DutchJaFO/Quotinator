using Quotinator.Data.Enums;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// The outcome of checking one database (#490, ADR 028): one <see cref="DatabaseCheckLayerResult"/> for every
/// layer, in the order the check runs them.
/// </summary>
/// <remarks>
/// A fault the check finds is detected deliberately, so it is returned as this outcome rather than thrown
/// (ADR 022). Suffixed <c>Result</c> per ADR 016: the in-process outcome of one operation.
/// </remarks>
public sealed class DatabaseCheckResult
{
    /// <summary>The name the database is reported under.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>The depth the check ran at.</summary>
    public required DatabaseCheckDepth Depth { get; init; }

    /// <summary>
    /// Every layer, in the order the check runs them. A layer is never left out: one that did not run is
    /// reported <see cref="DatabaseCheckLayerStatus.NotRun"/> or <see cref="DatabaseCheckLayerStatus.Skipped"/>,
    /// so every report has the same shape.
    /// </summary>
    public required IReadOnlyList<DatabaseCheckLayerResult> Layers { get; init; }

    /// <summary>
    /// The first layer that failed or was inconclusive, which is the check's result, or
    /// <see langword="null"/> when none did.
    /// </summary>
    public DatabaseCheckLayerResult? FirstFailure =>
        Layers.FirstOrDefault(l => l.Status is DatabaseCheckLayerStatus.Failed or DatabaseCheckLayerStatus.Inconclusive);

    /// <summary>
    /// Whether every layer that ran passed. At <see cref="DatabaseCheckDepth.Fast"/> this says nothing about
    /// the layers it skipped.
    /// </summary>
    public bool IsSound => FirstFailure is null;
}
