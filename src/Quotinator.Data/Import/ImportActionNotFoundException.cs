namespace Quotinator.Data.Import;

/// <summary>Thrown by <see cref="IImportActionCoordinator"/> when an action id does not exist.</summary>
/// <remarks>Creates the exception for the given missing action id.</remarks>
/// <param name="actionId">The action id that was not found.</param>
public sealed class ImportActionNotFoundException(Guid actionId) : Exception($"Import action '{actionId}' does not exist.")
{
    /// <summary>The action id that was not found.</summary>
    public Guid ActionId { get; } = actionId;
}
