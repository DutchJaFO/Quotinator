using Microsoft.Extensions.Logging;

namespace Quotinator.Data.Import;

/// <summary>Builds the ordered list of <see cref="SeedBatch"/>es to seed from at startup.</summary>
public static class SeedBatchesBuilder
{
    /// <summary>
    /// Builds seed batches for the bundled sources directory and the user imports directory.
    /// </summary>
    /// <param name="bundledDir">Read-only directory bundled with the application image.</param>
    /// <param name="importsDir">User-writable directory scanned for import files.</param>
    /// <param name="configPolicy">Duplicate-resolution policy from application configuration.</param>
    /// <param name="includeDefaultSources">When <c>false</c>, the bundled sources directory is skipped entirely, even if it exists and contains files.</param>
    /// <param name="createMissingManifest">Whether a missing manifest in <paramref name="importsDir"/> may be auto-created.</param>
    /// <param name="planner">Manifest reader used to plan each directory's seed file list.</param>
    /// <param name="logger">Logger for startup diagnostics.</param>
    public static IReadOnlyList<SeedBatch> Build(
        string bundledDir, string importsDir, ManifestPolicy configPolicy,
        bool includeDefaultSources, bool createMissingManifest,
        IManifestSeedPlanner planner, ILogger logger)
    {
        var batches = new List<SeedBatch>();

        if (!includeDefaultSources)
        {
            logger.LogInformation("[Database - Init] Quotinator__IncludeDefaultSources is false — skipping bundled sources");
        }
        else if (Directory.Exists(bundledDir))
        {
            var (files, policy) = planner.PlanSeed(bundledDir, configPolicy, allowAutoCreate: false);
            if (files.Count > 0)
                batches.Add(new SeedBatch(files, policy, "bundled sources", SeedBatchOrigin.Bundled));
        }
        else
        {
            logger.LogWarning("[Database - Init] bundled sources directory not found at {Dir} — database will be empty on first run", bundledDir);
        }

        if (Directory.Exists(importsDir))
        {
            var (files, policy) = planner.PlanSeed(importsDir, configPolicy, allowAutoCreate: createMissingManifest);
            if (files.Count > 0)
                batches.Add(new SeedBatch(files, policy, "user imports", SeedBatchOrigin.UserImports));
        }

        return batches;
    }
}
