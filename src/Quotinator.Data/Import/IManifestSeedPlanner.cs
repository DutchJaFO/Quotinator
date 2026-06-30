namespace Quotinator.Data.Import;

/// <summary>Reads (or auto-creates) the <c>manifest.json</c> in a source directory and builds the ordered seed file list.</summary>
public interface IManifestSeedPlanner
{
    /// <summary>
    /// Reads <c>manifest.json</c> in <paramref name="dir"/> and returns the ordered file list and resolved
    /// duplicate-resolution policy. Files not listed in the manifest are appended alphabetically after listed
    /// entries. When no manifest exists, a new one is auto-created only if <paramref name="allowAutoCreate"/>
    /// is <c>true</c> and the directory contains at least one source file.
    /// </summary>
    /// <param name="dir">Directory containing source JSON files and an optional <c>manifest.json</c>.</param>
    /// <param name="configPolicy">Duplicate-resolution policy from application configuration, used when the manifest has none of its own.</param>
    /// <param name="allowAutoCreate">Whether a missing manifest may be auto-created. Must be <c>false</c> for read-only directories (e.g. the bundled sources directory under Docker/HA).</param>
    (IReadOnlyList<SeedFile> Files, ManifestPolicy Policy) PlanSeed(
        string dir, ManifestPolicy configPolicy, bool allowAutoCreate);
}
