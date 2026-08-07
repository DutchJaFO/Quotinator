namespace Quotinator.Data.Import;

/// <summary>
/// Thrown by <see cref="IImportActionCoordinator.DiscardBatchAsync"/> when a batch-level operation
/// isn't valid for the batch's current aggregate state — e.g. discarding a batch that has already
/// been applied, already been discarded, or has no staged actions at all.
/// </summary>
/// <remarks>Creates the exception with the batch id and a human-readable reason.</remarks>
/// <param name="batchId">The batch id the operation was attempted on.</param>
/// <param name="reason">Human-readable explanation of why the operation isn't valid for the batch's current state.</param>
public sealed class ImportBatchStateException(string batchId, string reason) : Exception($"Batch '{batchId}' {reason}")
{
    /// <summary>The batch id the operation was attempted on.</summary>
    public string BatchId { get; } = batchId;
}
