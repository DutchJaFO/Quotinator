using Quotinator.Core.Models;
using Quotinator.Engine.Models;
using Quotinator.Engine.Services;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>Test double for <see cref="IConflictResolutionService"/> — returns canned results or throws a configured exception, recording the arguments it was called with.</summary>
internal sealed class FakeConflictResolutionService : IConflictResolutionService
{
    public ConflictPageResponse? ReturnPage { get; set; }
    public Exception? ThrowOnDecide { get; set; }
    public Exception? ThrowOnUndo { get; set; }
    public ConflictBatchStatusResponse? ReturnApplyResult { get; set; }

    public Guid? LastDecidedConflictId { get; private set; }
    public ConflictDecisionRequest? LastDecisionRequest { get; private set; }
    public Guid? LastUndoneConflictId { get; private set; }
    public string? LastAppliedBatchId { get; private set; }

    public Task<ConflictPageResponse> GetPagedAsync(string? batchId, string? status, int page, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(ReturnPage ?? new ConflictPageResponse
        {
            TotalMatching = 0,
            TotalPages    = 0,
            Page          = page,
            PageSize      = pageSize,
            Items         = [],
        });

    public Task DecideAsync(Guid conflictId, ConflictDecisionRequest request, CancellationToken cancellationToken = default)
    {
        LastDecidedConflictId = conflictId;
        LastDecisionRequest   = request;
        if (ThrowOnDecide is not null) throw ThrowOnDecide;
        return Task.CompletedTask;
    }

    public Task UndoDecisionAsync(Guid conflictId, CancellationToken cancellationToken = default)
    {
        LastUndoneConflictId = conflictId;
        if (ThrowOnUndo is not null) throw ThrowOnUndo;
        return Task.CompletedTask;
    }

    public Task<ConflictBatchStatusResponse?> ApplyBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        LastAppliedBatchId = batchId;
        return Task.FromResult(ReturnApplyResult);
    }
}
