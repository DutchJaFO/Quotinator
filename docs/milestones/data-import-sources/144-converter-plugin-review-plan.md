# #144 — Converter plugins: generic naming, internal-only slots, configuration options

**Status:** Waiting for release
**GitHub issue:** #144
**Depends on:** #140

---

## Scope changes

Supersedes an earlier draft of this plan that treated `NikhilNamal17` as splitting into a generic
"field-mapped JSON" plugin and `Vilaboim` as a pure rename. The user redefined the target architecture
directly: three supported import formats, with `NikhilNamal17` and `Vilaboim` retired as dedicated
plugin *projects* entirely, becoming pure manifest configuration of two new generic plugins.

**The three formats:**

0. **Canonical JSON** (`schemas/source-flat.schema.json`/`source-extended.schema.json`) — needs no
   converter; already the format the seed/import pipeline reads directly. The only format that can
   express conversations (#67) or multiple translations. No code changes here — stated explicitly as
   in-scope-to-confirm, not silently assumed.
1. **CSV** (`Quotinator.Converters.Csv`, enhanced in place) — one record per row, optional header line.
   Already supports auto-matching columns to canonical property names by header text; gains an
   explicit `[column index] = [target property]` mapping (1-based) for headers that don't match
   canonical names (or files with no header at all), plus `[canonical property] = [default value]`
   pairs for fields not sourced from any column. Cannot express conversations.
2. **Basic JSON-array** (new `Quotinator.Converters.BasicJsonArray`) — a flat JSON array of objects.
   Same options mechanism as CSV, except mapping keys are raw JSON property names instead of column
   indexes: `[source property] = [target property]`, plus the same default-value pairs. Cannot express
   conversations.

**A fourth, related mechanism — not itself one of the three named formats, but reusing format 1's
indexing convention exactly as the user specified:** a JSON array of bare strings, each parsed via a
manifest-supplied regex whose capture groups map to canonical properties by the same 1-based index
convention as CSV (`Quotinator.Converters.RegexArray`, new). Confirmed with the user: the regex
**pattern itself is also a manifest option**, not hardcoded per source — this keeps the plugin fully
generic, consistent with formats 1 and 2, rather than leaving one bespoke plugin behind.

**`NikhilNamal17` and `Vilaboim` need no dedicated code after this.** Both become manifest entries:
`NikhilNamal17` configures `basic-json-array` (`converterOptions: {"propertyMapping": {"source":
"movie", "date": "year"}}` — its raw `quote`/`type` properties already match canonical names, so only
the two that differ need an entry); `Vilaboim` configures `regex-array`
(`converterOptions: {"pattern": "^\"(.+?)\"\\s+(.+)$", "groupMapping": {"quote": 1, "source": 2}}`).
Net converter-project count stays at three (`Csv`, `BasicJsonArray`, `RegexArray`) instead of today's
three (`Csv`, `Vilaboim`, `NikhilNamal17`) — no growth, but zero source-specific naming left anywhere.

**Configuration is typed classes, not a flat `Dictionary<string,string>`.** An earlier draft of this
plan routed everything through one `converterOptions` dictionary, disambiguated at runtime by a rule
("a key matching a canonical property name is a default; anything else is a mapping"). Reworked after
review: a dictionary doesn't document what options a plugin actually accepts, and this session already
established a strong precedent for replacing stringly-typed, ambiguous shapes with real C# types
wherever a fixed, known set of options exists (`InputValidation`'s enum-derived vocabularies,
`EntityType`'s move off raw string literals — see #59's work this session). `Quotinator.Data.Import
.ManifestPolicyDto` (`Default`/`Quotes`/`Sources`/`Characters`/`People`/`Translations`, each a plain
nullable typed slot) is this project's own existing pattern for exactly this problem — the new options
classes follow it directly instead of a dictionary. With mapping and defaults as distinct,
separately-named properties, there's nothing left to disambiguate — the runtime heuristic is dropped
entirely.

**Column/group index base: 1-based**, confirmed via the accepted example
(`"1": "quote", "2": "source"`) when resolving the regex-pattern-configurability question.

**Two shared shape classes + one shared defaults class, in `Quotinator.Core.Import`** (alongside
`QuoteTypeNormalisation.cs`/`YearParsing.cs`, which all three plugins already depend on via their
existing `Quotinator.Core` reference):

```csharp
/// <summary>Which raw column/capture-group index each canonical quote field is read from. Every slot
/// is optional — an unmapped field falls back to <see cref="QuoteFieldDefaults"/>, then its own
/// built-in default.</summary>
public sealed class IndexedFieldMapping
{
    public int? Id { get; init; }
    public int? Quote { get; init; }
    public int? OriginalLanguage { get; init; }
    public int? Source { get; init; }
    public int? Date { get; init; }
    public int? Character { get; init; }
    public int? Author { get; init; }
    public int? Type { get; init; }
    public int? Genres { get; init; }
}

/// <summary>Same shape as <see cref="IndexedFieldMapping"/>, keyed by raw JSON property name instead
/// of a numeric index — used by name-based formats (a flat JSON object array).</summary>
public sealed class NamedFieldMapping
{
    public string? Id { get; init; }
    public string? Quote { get; init; }
    public string? OriginalLanguage { get; init; }
    public string? Source { get; init; }
    public string? Date { get; init; }
    public string? Character { get; init; }
    public string? Author { get; init; }
    public string? Type { get; init; }
    public string? Genres { get; init; }
}

/// <summary>Literal default values applied to a canonical field not sourced from the raw row at all.
/// Quote/Source/Id are excluded deliberately — the first two are required per row (a row missing
/// either is skipped, never defaulted), and a single fixed id for every row would collide.</summary>
public sealed class QuoteFieldDefaults
{
    public string? OriginalLanguage { get; init; }
    public QuoteType? Type { get; init; }
    public string? Date { get; init; }
    public string? Character { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<string>? Genres { get; init; }
}
```

**Shared assembly helper, also in `Quotinator.Core.Import`** — the one piece of logic genuinely
identical across all three plugins:

```csharp
public static class MappedSourceQuoteBuilder
{
    /// <summary>Coalesces a raw value with its configured default — empty/whitespace counts as absent.</summary>
    public static string? Resolve(string? rawValue, string? defaultValue) =>
        !string.IsNullOrWhiteSpace(rawValue) ? rawValue.Trim() : defaultValue;

    /// <summary>Assembles one row's already-resolved field values into a <see cref="SourceQuote"/>,
    /// or <c>null</c> if quote/source ended up empty. Applies the same id-derivation, type-normalisation,
    /// and default-language rules every existing converter already uses.</summary>
    public static SourceQuote? Build(
        string? id, string? quote, string? originalLanguage, string? source, string? date,
        string? character, string? author, string? typeRaw, IReadOnlyList<string>? genres);
}
```

Each plugin resolves its own 9 fields per row (calling `Resolve` once per field, reading from its own
raw-lookup mechanism — column index, JSON property name, or regex group index — plus its typed
`Defaults`), then calls `Build` once. No dictionary walking, no runtime disambiguation, no
enum-of-canonical-fields needed anywhere — every mapping/default target is a compile-time-checked
property.

**Per-plugin options classes live in the plugin's own project, never shared upward into
`Quotinator.Data`** — keeping ADR 004's boundary intact (`Quotinator.Data`/`Quotinator.Core` must not
know about a specific converter's shape, the same reasoning that already keeps
`SystemImportAction.EntityType` free-text):

```csharp
// Quotinator.Converters.Csv
public sealed class CsvConverterOptions
{
    public bool HasHeader { get; init; } = true;
    public IndexedFieldMapping? ColumnMapping { get; init; }
    public QuoteFieldDefaults? Defaults { get; init; }
}

// Quotinator.Converters.BasicJsonArray
public sealed class BasicJsonArrayConverterOptions
{
    public NamedFieldMapping? PropertyMapping { get; init; }
    public QuoteFieldDefaults? Defaults { get; init; }
}

// Quotinator.Converters.RegexArray
public sealed class RegexArrayConverterOptions
{
    public string? Pattern { get; init; } // not `required` — validated explicitly, throws
                                            // SourceConversionException like every other unrecoverable
                                            // input case, not a raw JSON deserialization exception
    public IndexedFieldMapping? GroupMapping { get; init; }
    public QuoteFieldDefaults? Defaults { get; init; }
}
```

**The `IQuoteSourceConverter` interface boundary itself stays opaque — a raw `JsonElement?`, typed only
once inside each plugin.** `ConvertAsync` gains `JsonElement? options = null`, not a concrete options
type. Each plugin immediately does `options?.Deserialize<TheirOwnOptionsType>()`. This is the only way
to keep the interface free of any dependency on a specific plugin's options shape (mirrors why it's
already "free of any dependency on the canonical quote model," per its own XML doc) while still letting
each plugin be fully typed internally — putting typed options properties directly on the shared
`SourceImportSettingsDto` in `Quotinator.Data` would require Data to reference every converter plugin
project, which ADR 004 forbids and would break the plugin model (a new converter could no longer be
added without also changing the shared DTO). Passing a `JsonElement` across the boundary and
deserializing it immediately into a named POCO on the other side satisfies this project's JSON parsing
policy (`JsonSerializer.Deserialize<T>`/`element.Deserialize<T>()` — never manual node walking).
`SourceImportSettingsDto.ConverterOptions` (and therefore `SeedFile.ConverterOptions`) is `JsonElement?`,
not `Dictionary<string,string>?`. `schemas/manifest.schema.json`'s `converterOptions` stays a
loosely-typed `object` — JSON Schema can't cleanly discriminate the shape by a sibling `converter`
string value without `if`/`then` conditionals, more validation machinery than this issue needs; real
type safety lives at the C# layer where it actually catches bugs.

**Internal-only plugin slots (unaffected by this redesign):** decision from the original draft stands
— `IQuoteSourceConverter.IsInternalOnly` (default `false`), enforced against `SeedBatchOrigin.UserImports`.
Applies identically to all three plugins; none of them opts in.

---

## Spec requirements

1. Format 0 (canonical JSON) needs no converter — confirmed, not a silent assumption; no code change.
2. `Quotinator.Converters.Csv` gains `converterOptions`-driven index mapping and default values,
   fully backward compatible: existing header-name auto-matching remains the behaviour when no
   `converterOptions` are supplied at all.
3. New `Quotinator.Converters.BasicJsonArray` (`Name => "basic-json-array"`) — flat JSON object array,
   `converterOptions`-driven source-property mapping and default values.
4. New `Quotinator.Converters.RegexArray` (`Name => "regex-array"`) — JSON string array, a
   manifest-supplied regex `pattern` option plus 1-based capture-group-to-property mapping and default
   values, reusing the same index convention as CSV.
5. `Quotinator.Converters.NikhilNamal17` and `Quotinator.Converters.Vilaboim` (projects, test projects,
   `Program.cs` registrations, `Quotinator.slnx`/`docker/Dockerfile` entries) are deleted; their
   sources become manifest entries against `basic-json-array`/`regex-array` reproducing current
   behaviour exactly (id stability).
6. Shared typed classes and assembly logic live in `Quotinator.Core.Import`
   (`IndexedFieldMapping`, `NamedFieldMapping`, `QuoteFieldDefaults`, `MappedSourceQuoteBuilder`),
   used by all three plugins — no duplicated implementation, and no per-plugin
   `Dictionary<string,string>` options shape.
7. `IQuoteSourceConverter` gains a `JsonElement? options` parameter on `ConvertAsync` and
   `IsInternalOnly` (default `false`), enforced against `SeedBatchOrigin.UserImports`; wired through
   both call sites (`SourceCacheUpdater`, `SqliteQuoteImportService`). Each plugin deserializes
   `options` into its own typed options class (`CsvConverterOptions`, `BasicJsonArrayConverterOptions`,
   `RegexArrayConverterOptions`) immediately, rather than the interface carrying a concrete shape.
8. `schemas/manifest.schema.json` gains `converterOptions` (a loosely-typed `object` — the schema
   cannot discriminate its shape by the sibling `converter` value without `if`/`then` conditionals;
   real type safety is enforced at the C# layer via each plugin's own options class) on a `files[]`
   entry.
9. `data/sources/manifest.json` updated: `NikhilNamal17` entry → `converter: "basic-json-array"`,
   `converterOptions: {"propertyMapping": {"source": "movie", "date": "year"}}`; `Vilaboim` entry →
   `converter: "regex-array"`, `converterOptions` carrying its `pattern` and `groupMapping`.
10. `scripts/SOURCES.md` rewritten to document all three formats, each plugin's typed options class,
    and when a new source needs a brand-new plugin at all (only a genuinely novel raw shape —
    everything covered by "flat CSV," "flat JSON array," or "regex-extractable string array" needs
    only a manifest entry from now on).

## Non-goals

- No alias/synonym support for canonical property names in options classes (e.g. `language` is not
  accepted as an alias for `OriginalLanguage`/`originalLanguage` — the exact property name is required).
- Conversations/translations remain expressible only via canonical JSON (format 0) — none of the three
  flat/array formats gains any conversation-related capability.
- `Quotinator.Converters.Csv`'s existing zero-config, header-name-matching behaviour is preserved
  exactly, not replaced — this issue only adds the option to override it.

---

## Steps

### 1. Shared typed classes: `IndexedFieldMapping`, `NamedFieldMapping`, `QuoteFieldDefaults`, `MappedSourceQuoteBuilder`

**Status:** ✅ Done — `MappedSourceQuoteBuilderTests` (18 tests: `Resolve`'s raw/default coalescing,
`Build`'s quote/source-required contract, id derivation vs. explicit id, `en`/`Movie` fallbacks, genre
pass-through), `IndexedFieldMappingTests`/`QuoteFieldDefaultsTests` (deserialization, unmapped slots
stay `null`). Full suite green (1005 tests) after this step.

New files in `Quotinator.Core.Import` (alongside `QuoteTypeNormalisation.cs`/`YearParsing.cs`), exact
shapes per Scope changes, each property carrying an explicit `[JsonPropertyName]` matching this
project's existing DTO convention (`SourceQuote.cs`, `ManifestFileEntryDto.cs`) rather than relying on
case-insensitive matching. `QuoteFieldDefaults.Type` carries `[JsonConverter(typeof(QuoteTypeJsonConverter))]`
— the same kebab-case wire converter `SourceQuote.Type` already uses. `MappedSourceQuoteBuilder.Resolve`
coalesces a raw value with its configured default (empty/whitespace counts as absent).
`MappedSourceQuoteBuilder.Build` assembles one row's already-resolved 9 field values into a
`SourceQuote?`, returns `null` when `quote`/`source` end up empty, and derives `Id` via
`QuoteIdentity.StableId` when not otherwise supplied. No parsing/disambiguation logic needed here —
each plugin's typed options class is deserialized directly from its `JsonElement`, so mapping and
defaults are already distinct, separately-named properties by the time this code runs.

### 2. Thread `converterOptions` through the shared settings DTOs and schema

**Status:** ✅ Done — `ManifestSeedPlannerTests.PlanSeed_FileWithConverterOptions_PopulatesSeedFileConverterOptions`,
`SourceDataIntegrityTests.Manifest_EntryWithConverterOptions_PassesSchemaValidation`. Only one call
site constructed `SeedFile` positionally past the new parameter (`ManifestSeedPlanner.cs`) — found by
grepping every `new SeedFile(` call site, not assumed safe. Full suite green (1007 tests) after this
step.

`SourceImportSettingsDto.ConverterOptions` (`JsonElement?`, wire name `converterOptions`) inherited by
`ManifestFileEntryDto` and `Quotinator.Api`'s `ImportRequestSettingsDto`; `SeedFile.ConverterOptions`
(also `JsonElement?`); `ManifestSeedPlanner` threads `e.ConverterOptions` through;
`schemas/manifest.schema.json` gains `converterOptions` (loosely-typed `object`) on a `files[]` entry.

### 3. Extend `IQuoteSourceConverter` — options parameter and internal-only opt-in

**Status:** ✅ Done — `CsvQuoteConverterTests.IsInternalOnly_DefaultsToFalse`. **Not fully
source-compatible as first assumed**: `options` had to be inserted before `cancellationToken`
(matching the rest of the codebase's parameter-ordering convention of required-then-optional), which
broke every call site passing `cancellationToken` positionally as the 3rd argument — caught by the
compiler as `CS0535`/type-mismatch errors, not silently. Fixed 5 call sites: `SourceCacheUpdater.cs`,
`SqliteQuoteImportService.cs` (both now pass `cancellationToken:` named, pending Step 4's real options
wiring), and three `IQuoteSourceConverter` implementers whose signatures had to gain the new parameter
too (`CsvQuoteConverter`, plus `NikhilNamal17`/`VilaboimMovieQuotesConverter`, both slated for deletion
in later steps but must compile until then) and two test doubles (`QuoteImportServiceTests
.PassthroughTestConverter`, `SourceCacheUpdaterTests.FakeConverter` — the latter also extended with
`IsInternalOnly`/`LastReceivedOptions` now, ahead of Step 4's tests). A default interface member is
only reachable through the interface type, not the concrete class — `IsInternalOnly_DefaultsToFalse`
had to cast to `IQuoteSourceConverter` explicitly, found via a real `CS1061` compile error, not assumed.
Full suite green (1008 tests) after this step.

`JsonElement? options = null` added to `ConvertAsync`; `bool IsInternalOnly => false;` added as a
default interface member.

### 4. Wire `converterOptions` and internal-only enforcement into both call sites

**Status:** ✅ Done — `SourceCacheUpdaterTests.ResolveAsync_ConverterWithOptions_PassesOptionsToConvertAsync`,
`_InternalOnlyConverterFromUserImportsOrigin_FallsBackLikeUnregistered`,
`_InternalOnlyConverterFromBundledOrigin_Succeeds`, `SqliteQuoteImportServiceTests
.ImportAsync_ConverterWithOptions_PassesOptionsToConvertAsync`. `SqliteQuoteImportService
.LoadQuotesAsync` didn't take a full settings object, only `converterName` — had to add a
`converterOptions` parameter to it and its one call site, found by grepping every `LoadQuotesAsync`
reference rather than assumed. Full suite green (1013 tests) after this step.

`SourceCacheUpdater` threads `SeedBatchOrigin` from `ResolveAsync`'s `batch` down through
`ResolveOneAsync` into `TryDownloadAndPrepareAsync`, enforces `IsInternalOnly` against
`SeedBatchOrigin.UserImports` (fails closed exactly like an unregistered name, with a distinct log
message) before an internal-only converter is ever invoked, and passes `file.ConverterOptions` into
`ConvertAsync`. `SqliteQuoteImportService.LoadQuotesAsync` passes `settings?.ConverterOptions` into
`ConvertAsync`; no `SeedBatchOrigin` concept applies to the manual upload path (`AdminApiKey` auth is
the existing trust boundary there).

### 5. Enhance `Quotinator.Converters.Csv` in place

**Status:** ✅ Done — all 11 pre-existing `CsvQuoteConverterTests` unchanged and still green (proves
zero-config behaviour is byte-for-byte preserved), plus `ConvertAsync_ColumnMapping_MapsColumnsByPosition`,
`_HasHeaderFalse_TreatsFirstRowAsData`, `_Defaults_PopulatesUnmappedField`,
`_ColumnMappingWithRowValue_RowValueTakesPrecedenceOverDefault`. Full suite green (1017 tests) after
this step.

Add `CsvConverterOptions` (Scope changes). When `options` is null or deserializes to a `CsvConverterOptions`
with no `ColumnMapping`: unchanged existing behaviour (header-name auto-match, case-insensitive). When
`ColumnMapping` is present: index mapping is used exclusively for every canonical field it covers
(header text, if present, is not used for mapping purposes on those fields — only `HasHeader` decides
whether row 0 is data or a label, default `true`). Per row, resolves each of the 9 fields via
`MappedSourceQuoteBuilder.Resolve(row[mapping.Quote] etc., defaults?.OriginalLanguage etc.)`, then calls
`MappedSourceQuoteBuilder.Build`. Genres remain semicolon-delimited within one cell, as today (a
`Defaults.Genres` value applies when the mapped/default-cell genres end up empty).

### 6. `Quotinator.Converters.BasicJsonArray` — new project, replaces `NikhilNamal17`

**Status:** ✅ Done — 11 new `BasicJsonArrayConverterTests`, including the ID-stability regression test
against the real committed `NikhilNamal17_popular-movie-quotes.json`. Also updated (found by grepping
every `NikhilNamal17` reference, not assumed complete after just the two obvious projects):
`Program.cs`'s converter registration + `using`, `Quotinator.Api.csproj`'s/`Quotinator.Api.Tests
.csproj`'s `ProjectReference`s, `Quotinator.slnx` (project + CVE-folder entries, plus new CVE folders
created for the new project per `docs/testing-policy.md`'s CVE-folder rule), `docker/Dockerfile`'s
restore-layer `COPY` — folding the NikhilNamal17 half of Step 8's wiring into this step so the build
stays green after every step, rather than leaving it red until Step 8. Most significant find: **the
authoritative integration test wasn't the new unit test at all** — `RepositoryStructureTests
.ConverterPlugins_AgainstRawFixtures_ProduceFilesMatchingBaseline` (`Quotinator.Api.Tests`)
independently runs every converter against its committed raw fixture and asserts the *entire id set*
matches baseline (stricter than my own single-sample-id unit test) — updated its `NikhilNamal17`
case to run `BasicJsonArrayConverter` with the real production `PropertyMapping`. Full suite green
(1025 tests) after this step.

`Name => "basic-json-array"`. Add `BasicJsonArrayConverterOptions` (Scope changes). Deserializes the
raw input as `List<Dictionary<string, JsonElement>>` (a typed `JsonSerializer.Deserialize<T>` target —
not `JsonNode`/`JsonDocument` hand-walking, satisfying this project's JSON parsing policy) so fields
can be looked up by a runtime-configured raw property name from `PropertyMapping`. Per row, resolves
each field via `MappedSourceQuoteBuilder.Resolve` (raw lookup by `PropertyMapping.Quote` etc., falling
back to the canonical name itself when unmapped — the zero-config case) then calls
`MappedSourceQuoteBuilder.Build`. Genres: a JSON array element becomes each genre string; a single JSON
string element becomes one genre; absent means none — no delimiter splitting needed, since JSON
expresses arrays natively unlike CSV. Delete `src/Quotinator.Converters.NikhilNamal17/` and
`tests/Quotinator.Converters.NikhilNamal17.Tests/` in full (including `NikhilNamal17RawEntry.cs`/
`YearJsonConverter.cs` — the latter's tolerant number-or-string parsing becomes a plain private helper
inside `BasicJsonArrayConverter`, since it's not specific to this one source).

New test project `tests/Quotinator.Converters.BasicJsonArray.Tests/`, including the **ID-stability
regression test**: convert the committed NikhilNamal17 raw fixture through `BasicJsonArrayConverter`
with `PropertyMapping = { Source = "movie", Date = "year" }` and assert every resulting id exactly
matches the corresponding id already committed in `data/sources/NikhilNamal17_popular-movie-quotes.json`.

### 7. `Quotinator.Converters.RegexArray` — new project, replaces `Vilaboim`

**Status:** ✅ Done — 9 new `RegexArrayConverterTests`, including the ID-stability regression test
against the real committed `vilaboim_movie-quotes.json`, plus `ConvertAsync_NoGroupMapping_ThrowsSourceConversionException`
(not originally listed in the plan — added once implementation surfaced that a `Pattern` with no
`GroupMapping` is equally unusable and deserved the same explicit, named exception as a missing
`Pattern`, rather than silently degrading to "zero valid entries"). `RepositoryStructureTests
.ConverterPlugins_AgainstRawFixtures_ProduceFilesMatchingBaseline`'s Vilaboim case updated the same
way as NikhilNamal17's in step 6. Program.cs/csproj/slnx/Dockerfile wiring for this swap folded in
here too, same reasoning as step 6 — see step 8, now just the final confirmation that both swaps are
complete. Full suite green (1028 tests) after this step.

`Name => "regex-array"`. Add `RegexArrayConverterOptions` (Scope changes). Throws
`SourceConversionException` when `Pattern` is null/empty — a regex-array entry with no pattern can
convert nothing, and this must surface as the same exception type every other unrecoverable-input case
uses, not a raw deserialization failure. Applies the pattern to each raw string entry; a non-matching
entry is skipped, not an error, unless *zero* entries match (existing "converted nothing at all"
failure contract). Per matched entry, resolves each field via `MappedSourceQuoteBuilder.Resolve` (raw
lookup by `GroupMapping.Quote` etc. → `match.Groups[index].Value`) then calls
`MappedSourceQuoteBuilder.Build`. Delete `src/Quotinator.Converters.Vilaboim/` and
`tests/Quotinator.Converters.Vilaboim.Tests/` in full.

New test project `tests/Quotinator.Converters.RegexArray.Tests/`, including the **ID-stability
regression test**: convert the committed Vilaboim raw fixture through `RegexArrayConverter` with
`Pattern = "^\"(.+?)\"\\s+(.+)$"`, `GroupMapping = { Quote = 1, Source = 2 }` and assert every
resulting id exactly matches the corresponding id already committed in
`data/sources/vilaboim_movie-quotes.json`.

### 8. Update wiring: `Program.cs`, `Quotinator.slnx`, `docker/Dockerfile`

**Status:** ✅ Done — folded into steps 6 and 7 (each converter swap's wiring done immediately
alongside its deletion, not deferred to a separate step) so the build stayed green after every step
rather than going red between "delete old project" and "fix references." Confirmed complete: `Program.cs`'s
`quoteSourceConverters` registers exactly `RegexArrayConverter`/`BasicJsonArrayConverter`/
`CsvQuoteConverter`; `Quotinator.Api.csproj`/`Quotinator.Api.Tests.csproj` reference the two new
projects, not the two deleted ones; `Quotinator.slnx` lists the four new projects (two main, two test)
plus their CVE folders, with no stale `NikhilNamal17`/`Vilaboim` entries anywhere; `docker/Dockerfile`'s
restore-layer `COPY` block references the two new project paths.

### 9. Update `data/sources/manifest.json` and `scripts/SOURCES.md`

**Status:** ✅ Done — `SourceDataIntegrityTests.Manifest_ConformsToSchema` re-passes against the
updated file. `scripts/SOURCES.md` rewritten: documents all three generic converters with real
worked examples (the same `converterOptions` now actually configured for NikhilNamal17/Vilaboim in
the manifest), and reframes "adding a new source" around configuring an existing converter first —
a new plugin project is now the exception, not the default path.

`data/sources/manifest.json`: NikhilNamal17 entry's `converter`/`converterOptions` per Scope changes;
Vilaboim entry's `converter`/`converterOptions` per Scope changes. Neither entry's `file`/`name`/
`github` provenance fields change. `scripts/SOURCES.md`: rewritten converter-plugin section covering
all three formats, each plugin's typed options class (`CsvConverterOptions`,
`BasicJsonArrayConverterOptions`, `RegexArrayConverterOptions`), and updated guidance that a new
source needs a new plugin project only when its raw shape is neither flat-CSV, flat-JSON-array, nor a
regex-extractable string array.

### 10. Full regression pass and live verification

**Status:** ✅ Done. Full solution build: 0 warnings, 0 errors. Full test suite: 1028 tests, 0
failures. T1 (`dotnet run --configuration Release`, real network access,
`POST /api/v1/admin/sources/refresh?force=true`): both sources returned `"outcome":"updated"` with no
warnings in the log; the freshly re-converted cache files were diffed id-for-id against the
already-committed `data/sources/*.json` baselines — **732/732 NikhilNamal17 ids, 99/99 Vilaboim ids,
zero differences either direction**. T2 (Docker): fresh `docker build` succeeded (both new converter
projects present in the restore/publish layers); a fresh container's forced refresh produced the
identical result — same `"outcome":"updated"` for both, zero id differences against baseline, no
warnings/errors in container logs. This also serves as row 24's live verification: both real sources,
now configured as `basic-json-array`/`regex-array` in the manifest, converted cleanly with no "not
registered" warning — proving the swapped registration is correct — while the pre-existing, unaffected
`SourceCacheUpdaterTests.ResolveAsync_UnregisteredConverterName_FailsClosedAndLogsWarning` still covers
the rejection path for a genuinely unknown name.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `MappedSourceQuoteBuilder.Resolve` returns the raw value when non-empty, else the default | Unit test | `MappedSourceQuoteBuilderTests.Resolve_RawValuePresent_ReturnsRawValue`, `_RawValueEmpty_ReturnsDefault`, `_RawValueNull_ReturnsDefault`, `_BothNull_ReturnsNull` |
| 2 | ✅ | `MappedSourceQuoteBuilder.Build` returns `null` when `quote` or `source` end up empty | Unit test | `MappedSourceQuoteBuilderTests.Build_QuoteEmpty_ReturnsNull`, `_SourceEmpty_ReturnsNull` |
| 3 | ✅ | `MappedSourceQuoteBuilder.Build` derives `Id` via `QuoteIdentity.StableId` when not supplied, honours an explicit id when supplied | Unit test | `MappedSourceQuoteBuilderTests.Build_NoIdSupplied_DerivesStableId`, `_ExplicitIdSupplied_TakesPrecedence` |
| 4 | ✅ | `MappedSourceQuoteBuilder.Build` applies `originalLanguage="en"`/`type=Movie` fallbacks when neither a value nor a default is supplied | Unit test | `MappedSourceQuoteBuilderTests.Build_NoOriginalLanguageOrDefault_FallsBackToEn`, `_NoTypeOrDefault_FallsBackToMovie` |
| 5 | ✅ | `IndexedFieldMapping`/`NamedFieldMapping`/`QuoteFieldDefaults` deserialize correctly from JSON, with every unmapped slot left `null` | Unit test | `IndexedFieldMappingTests.Deserialize_PartialMapping_UnmappedSlotsAreNull`, `QuoteFieldDefaultsTests.Deserialize_PartialDefaults_UnsetSlotsAreNull` (`Quotinator.Core.Tests`, paired with `Quotinator.Core.Import`) |
| 6 | ✅ | `SourceImportSettingsDto`/`SeedFile` carry `ConverterOptions` (`JsonElement?`) through to a `SeedFile` built from a manifest entry | Unit test | `ManifestSeedPlannerTests.PlanSeed_FileWithConverterOptions_PopulatesSeedFileConverterOptions` |
| 7 | ✅ | `schemas/manifest.schema.json` accepts a `files[]` entry with `converterOptions` | Unit test | `SourceDataIntegrityTests.Manifest_EntryWithConverterOptions_PassesSchemaValidation` |
| 8 | ✅ | `IQuoteSourceConverter.ConvertAsync` accepts a `JsonElement? options` parameter; default `IsInternalOnly` is `false` for an implementation that doesn't override it | Unit test | `CsvQuoteConverterTests.IsInternalOnly_DefaultsToFalse` |
| 9 | ✅ | `SourceCacheUpdater` passes a `SeedFile`'s `ConverterOptions` into `ConvertAsync`; refuses an internal-only converter from a `UserImports`-origin batch; allows one from a `Bundled`-origin batch | Unit test | `SourceCacheUpdaterTests.ResolveAsync_ConverterWithOptions_PassesOptionsToConvertAsync`, `_InternalOnlyConverterFromUserImportsOrigin_FallsBackLikeUnregistered`, `_InternalOnlyConverterFromBundledOrigin_Succeeds` |
| 10 | ✅ | `SqliteQuoteImportService`'s manual import path passes `settings.ConverterOptions` into `ConvertAsync` | Unit test | `SqliteQuoteImportServiceTests.ImportAsync_ConverterWithOptions_PassesOptionsToConvertAsync` |
| 11 | ✅ | `CsvQuoteConverter` with no `converterOptions` (or `ColumnMapping` absent) behaves exactly as before (regression) | Unit test | Existing `CsvQuoteConverterTests` suite (11 tests), unmodified, still green |
| 12 | ✅ | `CsvQuoteConverter` with `CsvConverterOptions.ColumnMapping` set maps columns by position, ignoring header text for those fields | Unit test | `CsvQuoteConverterTests.ConvertAsync_ColumnMapping_MapsColumnsByPosition` |
| 13 | ✅ | `CsvQuoteConverter` with `HasHeader = false` treats row 0 as data | Unit test | `CsvQuoteConverterTests.ConvertAsync_HasHeaderFalse_TreatsFirstRowAsData` |
| 14 | ✅ | `CsvQuoteConverter` with `Defaults` set populates a canonical field sourced from no column; a row value always takes precedence over a default | Unit test | `CsvQuoteConverterTests.ConvertAsync_Defaults_PopulatesUnmappedField`, `_ColumnMappingWithRowValue_RowValueTakesPrecedenceOverDefault` |
| 15 | ✅ | `BasicJsonArrayConverter` needs no options when raw property names already match canonical names | Unit test | `BasicJsonArrayConverterTests.ConvertAsync_CanonicalPropertyNames_NoOptionsNeeded` |
| 16 | ✅ | `BasicJsonArrayConverter` deserializes `BasicJsonArrayConverterOptions.PropertyMapping` and remaps a non-canonical raw name | Unit test | `BasicJsonArrayConverterTests.ConvertAsync_PropertyMapping_RemapsField` |
| 17 | ✅ | `BasicJsonArrayConverter` genres: JSON array, single string, and absent all resolve correctly | Unit test | `BasicJsonArrayConverterTests.ConvertAsync_GenresAsArray_ProducesMultipleGenres`, `_GenresAsSingleString_ProducesOneGenre`, `_GenresAbsent_ProducesEmptyList` |
| 18 | ✅ | `BasicJsonArrayConverter` skips a row missing quote/source; throws on invalid JSON or zero converted entries | Unit test | `BasicJsonArrayConverterTests.ConvertAsync_RowMissingQuoteOrSource_SkipsRow`, `_InvalidJson_ThrowsSourceConversionException`, `_ZeroValidEntries_ThrowsSourceConversionException` |
| 19 | ✅ | **ID stability**: `BasicJsonArrayConverter` reproduces every id already committed in `NikhilNamal17_popular-movie-quotes.json` from the same raw fixture | Unit test | `BasicJsonArrayConverterTests.ConvertAsync_AgainstCommittedNikhilNamal17Fixture_IdsMatchExactly` (single sample id) + `RepositoryStructureTests.ConverterPlugins_AgainstRawFixtures_ProduceFilesMatchingBaseline` (`Quotinator.Api.Tests` — stricter: asserts the entire id set matches, not just one sample) |
| 20 | ✅ | `RegexArrayConverter` deserializes `RegexArrayConverterOptions`, applies `Pattern`, and maps capture groups by `GroupMapping`'s 1-based index | Unit test | `RegexArrayConverterTests.ConvertAsync_PatternAndGroupMapping_ProducesExpectedQuotes` |
| 21 | ✅ | `RegexArrayConverter` throws `SourceConversionException` when `Pattern` is null/empty, or when `GroupMapping` is absent | Unit test | `RegexArrayConverterTests.ConvertAsync_NoPattern_ThrowsSourceConversionException`, `_NoGroupMapping_ThrowsSourceConversionException` |
| 22 | ✅ | `RegexArrayConverter` skips non-matching entries; throws on invalid JSON or zero converted entries | Unit test | `RegexArrayConverterTests.ConvertAsync_NonMatchingEntry_SkipsIt`, `_InvalidJson_ThrowsSourceConversionException`, `_ZeroValidEntries_ThrowsSourceConversionException` |
| 23 | ✅ | **ID stability**: `RegexArrayConverter` reproduces every id already committed in `vilaboim_movie-quotes.json` | Unit test | `RegexArrayConverterTests.ConvertAsync_AgainstCommittedVilaboimFixture_IdsMatchExactly` (single sample id) + `RepositoryStructureTests.ConverterPlugins_AgainstRawFixtures_ProduceFilesMatchingBaseline` (`Quotinator.Api.Tests` — entire id set) |
| 24 | ✅ | `Program.cs` registers exactly `csv`, `basic-json-array`, `regex-array` — no lingering `nikhilnamal17`/`vilaboim` registration | Live | No clean unit-testable seam exists — `quoteSourceConverters` is a closure-local `Dictionary` baked into `SourceCacheOptions` inside a DI factory, not itself exposed via DI (adding a seam purely for this one assertion would be testability-driven over-engineering). Verified at T1 and T2 (step 10): `POST /api/v1/admin/sources/refresh?force=true` produced no "not registered" warning for either real source (now configured as `basic-json-array`/`regex-array`) in either environment, with an exact id-set match against baseline proving the correct converter actually ran, not just that something didn't warn. The rejection path for a genuinely unknown name is separately covered by the pre-existing, unaffected `SourceCacheUpdaterTests.ResolveAsync_UnregisteredConverterName_FailsClosedAndLogsWarning` |
| 25 | ✅ | `data/sources/manifest.json` deserializes with the updated `converter`/`converterOptions` values and still passes schema validation | Unit test | `SourceDataIntegrityTests.Manifest_ConformsToSchema` |
| 26 | ✅ | `Quotinator.slnx` lists the four new projects (two main, two test), no stale references to the deleted two | Live | `Quotinator.slnx` manually re-read end-to-end — both new plugins, their test projects, and their CVE folders present; `grep` for `NikhilNamal17`/`Vilaboim` across the repo returns only the untouched `data/sources/NikhilNamal17_popular-movie-quotes.json` filename (deliberately unrenamed, Scope changes) and doc/changelog prose; `dotnet build --configuration Release` succeeds at 0 warnings/errors, which VS's own build uses the same MSBuild engine to reproduce |
| 27 | ✅ | `scripts/SOURCES.md` documents all three formats and each plugin's typed options class | Live | Manual read-through of the rewritten `scripts/SOURCES.md` — all three converters documented with real, now-actually-configured examples |
| 28 | ✅ | T1 — live re-conversion of both sources via `force=true` refresh produces content matching the currently-committed files | Live | `dotnet run --configuration Release` + `POST /api/v1/admin/sources/refresh?force=true` against real upstream URLs → both `"outcome":"updated"`, no warnings; id-set diff against `data/sources/*.json`: 732/732 NikhilNamal17, 99/99 Vilaboim, zero differences |
| 29 | ✅ | T2 — same re-conversion succeeds inside a fresh Docker container | Live | `docker build -f docker/Dockerfile` succeeded; fresh container's forced refresh → both `"outcome":"updated"`, zero id differences against baseline (same counts as T1), no warnings/errors in container logs |
