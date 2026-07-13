namespace Quotinator.Engine.Services;

/// <summary>
/// Thrown by <see cref="IImportActionService.DecideAsync"/> when a decision is attempted on an
/// action whose entity type does not currently support a Modify decision. Which entity types are
/// decidable (and for which <c>ActionType</c>) is domain knowledge that lives in
/// <see cref="IImportActionService.DecideAsync"/>'s own branching, not here — this exception only
/// reports the rejection; <c>Quotinator.Data</c>'s generic coordinator has no such domain knowledge,
/// so the rejection lives here rather than in <c>ImportActionResolutionCoordinator</c>.
/// </summary>
/// <remarks>Creates the exception with the action id and its actual entity type.</remarks>
public sealed class ImportActionNotDecidableException(Guid actionId, string entityType) : Exception($"Import action '{actionId}' is a '{entityType}' action and cannot be manually decided — this action's entity type does not currently support a Modify decision.")
{
    /// <summary>The action id the decision was attempted on.</summary>
    public Guid ActionId { get; } = actionId;

    /// <summary>The action's actual entity type.</summary>
    public string EntityType { get; } = entityType;
}
