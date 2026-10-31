using System.Text.Json;
using System.Text.Json.Serialization;
using Quotinator.Tools.TestProbe.Arguments;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>
/// <c>seed-sources</c>: checks the Sources a saved <c>/masterdata/sources?pageSize=0</c> response lists for
/// exact duplicates, casing-only duplicates, and date variants nobody has declared in the <c>sources[]</c>
/// of the files in <c>--declared</c>. An empty list fails, since it would satisfy all three.
/// </summary>
internal sealed class SeedSourcesCommand : IProbeCommand
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        string? list = OptionReader.Single(args, "--sources");
        string? declaredDirectory = OptionReader.Single(args, "--declared");

        if (list is null || declaredDirectory is null || !File.Exists(list) || !Directory.Exists(declaredDirectory))
        {
            return ExpectationResult.Failed("seed-sources needs --sources <file> and --declared <directory>, both existing");
        }

        List<SourceRow> rows = JsonSerializer.Deserialize<SourceList>(File.ReadAllText(list), Options)?.Items ?? [];

        if (rows.Count == 0)
        {
            return ExpectationResult.Failed("the response lists no sources, so there is nothing to check");
        }

        HashSet<string> declared = ReadDeclared(declaredDirectory);
        List<string> failures = [];

        foreach (IGrouping<string, SourceRow> sameWork in rows.GroupBy(row => row.WorkKey(), StringComparer.OrdinalIgnoreCase))
        {
            failures.AddRange(sameWork
                .GroupBy(row => row.Date ?? string.Empty)
                .Where(sameDate => sameDate.Count() > 1)
                .Select(sameDate => $"exact duplicate: {sameWork.Key}, date '{sameDate.Key}', {sameDate.Count()} rows"));

            if (sameWork.Select(row => row.Title).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                failures.Add($"casing duplicate: {string.Join(" / ", sameWork.Select(row => row.Title).Distinct(StringComparer.Ordinal))}");
            }

            if (sameWork.Count() > 1)
            {
                failures.AddRange(sameWork
                    .Where(row => !declared.Contains(row.Key()))
                    .Select(row => $"undeclared date variant: {row.Key()}"));
            }
        }

        return new ExpectationResult([$"sources = {rows.Count}", $"titles carrying more than one row = {rows.GroupBy(row => row.WorkKey(), StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1)}"], failures);
    }

    private static HashSet<string> ReadDeclared(string directory)
    {
        HashSet<string> declared = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(directory, "*.json"))
        {
            string json = File.ReadAllText(file);

            // A source file may be a bare array of quotes, which declares no Sources at all.
            using (JsonDocument shape = JsonDocument.Parse(json))
            {
                if (shape.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
            }

            foreach (SourceRow row in JsonSerializer.Deserialize<DeclaredSources>(json, Options)?.Sources ?? [])
            {
                declared.Add(row.Key());
            }
        }

        return declared;
    }

    /// <summary>One Source, as the API lists it and as a file declares it.</summary>
    private sealed class SourceRow
    {
        /// <summary>The title, with its casing.</summary>
        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        /// <summary>The source type.</summary>
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        /// <summary>The date, when it has one.</summary>
        [JsonPropertyName("date")]
        public string? Date { get; init; }

        /// <summary>What identifies the work whatever its date.</summary>
        public string WorkKey() => $"{Title}|{Type}";

        /// <summary>What identifies this dated version of the work.</summary>
        public string Key() => $"{WorkKey()}|{Date}";
    }

    /// <summary>A saved list response.</summary>
    private sealed class SourceList
    {
        /// <summary>The rows.</summary>
        [JsonPropertyName("items")]
        public List<SourceRow>? Items { get; init; }
    }

    /// <summary>The part of a source file that declares Sources.</summary>
    private sealed class DeclaredSources
    {
        /// <summary>The declarations, when the file has any.</summary>
        [JsonPropertyName("sources")]
        public List<SourceRow>? Sources { get; init; }
    }
}
