using Quotinator.Data.Enums;

namespace Quotinator.Data.Import;

/// <summary>
/// Lets a reader find out whether the startup changelog import has concluded, and how (#309).
/// </summary>
/// <remarks>
/// Exists because an empty changelog database is ambiguous on its own: it means "the import has not
/// written anything yet" during the startup window, and "there is genuinely nothing to show" once the
/// import has finished. Found live — the what's-new producer and the import run as separate detached
/// tasks, and the producer won that race on every single boot, so the JSON fallback quietly served the
/// startup read every time while the database-backed path this issue built was never the one that
/// answered. Falling back on emptiness alone answers a question that has not been asked yet.
/// </remarks>
public interface IChangelogImportReadiness
{
    /// <summary>Records that the import completed successfully. Releases every waiting reader.</summary>
    void MarkSucceeded();

    /// <summary>Records that the import threw. Releases every waiting reader, which then fall back to the JSON files.</summary>
    void MarkFailed();

    /// <summary>
    /// Waits for the import to conclude, returning how it concluded — or
    /// <see cref="ChangelogImportOutcome.TimedOut"/> if it has not concluded within this instance's own
    /// wait budget. Returns immediately once an outcome is already known, so this costs nothing after
    /// startup. The budget belongs to the implementation rather than the caller: a reader has no basis
    /// for choosing one, and every reader must wait the same amount.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    Task<ChangelogImportOutcome> WaitAsync(CancellationToken cancellationToken = default);
}
