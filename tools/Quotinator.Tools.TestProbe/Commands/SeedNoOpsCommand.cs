using System.Text.Json;
using System.Text.Json.Serialization;
using Quotinator.Tools.TestProbe.Arguments;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>
/// <c>seed-noops</c>: checks every <c>ResolvedToExisting</c> action in a saved <c>/import/actions?pageSize=0</c>
/// response is accounted for: a rule in a <c>*conflict-rules.json</c> file in <c>--rules</c> covers its entity,
/// or the incoming side left empty every field that differs. Having no such action at all fails, since that
/// would satisfy the check too.
/// </summary>
internal sealed class SeedNoOpsCommand : IProbeCommand
{
    private const string NoOpKind = "ResolvedToExisting";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        string? list = OptionReader.Single(args, "--actions");
        string? rulesDirectory = OptionReader.Single(args, "--rules");

        if (list is null || rulesDirectory is null || !File.Exists(list) || !Directory.Exists(rulesDirectory))
        {
            return ExpectationResult.Failed("seed-noops needs --actions <file> and --rules <directory>, both existing");
        }

        List<ActionRow> noOps =
        [
            .. (JsonSerializer.Deserialize<ActionList>(File.ReadAllText(list), Options)?.Items ?? [])
                .Where(action => string.Equals(action.ActionType, NoOpKind, StringComparison.OrdinalIgnoreCase)),
        ];

        if (noOps.Count == 0)
        {
            return ExpectationResult.Failed($"the response holds no {NoOpKind} action, so there is nothing to check");
        }

        HashSet<string> ruled = ReadRuledEntities(rulesDirectory);
        List<string> observations = [$"resolvedToExisting = {noOps.Count}"];
        observations.AddRange(noOps.GroupBy(action => action.EntityType).OrderBy(group => group.Key).Select(group => $"  {group.Key} = {group.Count()}"));

        List<string> failures =
        [
            .. noOps.Where(action => !ruled.Contains(action.EntityId) && action.IncomingDisagrees())
                .Select(action => $"unexplained no-op: {action.EntityType} {action.EntityId}"),
        ];

        return new ExpectationResult(observations, failures);
    }

    private static HashSet<string> ReadRuledEntities(string directory)
    {
        HashSet<string> ruled = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(directory, "*conflict-rules.json"))
        {
            foreach (RuleRow rule in JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(file), Options)?.Rules ?? [])
            {
                ruled.Add(rule.EntityId);
            }
        }

        return ruled;
    }

    /// <summary>One import action.</summary>
    private sealed class ActionRow
    {
        /// <summary>The action's kind.</summary>
        [JsonPropertyName("actionType")]
        public string ActionType { get; init; } = string.Empty;

        /// <summary>The entity type it concerns.</summary>
        [JsonPropertyName("entityType")]
        public string EntityType { get; init; } = string.Empty;

        /// <summary>The entity it concerns.</summary>
        [JsonPropertyName("entityId")]
        public string EntityId { get; init; } = string.Empty;

        /// <summary>What is stored.</summary>
        [JsonPropertyName("existingFields")]
        public Dictionary<string, JsonElement>? ExistingFields { get; init; }

        /// <summary>What arrived.</summary>
        [JsonPropertyName("incomingFields")]
        public Dictionary<string, JsonElement>? IncomingFields { get; init; }

        /// <summary>True when the incoming side holds a value that differs from the stored one.</summary>
        public bool IncomingDisagrees()
            => (ExistingFields ?? []).Any(existing =>
            {
                string incoming = Text(IncomingFields, existing.Key);

                return incoming.Length > 0 && incoming != Text(ExistingFields, existing.Key);
            });

        private static string Text(Dictionary<string, JsonElement>? fields, string name)
        {
            if (fields is null
                || !fields.TryGetValue(name, out JsonElement value)
                || value.ValueKind == JsonValueKind.Null
                || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0))
            {
                return string.Empty;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
        }
    }

    /// <summary>A saved list response.</summary>
    private sealed class ActionList
    {
        /// <summary>The rows.</summary>
        [JsonPropertyName("items")]
        public List<ActionRow>? Items { get; init; }
    }

    /// <summary>One conflict resolution rule, as far as this check needs it.</summary>
    private sealed class RuleRow
    {
        /// <summary>The entity the rule covers.</summary>
        [JsonPropertyName("entityId")]
        public string EntityId { get; init; } = string.Empty;
    }

    /// <summary>A conflict rules file.</summary>
    private sealed class RuleFile
    {
        /// <summary>Its rules.</summary>
        [JsonPropertyName("rules")]
        public List<RuleRow>? Rules { get; init; }
    }
}
