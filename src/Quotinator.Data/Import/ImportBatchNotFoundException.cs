namespace Quotinator.Data.Import;

/// <summary>
/// Thrown by a consumer's staged-batch-apply flow (e.g. an <c>ApplyStagedBatchAsync</c> method) when
/// the given batch id does not exist. A distinct type from any generic validation exception a
/// consumer defines, so its endpoint handler can return <c>404</c> rather than <c>422</c>.
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
