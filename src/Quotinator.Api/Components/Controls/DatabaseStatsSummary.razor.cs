using Microsoft.AspNetCore.Components;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Repositories;
using I18nTextService = Toolbelt.Blazor.I18nText.I18nText;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// Database counts and per-file import history (#263) — the counts and file table shared by
/// <see cref="StartupErrorModal"/> and <see cref="Pages.Stats"/>. The counts reflect
/// <see cref="IDatabaseInitializer"/>'s current state, which stays meaningful (the
/// last known-good snapshot) even while the database is degraded. The file table reads
/// <see cref="IFileResourceRepository"/>/<see cref="IImportBatchRepository"/> (#251's persisted
/// provenance) rather than <see cref="IDatabaseInitializer.LastSeedReport"/> — that report only
/// reflects what happened during the current process's own most recent seed pass, which is empty on
/// every ordinary restart once the database already has data (seeding is skipped entirely), making it
/// look like the file history was lost. The persisted store has no such gap.
/// </summary>
public partial class DatabaseStatsSummary
{
    #region Protected

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        Text = await I18nText.GetTextTableAsync<Quotinator.Api.I18nText.UI>(this);

        var page = await FileResourceRepository.GetPageAsync(fileName: null, origin: null, page: 1, pageSize: 0);
        var latestPerFile = page.Items
            .GroupBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase);

        var rows = new List<FileHistoryRow>();
        foreach (var item in latestPerFile)
        {
            var batchIds = await FileResourceRepository.GetBatchIdsAsync(item.Id);
            var batches  = new List<ImportBatchEntity>(batchIds.Count);
            foreach (var batchId in batchIds)
            {
                var batch = await ImportBatchRepository.GetByIdAsync(batchId);
                if (batch is not null) batches.Add(batch);
            }

            // A file resource linked to batches with more than one distinct Url (e.g. manifest.json,
            // which is captured alongside every batch it drives, per each of those batches' own
            // upstream source) has no single meaningful "source batch" — showing an arbitrary one's
            // Url/RecordCount against this file would misattribute another file's provenance to it.
            var representative = batches.Select(b => b.Url).Distinct().Count() <= 1
                ? batches.OrderByDescending(b => b.ImportedAt, StringComparer.Ordinal).FirstOrDefault()
                : null;

            rows.Add(new FileHistoryRow(item.FileName, item.Origin.Parsed, representative?.Url, item.LastSeenAtUtc.Parsed, representative?.RecordCount));
        }

        FileHistory = rows;
    }

    #endregion

    #region Private

    [Inject] private I18nTextService I18nText { get; set; } = default!;
    [Inject] private IDatabaseInitializer DatabaseInitializer { get; set; } = default!;
    [Inject] private IFileResourceRepository FileResourceRepository { get; set; } = default!;
    [Inject] private IImportBatchRepository ImportBatchRepository { get; set; } = default!;

    private Quotinator.Api.I18nText.UI Text = new();
    private IReadOnlyList<FileHistoryRow> FileHistory = [];

    private string OriginLabel(FileResourceOrigin? origin) => origin switch
    {
        FileResourceOrigin.System => Text.FileOriginSystem,
        FileResourceOrigin.User   => Text.FileOriginUser,
        FileResourceOrigin.Upload => Text.FileOriginUpload,
        _                         => "—",
    };

    /// <summary>One display row of the file import history table.</summary>
    private sealed record FileHistoryRow(string FileName, FileResourceOrigin? Origin, string? SourceUrl, DateTime? LastUpdatedUtc, int? RecordCount);

    #endregion
}
