namespace Quotinator.Data.Import;

/// <summary>
/// Thrown by a consumer's staged-batch-apply flow (e.g. an <c>ApplyStagedBatchAsync</c> method) when
/// the given batch id does not exist. A distinct type from any generic validation exception a
/// consumer defines, so its endpoint handler can return <c>404</c> rather than <c>422</c>.
/// </summary>
/// <remarks>Creates the exception for the given missing batch id.</remarks>
/// <param name="batchId">The batch id that was not found.</param>
public sealed class ImportBatchNotFoundException(Guid batchId) : Exception($"Import batch '{batchId}' does not exist.")
{
    /// <summary>The batch id that was not found.</summary>
    public Guid BatchId { get; } = batchId;
}
