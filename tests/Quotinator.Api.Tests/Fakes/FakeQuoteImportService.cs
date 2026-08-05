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
    public ImportSettingsDto? LastSettings { get; private set; }
    public bool? LastPreview { get; private set; }
    public string? LastFileName { get; private set; }
    public Guid? LastAppliedBatchId { get; private set; }
    public bool? LastImportPurgeOnSuccess { get; private set; }
    public bool? LastApplyPurgeOnSuccess { get; private set; }

    public Task<ImportResultResponse> ImportAsync(
        Stream file, string fileName, ImportSettingsDto? settings, bool preview, bool purgeOnSuccess = false,
        CancellationToken cancellationToken = default)
    {
        LastSettings = settings;
        LastPreview  = preview;
        LastFileName = fileName;
        LastImportPurgeOnSuccess = purgeOnSuccess;

        if (ThrowOnImport is not null) throw ThrowOnImport;

        return Task.FromResult(ReturnResult ?? new ImportResultResponse
        {
            BatchId        = preview ? null : Guid.NewGuid(),
            Preview        = preview,
            ConflictPolicy = "newest-wins",
            Summary        = new ImportSummary { Total = 1, Imported = 1, Updated = 0, Skipped = 0, Errors = 0 },
            Report         = new FileImportReport { FileName = fileName, EntityTypes = new Dictionary<string, EntityTypeActionCounts>() }
        });
    }

    public Task<ImportResultResponse> ApplyStagedBatchAsync(Guid batchId, bool purgeOnSuccess = false, CancellationToken cancellationToken = default)
    {
        LastAppliedBatchId = batchId;
        LastApplyPurgeOnSuccess = purgeOnSuccess;

        if (ThrowOnApplyStagedBatch is not null) throw ThrowOnApplyStagedBatch;

        return Task.FromResult(ReturnResult ?? new ImportResultResponse
        {
            BatchId        = batchId,
            Preview        = false,
            ConflictPolicy = "newest-wins",
            Summary        = new ImportSummary { Total = 1, Imported = 1, Updated = 0, Skipped = 0, Errors = 0 },
            Report         = new FileImportReport { FileName = "staged-batch", EntityTypes = new Dictionary<string, EntityTypeActionCounts>() }
        });
    }
}
