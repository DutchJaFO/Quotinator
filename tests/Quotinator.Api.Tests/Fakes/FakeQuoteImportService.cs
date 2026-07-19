using Quotinator.Core.Models;
using Quotinator.Data.Import;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>Test double for <see cref="IQuoteImportService"/> — returns a canned result or throws a configured exception, recording the arguments it was called with.</summary>
internal sealed class FakeQuoteImportService : IQuoteImportService
{
    public Exception? ThrowOnImport { get; set; }
    public Exception? ThrowOnApplyStagedBatch { get; set; }
    public ImportResultResponse? ReturnResult { get; set; }
    public ImportRequestSettingsDto? LastSettings { get; private set; }
    public bool? LastPreview { get; private set; }
    public string? LastFileName { get; private set; }
    public Guid? LastAppliedBatchId { get; private set; }

    public Task<ImportResultResponse> ImportAsync(
        Stream file, string fileName, ImportRequestSettingsDto? settings, bool preview,
        CancellationToken cancellationToken = default)
    {
        LastSettings = settings;
        LastPreview  = preview;
        LastFileName = fileName;

        if (ThrowOnImport is not null) throw ThrowOnImport;

        return Task.FromResult(ReturnResult ?? new ImportResultResponse
        {
            BatchId        = preview ? null : Guid.NewGuid(),
            Preview        = preview,
            ConflictPolicy = "newest-wins",
            Summary        = new ImportSummary { Total = 1, Imported = 1, Updated = 0, Skipped = 0, Errors = 0 }
        });
    }

    public Task<ImportResultResponse> ApplyStagedBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        LastAppliedBatchId = batchId;

        if (ThrowOnApplyStagedBatch is not null) throw ThrowOnApplyStagedBatch;

        return Task.FromResult(ReturnResult ?? new ImportResultResponse
        {
            BatchId        = batchId,
            Preview        = false,
            ConflictPolicy = "newest-wins",
            Summary        = new ImportSummary { Total = 1, Imported = 1, Updated = 0, Skipped = 0, Errors = 0 }
        });
    }
}
