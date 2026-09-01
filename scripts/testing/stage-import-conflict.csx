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
//     precedence, so the flag never reaches the bundled files. (data/sources/manifest.json sets `skip`
//     at its top level and `review` per file — neither of which governs the imports directory.)
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
// Do not add further files to that directory by hand, and do not delete the manifest. A file the
// manifest does not name — and every file, once the manifest is gone and the application auto-creates
// one with no duplicateResolution at all — falls back to the configuration default, which is
// `newest-wins` (ManifestPolicy.HardcodedDefault). That does not merely stage nothing: it applies the
// incoming value over the stored one without asking. The fixture goes quiet and the data changes.
// Use --count for more than one conflicting file instead.
//
// Options:
//   --imports <path>   The imports directory to write into; created when missing (required)
//   --count   <n>      How many conflicting files to write, each against a different bundled quote
//                      (default: 1). One alert is raised per file that stages a decision.
//   --source  <path>   The bundled file to take real quote ids from
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

int count = int.TryParse(Value("--count"), out int requested) ? requested : 1;
if (count < 1 || count > quotes.Count)
{
    Console.Error.WriteLine($"--count must be between 1 and {quotes.Count} (the number of quotes in {sourcePath}).");
    return 1;
}

Directory.CreateDirectory(importsDir);

List<object> manifestFiles = [];
List<string> stagedIds = [];

for (int i = 0; i < count; i++)
{
    // A different quote per file. Pointing two files at the same quote does not produce two conflicts:
    // the first one's change is applied, and the second then agrees with what is now stored.
    JsonNode target = quotes[i]!;
    string id = target["id"]!.GetValue<string>();
    string fileName = count == 1 ? "conflicting.json" : $"conflicting-{i + 1}.json";

    // Same id, different text. The id is what makes this a Modify rather than an Add, and the differing
    // text is what makes it ambiguous rather than an unchanged re-import.
    var conflicting = new
    {
        quotes = new[]
        {
            new
            {
                id,
                quote            = $"A deliberately different text ({i + 1}), staged to force a review decision.",
                originalLanguage = "en",
                source           = target["source"]?.GetValue<string>(),
                date             = target["date"]?.GetValue<string>(),
                type             = target["type"]?.GetValue<string>(),
                genres           = Array.Empty<string>(),
            },
        },
    };

    File.WriteAllText(Path.Combine(importsDir, fileName),
        JsonSerializer.Serialize(conflicting, new JsonSerializerOptions { WriteIndented = true }));

    manifestFiles.Add(new { file = fileName, name = $"test/{Path.GetFileNameWithoutExtension(fileName)}" });
    stagedIds.Add(id);
}

// `review` is the whole point: the default (`newest-wins`) resolves the conflict silently by applying
// the incoming value. An auto-created manifest carries no policy at all, which is why this one must
// name every file written — a file it does not list falls back to that default and overwrites instead
// of asking.
var manifest = new
{
    duplicateResolution = new { @default = "review" },
    files = manifestFiles,
};

File.WriteAllText(Path.Combine(importsDir, "manifest.json"),
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Staged {count} conflict(s) in {Path.GetFullPath(importsDir)}:");
foreach (string stagedId in stagedIds) Console.WriteLine($"  against quote {stagedId}");
Console.WriteLine($"Restart the application to seed them — {count} Pending action(s), {count} pending-review alert(s).");
return 0;
