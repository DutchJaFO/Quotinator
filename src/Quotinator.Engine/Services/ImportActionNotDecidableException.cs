namespace Quotinator.Engine.Services;

/// <summary>
/// Thrown by <see cref="IImportActionService.DecideAsync"/> when a decision is attempted on an
/// action whose <c>EntityType</c> isn't <c>"Quote"</c>. Source/Character/Person actions are always
/// staged already-<c>Decided</c> (Add is never ambiguous — <c>GetOrCreateSourceAsync</c>/etc. never
/// update an existing row) — this is domain knowledge <c>Quotinator.Data</c>'s generic coordinator
/// doesn't have, so the rejection lives here rather than in <c>ImportActionResolutionCoordinator</c>.
/// </summary>
public sealed class ImportActionNotDecidableException : Exception
{
    /// <summary>The action id the decision was attempted on.</summary>
    public Guid ActionId { get; }

    /// <summary>The action's actual entity type.</summary>
    public string EntityType { get; }

    /// <summary>Creates the exception with the action id and its actual entity type.</summary>
    public ImportActionNotDecidableException(Guid actionId, string entityType)
        : base($"Import action '{actionId}' is a '{entityType}' action and cannot be manually decided — only 'Quote' actions support a decision.")
    {
        ActionId   = actionId;
        EntityType = entityType;
    }
}
