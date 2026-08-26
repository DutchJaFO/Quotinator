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
//
// Options:
//   --file       <path>   The *-conflict-rules.json file to edit (required)
//   --entity-id  <id>     Which rule to act on, matched case-insensitively (required)
//   --remove              Delete that rule entirely
//   --field      <name>   With --resolution: which field entry to change (e.g. date)
//   --resolution <value>  The new resolution for that field (e.g. Keep, Replace)
//
// Exactly one of --remove and --resolution is given. The file is rewritten as UTF-8 without a BOM and
// re-indented by the serializer; the content is what matters here, not the formatting, and git restores
// the original either way.

using System.Text.Json;
using System.Text.Json.Nodes;

string? Value(string flag) => Args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();

string? file       = Value("--file");
string? entityId   = Value("--entity-id");
string? field      = Value("--field");
string? resolution = Value("--resolution");
bool remove        = Args.Contains("--remove");

if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(entityId) || remove == (resolution is not null))
{
    Console.Error.WriteLine(
        "Usage: dotnet-script scripts/testing/conflict-rule.csx -- --file <path> --entity-id <id> "
        + "(--remove | --field <name> --resolution <value>)");
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
        Console.Error.WriteLine("--resolution needs --field to say which field entry to change.");
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

    string previous = target["resolution"]?.GetValue<string>() ?? "(none)";
    target["resolution"] = resolution;

    Console.WriteLine($"{file}: {entityId} field '{field}' resolution {previous} -> {resolution}.");
}

File.WriteAllText(
    file,
    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
    new System.Text.UTF8Encoding(false));
