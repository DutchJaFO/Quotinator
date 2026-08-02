using Quotinator.Data.Enums;

namespace Quotinator.Data.Import;

/// <summary>A group of source files that share a common manifest and duplicate-resolution policy.</summary>
/// <param name="Files">Source files, in the order they should be imported.</param>
/// <param name="Policy">Resolved duplicate-resolution policy governing all files in this batch.</param>
/// <param name="Label">Human-readable label used in log messages (e.g. <c>"bundled sources"</c>, <c>"user imports"</c>).</param>
/// <param name="Origin">Where this batch's files were discovered from. Determines provenance classification independently of any URL the files declare.</param>
/// <param name="SourceDirectory">
/// The directory <see cref="IManifestSeedPlanner.PlanSeed"/> scanned to build <paramref name="Files"/>
/// (<c>bundledDir</c>/<c>importsDir</c> in <see cref="SeedBatchesBuilder.Build"/>) — <see langword="null"/>
/// only for batches built directly in tests rather than via <see cref="SeedBatchesBuilder"/>. Kept
/// separately from any individual <see cref="SeedFile.FilePath"/> because
/// <see cref="ISourceCacheUpdater.ResolveAsync"/> rewrites a downloaded file's own
/// <see cref="SeedFile.FilePath"/> to a separate download-cache directory (#251's own manifest.json
/// capture needs the original directory, not the cache one, to find <c>manifest.json</c> — found live
/// when a T2 pass showed the manifest's own <c>FileResource</c> row correctly captured but linked to
/// only 2 of 4 bundled batches, the 2 whose files were never cache-redirected).
/// </param>
public record SeedBatch(
    IReadOnlyList<SeedFile> Files,
    ManifestPolicy          Policy,
    string                  Label,
    SeedBatchOrigin         Origin = SeedBatchOrigin.Bundled,
    string?                 SourceDirectory = null);
