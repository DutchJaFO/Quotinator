using Quotinator.Data.Enums;

namespace Quotinator.Data.Database.Checks;

/// <summary>Builds what a layer of the database check reports (#490), so every layer words the shared cases alike.</summary>
internal static class LayerResults
{
    /// <summary>The finding of a layer that had no file to check.</summary>
    internal const string NoFileYet =
        "There is no database file yet, so there is nothing to check: a first start or a Reset creates one.";

    /// <summary>The most engine diagnostic lines a finding carries; the rest are counted, not listed.</summary>
    private const int MaxFindingLines = 20;

    /// <summary>The layer passed.</summary>
    internal static DatabaseCheckLayerResult Passed(DatabaseCheckLayer layer, string? finding = null) =>
        new() { Layer = layer, Status = DatabaseCheckLayerStatus.Passed, Finding = finding };

    /// <summary>The layer failed, for the reason <paramref name="finding"/> gives.</summary>
    internal static DatabaseCheckLayerResult Failed(DatabaseCheckLayer layer, string finding) =>
        new() { Layer = layer, Status = DatabaseCheckLayerStatus.Failed, Finding = finding };

    /// <summary>The layer could not run soundly, for the reason <paramref name="finding"/> gives. Never a pass.</summary>
    internal static DatabaseCheckLayerResult Inconclusive(DatabaseCheckLayer layer, string finding) =>
        new() { Layer = layer, Status = DatabaseCheckLayerStatus.Inconclusive, Finding = finding };

    /// <summary>The layer passed because there is no file to check yet.</summary>
    internal static DatabaseCheckLayerResult NothingToCheck(DatabaseCheckLayer layer) => Passed(layer, NoFileYet);

    /// <summary>The engine's diagnostic lines joined into one finding, the first <see cref="MaxFindingLines"/> of them.</summary>
    internal static string Join(IReadOnlyList<string> lines) =>
        lines.Count <= MaxFindingLines
            ? string.Join("; ", lines)
            : string.Join("; ", lines.Take(MaxFindingLines)) + $"; and {lines.Count - MaxFindingLines} more";
}
