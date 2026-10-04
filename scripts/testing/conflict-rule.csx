#!/usr/bin/env dotnet-script
#nullable enable
// Removes a rule from a *-conflict-rules.json file, or changes one field's resolution in it, so a test
// can provoke the state it needs without a person editing the file by hand.
//
// docs/automated-testing/import-and-staged-actions/15-rule-file-live-read-proof.md is the reason it
// exists. That test proves the rule lookup reads the file's live content rather than a cached decision,
// which it can only do by changing the content between two runs — and until this script it asked the
// reader to delete a rule, rebuild, restore it, edit a value and rebuild again. A folder called
// automated-testing cannot contain a step that stops for a person (see that suite's index, "Every test
// must be able to run unattended"), and per ADR 010 the edit is a committed C# script rather than a
// shell text transformation.
//
// The edits are temporary by design. Revert them with `git checkout -- <file>` when the test is done —
// this script deliberately has no undo, because git already is one.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/conflict-rule.csx -- --file <path> --entity-id <id> --remove
//   dotnet-script scripts/testing/conflict-rule.csx -- --file <path> --entity-id <id> --field <name> --resolution <value>
//   dotnet-script scripts/testing/conflict-rule.csx -- --file <path> --entity-id <id> --field <name> --recorded-incoming <json>
//
// Options:
//   --file              <path>  The *-conflict-rules.json file to edit (required)
//   --entity-id         <id>    Which rule to act on, matched case-insensitively (required)
//   --remove                    Delete that rule entirely
//   --field             <name>  With --resolution or --recorded-incoming: which field entry to change
//   --resolution        <value> The new resolution for that field (e.g. Keep, Replace)
//   --recorded-incoming <json>  The field's new recordedIncomingValue, as raw JSON: "1999" (with the
//                               quotes) for a string, null to record an explicit null, ["drama"] for a
//                               list, or the literal word absent to remove the property entirely
//
// Exactly one of --remove, --resolution and --recorded-incoming is given. The file is rewritten as UTF-8
// without a BOM and re-indented by the serializer; the content is what matters here, not the formatting,
// and git restores the original either way.
//
// --recorded-incoming exists for #420/ADR 023: staleness is judged per field, against that field's own
// recordedIncomingValue, so changing that value is how a test reaches a Stale reading. Document 16
// (conflict-rule-staleness) names this script as the way to reach its own "before" state; until #420 it
// could only change a resolution or remove a rule, so that stated "before" was unreachable with the tool
// the document named. Note `absent` is a distinct outcome from `null`: no recorded value resolves Stale,
// while a recorded explicit null is a real value that can still match.

using System.Text.Json;
using System.Text.Json.Nodes;

string? Value(string flag) => Args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();

string? file             = Value("--file");
string? entityId         = Value("--entity-id");
string? field            = Value("--field");
string? resolution       = Value("--resolution");
string? recordedIncoming = Value("--recorded-incoming");
bool remove              = Args.Contains("--remove");

int modeCount = (remove ? 1 : 0) + (resolution is not null ? 1 : 0) + (recordedIncoming is not null ? 1 : 0);

if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(entityId) || modeCount != 1)
{
    Console.Error.WriteLine(
        "Usage: dotnet-script scripts/testing/conflict-rule.csx -- --file <path> --entity-id <id> "
        + "(--remove | --field <name> --resolution <value> | --field <name> --recorded-incoming <json>)");
    Environment.Exit(1);
    return;
}

if (!File.Exists(file))
{
    Console.Error.WriteLine($"Rule file not found: {file}");
    Environment.Exit(1);
    return;
}

// The one place this suite walks a JSON document by hand rather than deserializing into a POCO: the
// file's own shape is owned by Quotinator.Data's rule model, and duplicating it here would give the
// script its own copy to drift from. A test-only editor that preserves every key it does not touch is
// the narrower thing to build.
JsonNode root = JsonNode.Parse(File.ReadAllText(file))!;
JsonArray rules = root["rules"]!.AsArray();

int index = -1;

for (int i = 0; i < rules.Count; i++)
{
    if (string.Equals(rules[i]!["entityId"]?.GetValue<string>(), entityId, StringComparison.OrdinalIgnoreCase))
    {
        index = i;
        break;
    }
}

if (index < 0)
{
    // Not found is a hard failure, never a silent no-op: the test that calls this concludes something
    // from the file having changed, and an unchanged file would look exactly like the mechanism under
    // test not working.
    Console.Error.WriteLine($"No rule in {file} has entityId {entityId}.");
    Environment.Exit(1);
    return;
}

if (remove)
{
    rules.RemoveAt(index);
    Console.WriteLine($"{file}: removed the rule for {entityId}. {rules.Count} rule(s) remain.");
}
else
{
    if (string.IsNullOrEmpty(field))
    {
        Console.Error.WriteLine("--resolution and --recorded-incoming each need --field to say which field entry to change.");
        Environment.Exit(1);
        return;
    }

    JsonNode? target = rules[index]!["fields"]!.AsArray()
        .FirstOrDefault(f => string.Equals(f!["field"]?.GetValue<string>(), field, StringComparison.OrdinalIgnoreCase));

    if (target is null)
    {
        Console.Error.WriteLine($"The rule for {entityId} has no field entry named {field}.");
        Environment.Exit(1);
        return;
    }

    if (resolution is not null)
    {
        string previous = target["resolution"]?.GetValue<string>() ?? "(none)";
        target["resolution"] = resolution;

        Console.WriteLine($"{file}: {entityId} field '{field}' resolution {previous} -> {resolution}.");
    }
    else
    {
        JsonObject targetObject = target.AsObject();
        string previous = targetObject.TryGetPropertyValue("recordedIncomingValue", out JsonNode? existingValue)
            ? existingValue?.ToJsonString() ?? "null"
            : "(absent)";

        if (string.Equals(recordedIncoming, "absent", StringComparison.OrdinalIgnoreCase))
        {
            targetObject.Remove("recordedIncomingValue");
            Console.WriteLine($"{file}: {entityId} field '{field}' recordedIncomingValue {previous} -> (absent). That field now resolves Stale.");
        }
        else
        {
            try
            {
                // Parsed as JSON, not assigned as a string: the point is to be able to record a string,
                // an explicit null or a list, and "null" typed as a C# string would record the four
                // characters rather than a JSON null.
                targetObject["recordedIncomingValue"] = JsonNode.Parse(recordedIncoming!);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine(
                    $"--recorded-incoming must be raw JSON, not bare text: {ex.Message} "
                    + "Quote a string value (\"1999\"), or pass null, a JSON list, or the word absent.");
                Environment.Exit(1);
                return;
            }

            Console.WriteLine($"{file}: {entityId} field '{field}' recordedIncomingValue {previous} -> {recordedIncoming}.");
        }
    }
}

File.WriteAllText(
    file,
    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
    new System.Text.UTF8Encoding(false));
