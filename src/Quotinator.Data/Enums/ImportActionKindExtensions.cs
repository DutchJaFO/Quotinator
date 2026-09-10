namespace Quotinator.Data.Enums;

/// <summary>Classifies <see cref="ImportActionKind"/> members by how the planner stages them (#389).</summary>
public static class ImportActionKindExtensions
{
    /// <summary>
    /// Whether an action of <paramref name="kind"/> is a plan-time no-op — staged straight to
    /// <see cref="ImportActionStatus.Applied"/> because nothing is ever written for it.
    /// </summary>
    /// <remarks>
    /// A no-op is not applied work, whatever its status says: <c>TryApplyBatchAsync</c> applies only
    /// <c>Decided</c> rows, so nothing was written for it, and a discard has nothing of it to undo (#389).
    /// <para>
    /// Exhaustive on purpose. Every member is listed, and a kind added later throws until someone decides
    /// which side it is on — <c>ImportActionKindExtensionsTests.EveryKind_IsClassified</c> walks the enum,
    /// so that decision cannot be skipped. A fall-through arm returning <see langword="false"/> would have
    /// quietly counted a new no-op kind as applied work, which is exactly how discard came to refuse
    /// batches it should have accepted.
    /// </para>
    /// </remarks>
    /// <param name="kind">The action's own kind.</param>
    public static bool IsPlanTimeNoOp(this ImportActionKind kind) => kind switch
    {
        ImportActionKind.Add                => false,
        ImportActionKind.Modify             => false,
        ImportActionKind.Unchanged          => true,
        ImportActionKind.ResolvedToExisting => true,
        ImportActionKind.AlreadyReported    => true,
        _ => throw new NotSupportedException($"No plan-time classification is declared for import action kind '{kind}'."),
    };
}
