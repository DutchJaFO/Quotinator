namespace Quotinator.Data.Import;

/// <summary>
/// Thrown by <see cref="IImportActionCoordinator.DiscardBatchAsync"/> when a batch-level operation
/// isn't valid for the batch's current aggregate state — e.g. discarding a batch that has already
/// been applied, already been discarded, or has no staged actions at all.
/// </summary>
public sealed class ImportBatchStateException : Exception
{
    /// <summary>The batch id the operation was attempted on.</summary>
    public string BatchId { get; }

    /// <summary>Creates the exception with the batch id and a human-readable reason.</summary>
    public ImportBatchStateException(string batchId, string reason)
        : base($"Batch '{batchId}' {reason}")
    {
        BatchId = batchId;
    }
}
