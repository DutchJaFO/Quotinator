namespace Quotinator.Data.Import;

/// <summary>
/// Thrown by <see cref="IImportActionCoordinator"/> when an operation isn't valid for an action's
/// current <see cref="Entities.SystemImportAction.Status"/> — e.g. deciding an already-applied
/// action, or undoing one that was never decided.
/// </summary>
public sealed class ImportActionStateException : Exception
{
    /// <summary>The action id the operation was attempted on.</summary>
    public Guid ActionId { get; }

    /// <summary>The action's actual status at the time of the attempt — one of the <see cref="Entities.ImportActionStatus"/> constants.</summary>
    public string CurrentStatus { get; }

    /// <summary>Creates the exception with the action id and its actual current status.</summary>
    public ImportActionStateException(Guid actionId, string currentStatus)
        : base($"Import action '{actionId}' is not in a valid state for this operation (current status: '{currentStatus}').")
    {
        ActionId      = actionId;
        CurrentStatus = currentStatus;
    }
}
