namespace Quotinator.Data.Import;

/// <summary>Thrown by <see cref="IImportActionCoordinator"/> when an action id does not exist.</summary>
public sealed class ImportActionNotFoundException : Exception
{
    /// <summary>The action id that was not found.</summary>
    public Guid ActionId { get; }

    /// <summary>Creates the exception for the given missing action id.</summary>
    public ImportActionNotFoundException(Guid actionId)
        : base($"Import action '{actionId}' does not exist.")
    {
        ActionId = actionId;
    }
}
