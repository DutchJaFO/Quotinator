namespace Quotinator.Engine.Services;

/// <summary>
/// Thrown by <see cref="IQuoteImportService.ApplyStagedBatchAsync"/> when the given batch id does
/// not exist. A distinct type (rather than a generic <see cref="QuoteImportValidationException"/>)
/// so the endpoint handler can return <c>404</c> rather than <c>422</c>.
/// </summary>
public sealed class ImportBatchNotFoundException : Exception
{
    /// <summary>The batch id that was not found.</summary>
    public Guid BatchId { get; }

    /// <summary>Creates the exception for the given missing batch id.</summary>
    public ImportBatchNotFoundException(Guid batchId)
        : base($"Import batch '{batchId}' does not exist.")
    {
        BatchId = batchId;
    }
}
