using Quotinator.Core.Models;
using Quotinator.Data.Models;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>Test double for <see cref="IImportActionService"/> — returns canned results or throws a configured exception, recording the arguments it was called with.</summary>
internal sealed class FakeImportActionService : IImportActionService
{
    public PagedItems<ImportActionSummaryResponse>? ReturnPage { get; set; }
    public Exception? ThrowOnDecide { get; set; }
    public Exception? ThrowOnUndo { get; set; }
    public Exception? ThrowOnDiscard { get; set; }
    public Exception? ThrowOnReverse { get; set; }
    public ImportActionBatchStatusResponse? ReturnApplyResult { get; set; }

    public Guid? LastDecidedActionId { get; private set; }
    public ConflictDecisionRequest? LastDecisionRequest { get; private set; }
    public Guid? LastUndoneActionId { get; private set; }
    public string? LastAppliedBatchId { get; private set; }
    public string? LastDiscardedBatchId { get; private set; }
    public string? LastReversedBatchId { get; private set; }

    public Task<PagedItems<ImportActionSummaryResponse>> GetPagedAsync(string? batchId, string? status, string? entityType, int page, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(ReturnPage ?? new PagedItems<ImportActionSummaryResponse>([], page, pageSize, 0));

    public Task DecideAsync(Guid actionId, ConflictDecisionRequest request, CancellationToken cancellationToken = default)
    {
        LastDecidedActionId = actionId;
        LastDecisionRequest = request;
        if (ThrowOnDecide is not null) throw ThrowOnDecide;
        return Task.CompletedTask;
    }

    public Task UndoDecisionAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        LastUndoneActionId = actionId;
        if (ThrowOnUndo is not null) throw ThrowOnUndo;
        return Task.CompletedTask;
    }

    public Task<ImportActionBatchStatusResponse?> ApplyBatchAsync(string batchId, InitiatorType initiatedByType = InitiatorType.WriteEndpoint, CancellationToken cancellationToken = default)
    {
        LastAppliedBatchId = batchId;
        return Task.FromResult(ReturnApplyResult);
    }

    public Task DiscardBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        LastDiscardedBatchId = batchId;
        if (ThrowOnDiscard is not null) throw ThrowOnDiscard;
        return Task.CompletedTask;
    }

    public bool? LastReversePreview { get; private set; }

    public Task ReverseBatchAsync(string batchId, bool preview = false, InitiatorType initiatedByType = InitiatorType.WriteEndpoint, CancellationToken cancellationToken = default)
    {
        LastReversedBatchId = batchId;
        LastReversePreview  = preview;
        if (ThrowOnReverse is not null) throw ThrowOnReverse;
        return Task.CompletedTask;
    }
}
