namespace Quotinator.Data.Import;

/// <summary>
/// Thrown by <see cref="IImportActionCoordinator"/> when an operation isn't valid for an action's
/// current <see cref="Entities.ImportActionEntity.Status"/> — e.g. deciding an already-applied
/// action, or undoing one that was never decided.
/// </summary>
/// <remarks>Creates the exception with the action id and its actual current status.</remarks>
/// <param name="actionId">The action id the operation was attempted on.</param>
/// <param name="currentStatus">The action's actual status at the time of the attempt — one of the <see cref="Enums.ImportActionStatus"/> constants.</param>
public sealed class ImportActionStateException(Guid actionId, string currentStatus) : Exception($"Import action '{actionId}' is not in a valid state for this operation (current status: '{currentStatus}').")
{
    /// <summary>The action id the operation was attempted on.</summary>
    public Guid ActionId { get; } = actionId;

    /// <summary>The action's actual status at the time of the attempt — one of the <see cref="Enums.ImportActionStatus"/> constants.</summary>
    public string CurrentStatus { get; } = currentStatus;
}
