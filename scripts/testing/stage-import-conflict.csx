#!/usr/bin/env dotnet-script
#nullable enable
// Writes a user-imports file that deliberately conflicts with an already-bundled quote, so a seed
// leaves import actions awaiting review (#303).
//
// Exists because the bundled sources cannot produce a conflict on their own, which makes #303's alert
// impossible to see without one. Two reasons, both measured while running
// docs/automated-testing/import-and-staged-actions/20-pending-review-alert.md:
//
//   * A first seed inserts everything as an Add. A conflict needs *existing* data that disagrees with
//     what is arriving, and on a fresh database there is nothing yet to disagree with.
//   * Quotinator__DefaultConflictPolicy=Review does not help: the manifest's own per-file policy takes
//     precedence, so the flag never reaches the bundled files.
//
// The file this writes re-states a real bundled quote's id with different text under a `review` policy.
// The user-imports batch seeds after the bundled ones, so it meets content that is already stored —
// the only shape that actually stages a decision.
//
// Developer-only: never referenced by src/Quotinator.Api, never built into the Docker image.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/stage-import-conflict.csx -- --imports <path-to-imports-dir>
//
// For a T1 run from Visual Studio that is the bin data directory, e.g.
//   dotnet-script scripts/testing/stage-import-conflict.csx -- --imports src/Quotinator.Api/bin/Debug/net10.0/data/imports
//
// Then start (or restart) the app: the seed stages one Pending action and raises the pending-review
// alert. Remove the two files it writes to go back to a clean seed.
//
// Options:
//   --imports <path>   The imports directory to write into; created when missing (required)
//   --source  <path>   The bundled file to take a real quote id from
//                      (default: data/sources/quotinator-curated.json)

using System.Text.Json;
using System.Text.Json.Nodes;

string? Value(string name)
{
    int i = Args.IndexOf(name);
    return i >= 0 && i + 1 < Args.Count ? Args[i + 1] : null;
}

string? importsDir = Value("--imports");
if (string.IsNullOrWhiteSpace(importsDir))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/testing/stage-import-conflict.csx -- --imports <path> [--source <path>]");
    return 1;
}

string sourcePath = Value("--source") ?? Path.Combine("data", "sources", "quotinator-curated.json");
if (!File.Exists(sourcePath))
{
    Console.Error.WriteLine($"Source file not found: {sourcePath}");
    return 1;
}

// Only to pick which shape the file uses — a bare array, or a { "quotes": [...] } wrapper. The quote
// itself is read as a node below rather than deserialized into a DTO: this script needs exactly two
// fields off it, and it lives outside the projects that own the real quote model.
JsonNode root = JsonNode.Parse(File.ReadAllText(sourcePath))!;
JsonArray quotes = root is JsonArray bare ? bare : root["quotes"]!.AsArray();

JsonNode target = quotes[0]!;
string id = target["id"]!.GetValue<string>();

// Same id, different text. The id is what makes this a Modify rather than an Add, and the differing
// text is what makes it ambiguous rather than an unchanged re-import.
var conflicting = new
{
    quotes = new[]
    {
        new
        {
            id,
            quote            = "A deliberately different text, staged to force a review decision.",
            originalLanguage = "en",
            source           = target["source"]?.GetValue<string>(),
            date             = target["date"]?.GetValue<string>(),
            type             = target["type"]?.GetValue<string>(),
            genres           = Array.Empty<string>(),
        },
    },
};

// `review` is the whole point: any other policy resolves the conflict silently and stages nothing.
var manifest = new
{
    duplicateResolution = new { @default = "review" },
    files = new[] { new { file = "conflicting.json", name = "test/conflicting" } },
};

Directory.CreateDirectory(importsDir);

JsonSerializerOptions options = new() { WriteIndented = true };
File.WriteAllText(Path.Combine(importsDir, "conflicting.json"), JsonSerializer.Serialize(conflicting, options));
File.WriteAllText(Path.Combine(importsDir, "manifest.json"), JsonSerializer.Serialize(manifest, options));

Console.WriteLine($"Staged a conflict against quote {id} in {Path.GetFullPath(importsDir)}");
Console.WriteLine("Restart the application to seed it — one Pending action and one pending-review alert.");
return 0;
