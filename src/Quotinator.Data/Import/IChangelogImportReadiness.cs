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
/// <para>
/// <b>Registration is not optional, and forgetting it fails quietly.</b> Any application wiring
/// <see cref="Quotinator.Data.Repositories.ChangelogReader"/> must also register this as a
/// <em>singleton</em> and have whatever runs the import call <see cref="MarkSucceeded"/> or
/// <see cref="MarkFailed"/> on every exit path. Omit the registration and each empty-database read
/// waits out its whole budget before falling back; register it per-scope instead of as a singleton and
/// each reader gets its own signal that the importer never completes. Both are correct but badly
/// degraded, and neither announces itself. This matters beyond Quotinator because
/// <c>Quotinator.Data</c> is meant to be reusable (ADR 003/004) — see
/// <c>docs/data-access.md</c>'s "Readiness signals" section.
/// </para>
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
