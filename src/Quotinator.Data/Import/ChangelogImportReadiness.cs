using Quotinator.Data.Enums;

namespace Quotinator.Data.Import;

/// <inheritdoc/>
/// <remarks>Initialises the signal with the budget a waiting reader is allowed to spend.</remarks>
/// <param name="waitBudget">How long a reader waits for the import to conclude before giving up. Defaults to <see cref="DefaultWaitBudget"/>; tests supply a short one so a timeout case does not cost real seconds.</param>
public sealed class ChangelogImportReadiness(TimeSpan? waitBudget = null) : IChangelogImportReadiness
{
    /// <summary>
    /// Default wait budget for a reader that finds the changelog database empty. Generous relative to
    /// the import's measured cost — roughly one second for the three bundled language files — because
    /// its only job is to stop a reader waiting forever if the import task dies without reporting.
    /// It is a backstop, not a tuned value.
    /// </summary>
    public static readonly TimeSpan DefaultWaitBudget = TimeSpan.FromSeconds(30);

    // RunContinuationsAsynchronously: without it, the import task's own MarkSucceeded call would run
    // every waiting reader's continuation inline on the import thread.
    private readonly TaskCompletionSource<ChangelogImportOutcome> _concluded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public void MarkSucceeded() => _concluded.TrySetResult(ChangelogImportOutcome.Succeeded);

    /// <inheritdoc/>
    public void MarkFailed() => _concluded.TrySetResult(ChangelogImportOutcome.Failed);

    /// <summary>The budget a waiting reader is allowed to spend before giving up.</summary>
    public TimeSpan WaitBudget { get; } = waitBudget ?? DefaultWaitBudget;

    /// <inheritdoc/>
    public async Task<ChangelogImportOutcome> WaitAsync(CancellationToken cancellationToken = default)
    {
        if (_concluded.Task.IsCompleted) return await _concluded.Task;

        using CancellationTokenSource delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task completed = await Task.WhenAny(_concluded.Task, Task.Delay(WaitBudget, delayCancellation.Token));

        if (completed != _concluded.Task) return ChangelogImportOutcome.TimedOut;

        // The import concluded first — stop the timer rather than leaving it to run out on its own.
        await delayCancellation.CancelAsync();
        return await _concluded.Task;
    }
}
