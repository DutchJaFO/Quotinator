# #206 — Merge Quotinator.Engine into Quotinator.Core

**Status:** Waiting for release
**GitHub issue:** #206
**Tiers required:** T1, T2
**Depends on:** nothing

---

## Background — why this issue exists

ADR 004 was revised for issue #121 to split `Quotinator.Engine` out of `Quotinator.Core`, so that
`Quotinator.Data` could stay domain-agnostic while something still held Quotinator-domain entities and
SQL needing to see both Core's domain types and Data's Dapper/SQLite infrastructure. That reasoning is
sound and this issue does not touch it — `Quotinator.Data` stays exactly as it is, domain-agnostic, no
Core reference.

The separate half of that decision — that `Quotinator.Core` itself must stay Dapper/SQLite-free, forcing
domain-implementation code into a third project rather than letting Core depend on Data directly — is
what this issue undoes. Found while planning #192: `QuoteResponse` (Core) needed the same
`MasterDataReference` shape #184 defined in `Quotinator.Api.Models`, but every masterdata response DTO
(`SourceResponse`, `CharacterResponse`, etc.) also needs `Quotinator.Data.Entities.CompletenessStatus` —
a type neither Core nor Engine alone could see together with a Core-owned reference type, forcing those
DTOs into `Quotinator.Api.Models` instead of alongside `QuoteResponse`. That's structural, not
incidental: it recurs for every future Core-shaped type that also needs a Data-layer concept, because
Core and Engine are artificially split while Api sits above both.

**Verified before starting** (per this milestone's standing rule):

- **`Quotinator.Core.csproj` has zero `<ProjectReference>` entries; `Quotinator.Engine.csproj` references
  both `Quotinator.Core.csproj` and `Quotinator.Data.csproj`, plus direct package references to
  `Dapper`, `Dapper.Contrib`, and `Microsoft.Data.Sqlite`** — confirmed by reading both `.csproj` files
  directly. Engine does not merely consume Data's infrastructure at arm's length; it has its own direct
  ORM package references to write and execute Quotinator-domain SQL.
- **The boundary has already blurred in practice, independent of #192**: `Quotinator.Core.Tests.csproj`
  already has a direct `ProjectReference` to `Quotinator.Engine.csproj`. `tests/Quotinator.Core.Tests/
  Data/` already contains `QuotinatorMigrationsTests.cs` and `SqliteQuoteServiceConversationTests.cs`/
  `SqliteQuoteServiceSearchTests.cs` — tests for `Quotinator.Engine.Database.QuotinatorMigrations` and
  `Quotinator.Engine.Services.SqliteQuoteService`, filed under a folder name (`Data/`) matching neither
  Engine namespace (`Database`/`Services`).
- **No other project consumes Core without also consuming Engine.** `Quotinator.Api.csproj` references
  both together. Three converter plugins (`Quotinator.Converters.RegexArray/Csv/BasicJsonArray`) and
  their test projects reference only Core — they need `SourceQuote`/`IQuoteSourceConverter`-shaped types
  and will gain Dapper/Microsoft.Data.Sqlite as an unused transitive package reference after this merge.
  Accepted cost, not a functional problem — consistent with this project's Simplicity priority over
  minimal per-project dependency footprints.
- **No filename collisions exist between Core's and Engine's `Models/`/`Services/`/`Helpers/` folders** —
  confirmed by listing both directly. Engine's `Entities/`, `Database/`, `Queries/`, `Repositories/`
  folders have no Core equivalent at all and move wholesale.
- **`Quotinator.Core.Services.QuoteService` (an in-memory `IQuoteService` implementation, dating to v1's
  flat-file-JSON era) is dead code** — confirmed via `Program.cs:394`, which registers
  `Quotinator.Engine.Services.SqliteQuoteService` for `IQuoteService`, not the Core one. Out of scope for
  this issue (a namespace/project merge, not a dead-code sweep) — noted in this plan's Notes section as a
  separate follow-up, not silently deleted here.
- **11 files in `Quotinator.Api` and 39 files across `tests/`** reference a `Quotinator.Engine.*`
  namespace today — confirmed via direct search, full lists in Steps 4 and 6 below.

---

## Steps

### 1. Merge `src/Quotinator.Engine/` into `src/Quotinator.Core/`

**Status:** ✅ Done. Executed via a one-time `dotnet-script` migration script (`.claude/temp/merge-core-engine.csx`,
per ADR 010's sanctioned scripting escape valve — not committed, discarded after the run) rather than
manual file-by-file moves, given the volume. Folder mapping matched the table below exactly; zero
collisions, confirmed empirically after the move (build succeeded on first attempt for this step).

Folder/namespace mapping (verified no collisions):

| Engine folder | Files | Destination | New namespace |
|---|---|---|---|
| `Entities/` | 17 | `src/Quotinator.Core/Entities/` (new) | `Quotinator.Core.Entities` |
| `Database/` | 5 | `src/Quotinator.Core/Database/` (new) | `Quotinator.Core.Database` |
| `Queries/` | 1 (`Sql.cs`) | `src/Quotinator.Core/Queries/` (new) | `Quotinator.Core.Queries` |
| `Repositories/` | 8 | `src/Quotinator.Core/Repositories/` (new) | `Quotinator.Core.Repositories` |
| `Helpers/` | 2 | `src/Quotinator.Core/Helpers/` (existing — has `InputValidation.cs`) | `Quotinator.Core.Helpers` |
| `Models/` | 3 | `src/Quotinator.Core/Models/` (existing) | `Quotinator.Core.Models` |
| `Services/` | 8 | `src/Quotinator.Core/Services/` (existing — has `ApiLocalizer.cs`, `VersionService.cs`, `IQuoteService.cs`, `QuoteService.cs`) | `Quotinator.Core.Services` |

Each file's `namespace` declaration changes from `Quotinator.Engine.X` to `Quotinator.Core.X`; no other
content changes. Internal `using Quotinator.Engine.Y;` statements between these files (e.g.
`SqliteQuoteService.cs` referencing `Quotinator.Engine.Queries`) become same-namespace-tree references
and are removed or updated to `Quotinator.Core.Y` as needed.

### 2. Retire `Quotinator.Engine.csproj`; update `Quotinator.Core.csproj`

**Status:** ✅ Done. One gap found beyond this step's original scope: `Quotinator.Data.csproj`'s own
`InternalsVisibleTo` list had entries for `Quotinator.Engine` and `Quotinator.Engine.Tests` (not
anticipated when this plan was written) — updated to `Quotinator.Core`/`Quotinator.Core.Tests`, since
without this fix any `internal` Data member Engine's moved code used would have silently stopped being
visible.

Delete `src/Quotinator.Engine/Quotinator.Engine.csproj` and the now-empty `src/Quotinator.Engine/`
folder. Update `src/Quotinator.Core/Quotinator.Core.csproj`:
- Add package references: `Dapper` (2.1.79), `Dapper.Contrib` (2.0.78), `Microsoft.Data.Sqlite` (10.0.9)
  — versions matching Engine's current `.csproj` exactly, not re-pinned.
- Add `<ProjectReference Include="..\Quotinator.Data\Quotinator.Data.csproj" />`.
- `InternalsVisibleTo` stays `Quotinator.Core.Tests` only (Engine's own `InternalsVisibleTo` entries were
  `Quotinator.Engine.Tests` and `Quotinator.Core.Tests` — the former is retired in Step 3, the latter
  already exists on Core's `.csproj`).
- Update the `<Description>` element to describe the merged scope (domain models, interfaces, and the
  SQLite-backed implementation).

### 3. Merge `tests/Quotinator.Engine.Tests/` into `tests/Quotinator.Core.Tests/`

**Status:** ✅ Done, including the `Data/` → `Database/`/`Services/` relocation for the 3 pre-existing
misfiled tests. `MSTestSettings.cs` equivalence confirmed by direct comparison before discarding Engine's
copy (both were byte-identical apart from their namespace declaration; MSTest allows only one
`[AssemblyInitialize]` per assembly, so keeping both would have broken the merged project). Two identifiers
inside `DatabaseInitializerTests.cs` still carried "Engine" in their own name (not the namespace the
migration script's text-replace targeted) — `EngineDomainTables` and
`Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema` — renamed to `ConsumerDomainTables` and
`...ProduceIdenticalConsumerSchema` for accuracy, matching CLAUDE.md's own generic "consumer" terminology.

Same renamespacing rule as Step 1. Folder mapping:

| Engine.Tests folder | Destination | Notes |
|---|---|---|
| `Database/` (4 files) | `Quotinator.Core.Tests/Database/` (new) | |
| `Repositories/` (2 files) | `Quotinator.Core.Tests/Repositories/` (new) | |
| `Security/` (`SqlQueryGuardTests.cs`) | `Quotinator.Core.Tests/Security/` (existing — has `SqlSourceScanTests.cs`) | |
| `Services/` (4 files) | `Quotinator.Core.Tests/Services/` (existing — has `ApiLocalizerTests.cs`, `QuoteServiceTests.cs`) | |
| `MSTestSettings.cs` | discard | `Quotinator.Core.Tests` already has its own; confirm the two are equivalent boilerplate before discarding Engine's copy, don't assume |

While merging, fix the pre-existing `Data/` vs `Database/` mismatch found during this issue's own
investigation: `Quotinator.Core.Tests/Data/QuotinatorMigrationsTests.cs`,
`SqliteQuoteServiceConversationTests.cs`, and `SqliteQuoteServiceSearchTests.cs` move into
`Quotinator.Core.Tests/Database/` and `Quotinator.Core.Tests/Services/` respectively — matching the
namespace of the type each test actually exercises (`QuotinatorMigrations` → `Database`;
`SqliteQuoteService` → `Services`), not the folder they happened to be filed under. The `Data/` folder is
removed once empty.

Delete `tests/Quotinator.Engine.Tests/Quotinator.Engine.Tests.csproj` and the emptied folder.

### 4. Update `Quotinator.Api` — drop the Engine reference, renamespace 11 files

**Status:** ✅ Done. Renamespacing left several files with a duplicate `using` line (files that had
separately imported both a `Quotinator.Engine.X` and a `Quotinator.Core.X` before the merge collapsed to
the same line twice) — 11 `CS0105` warnings across `Quotinator.Core`, `Quotinator.Api`, and their test
projects, fixed by removing each duplicate.

Remove the `Quotinator.Engine` `<ProjectReference>` from `Quotinator.Api.csproj` (the `Quotinator.Core`
reference already covers everything). Update `using Quotinator.Engine.X;` → `using Quotinator.Core.X;`
in:

`Program.cs` (5 using lines: `Database`, `Entities`, `Helpers`, `Repositories`, `Services`),
`OpenApi/EnumParameterSchemaTransformer.cs`, `Endpoints/ConversationEndpoints.cs`,
`Endpoints/PersonEndpoints.cs`, `Endpoints/ImportEndpoints.cs`, `Endpoints/CharacterEndpoints.cs`,
`Endpoints/UniverseEndpoints.cs`, `Endpoints/StageDirectionEndpoints.cs`,
`Endpoints/SeriesEndpoints.cs`, `Endpoints/SourceEndpoints.cs`, `Endpoints/SoundCueEndpoints.cs`.

### 5. Move `MasterDataReference` and every masterdata response DTO to `Quotinator.Core.Models`

**Status:** ✅ Done. `Quotinator.Api.Models/` ended up completely empty (all 9 files moved out) and was
removed. `ICharacterSourceLinkReader.cs`'s doc comment rewritten properly rather than left as a
text-replaced but now-nonsensical statement — it previously explained the tuple-not-DTO choice as a
project-boundary constraint; now explains it as a deliberate data-shape/response-DTO separation, since
both types are reachable from the same project after the merge.

This is the change #192 actually needed, now unblocked by Steps 1–4: `Quotinator.Core` can reference
`Quotinator.Data.Entities.CompletenessStatus` once Core itself depends on Data (Step 2). Move, with
namespace updates only (no content changes):
- `src/Quotinator.Api/Models/MasterDataReference.cs` → `src/Quotinator.Core/Models/MasterDataReference.cs`
- `src/Quotinator.Api/Models/SourceResponse.cs` → `src/Quotinator.Core/Models/SourceResponse.cs`
- `src/Quotinator.Api/Models/CharacterResponse.cs` → `src/Quotinator.Core/Models/CharacterResponse.cs`
- `src/Quotinator.Api/Models/PersonResponse.cs` → `src/Quotinator.Core/Models/PersonResponse.cs`
- `src/Quotinator.Api/Models/SeriesResponse.cs` → `src/Quotinator.Core/Models/SeriesResponse.cs`
- `src/Quotinator.Api/Models/UniverseResponse.cs` → `src/Quotinator.Core/Models/UniverseResponse.cs`
- `src/Quotinator.Api/Models/StageDirectionResponse.cs` → `src/Quotinator.Core/Models/StageDirectionResponse.cs`
- `src/Quotinator.Api/Models/SoundCueResponse.cs` → `src/Quotinator.Core/Models/SoundCueResponse.cs`
- `src/Quotinator.Api/Models/ConversationSummaryResponse.cs` → `src/Quotinator.Core/Models/ConversationSummaryResponse.cs`

Update the 9 corresponding `Endpoints.cs` files' `using Quotinator.Api.Models;` → `using
Quotinator.Core.Models;` (some already have this using for other Core types and need no change; confirm
per file rather than assume). `Quotinator.Api.Models` may end up empty or near-empty after this — check
whether any file still needs to live there before removing the folder outright.

Update `ICharacterSourceLinkReader.cs`'s doc comment ("Returns plain (Id, Name) tuples, not
Quotinator.Api.Models.MasterDataReference directly") — the type it's contrasting against no longer lives
in Api.Models; reword or remove the now-stale cross-reference.

### 6. Update test files — 39 files referencing `Quotinator.Engine`

**Status:** ✅ Done.

`Quotinator.Core.Tests` and `Quotinator.Engine.Tests` files are covered by Step 3's move. The remaining
category is `Quotinator.Api.Tests` — 23 files (`Endpoints/*.cs` test files and `Fakes/*.cs`) referencing
`Quotinator.Engine.Entities`/`Repositories`/`Models`/`Services` — each gets the same
`using Quotinator.Engine.X;` → `using Quotinator.Core.X;` mechanical update. No test logic changes.

### 7. Update `Quotinator.slnx`

**Status:** ✅ Done. Also found and fixed a gap this plan's original Step 1/3 scope didn't anticipate:
each project's `CVE/` tracking folder (per-project CVE-2025-6965 dismissal record, separate from
`src/`/`tests/` source code) was not covered by the migration script's folder-mapping table at all.
`src/Quotinator.Engine/CVE/CVE-2025-6965.md` (Dependabot alert #5) and
`tests/Quotinator.Engine.Tests/CVE/CVE-2025-6965.md` (alert #7) both had genuinely distinct historical
dismissal records from Core's own (alert #1) — not duplicates to discard. Resolved by moving
Engine.Tests' file into `Quotinator.Core.Tests/CVE/` (which had only a placeholder `.gitkeep`, no prior
file) and appending an update-history row to Core's own `CVE-2025-6965.md` documenting alert #5's
retirement, rather than losing either record. `docs/security/README.md`'s summary table and this file's
own `/CVE/Quotinator.Engine/`, `/CVE/Quotinator.Engine.Tests/`, and `/CVE/Quotinator.Core.Tests/` folder
entries updated to match.

Remove the `Quotinator.Engine` and `Quotinator.Engine.Tests` project entries. Confirm the merged files
under `Quotinator.Core`/`Quotinator.Core.Tests` don't need explicit `<Folder>` entries — per the File
placement rule, files inside a project's own folder structure are visible via the project node and don't
need one; only files living outside any project need an explicit `<Folder>`.

### 8. Update ADR 004

**Status:** ✅ Done. Also corrected the header's `**Status:**` line, which referenced a non-existent
"ADR 004-A (Engine layer)" file — the Engine revision was always inline within this same document, not a
separate ADR. Updated to reference this issue's own revision section instead.

Add a new `## Revision — issue #206 merged Quotinator.Engine back into Quotinator.Core` section:
record that the three-project split's second half (Core must stay Dapper/SQLite-free) is retired,
`Quotinator.Data` stays domain-agnostic and unaffected, and the project graph returns to two Quotinator
layers (`Quotinator.Data` infrastructure; `Quotinator.Core` everything domain-specific including its own
SQLite implementation) under `Quotinator.Api`. Do not edit the existing 2026-06-25/#121/#157/#158
sections' text — ADRs are append-only history per this project's own doc-update convention; a merge is a
new revision section, not a rewrite of the old ones.

### 9. Update `CLAUDE.md`

**Status:** ✅ Done, plus two files this plan's original scope missed entirely: `README.md`'s own project
structure tree (a near-duplicate of CLAUDE.md's, not previously cross-referenced in this plan) had the
same stale `Quotinator.Engine`/`Quotinator.Engine.Tests` bullets and was fixed identically; and
`docker/Dockerfile` had a hardcoded `COPY src/Quotinator.Engine/Quotinator.Engine.csproj ...` restore-cache
layer line, which broke the Docker build entirely (`file not found`) until removed — found live during
Step 10's T2 pass, not anticipated by this plan's Step 9 text, which only covered doc/markdown files.

- "Project structure": remove the `Quotinator.Engine/` bullet; update `Quotinator.Core/`'s description
  to "Domain models, interfaces, and the SQLite-backed service implementation for Quotinator — bridges
  domain contracts with `Quotinator.Data`'s generic infrastructure."; update the "Dependency direction"
  line to `Quotinator.Api` → `Quotinator.Core`; `Quotinator.Core` → `Quotinator.Data`; `Quotinator.Api`
  → `Quotinator.Constants`.
- "Masterdata reference shape": remove the Core/Api-split language — `MasterDataReference` and every
  masterdata response DTO now live in one place, `Quotinator.Core.Models`, resolving the split this
  section previously had to explain around.
- Any other literal `Quotinator.Engine` path/namespace references elsewhere in the file (e.g. "Endpoint
  test pattern", "SQL injection policy" examples) — grep and update rather than assume there are none.

### 10. Verify

**Status:** ✅ Done. `dotnet build --configuration Release` → 0 warnings, 0 errors (after fixing the 11
`CS0105` duplicate-using warnings from Step 4 and 3 `CS1574` doc-comment `cref` resolution warnings in
`DatabaseInitializerTests.cs` — the partially-qualified `<see cref="Data.Helpers.SafeEnumHandler{TEnum}"/>`
style crefs stopped resolving once the file's enclosing namespace changed from
`Quotinator.Engine.Tests.Database` to `Quotinator.Core.Tests.Database`; fixed by fully qualifying them
rather than relying on namespace-ancestor resolution). `dotnet test --configuration Release
--verbosity normal` → full solution green across every test project (`Quotinator.Core.Tests` 560,
`Quotinator.Api.Tests` 487, `Quotinator.Data.Tests` 389, plus the smaller converter/tool test projects),
0 warnings, 0 errors — every test from both former projects passes unmodified, confirming the merge
changed no behaviour.
`SqlQueryGuardTests` and the renamed schema-drift test
(`Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema`) pass.

T2 (Docker): `docker build` failed on the first attempt with the Dockerfile gap noted under Step 9; after
that fix, image built and container started cleanly (health, version, random, search all `200`; seeding
completed — 796 quotes, 479 sources). Verified the merged DI graph end-to-end across every category the
merge touched: `masterdata/sources`, `masterdata/characters` (confirmed `sources: [{id,name}]` populated
via the moved `ICharacterSourceLinkReader`), `conversations`, `import/actions`, `admin/audit`, and the
OpenAPI spec endpoint — all `200`, no errors in container logs. `masterdata/series`/`universes` correctly
returned empty (#180's overlay file seeds `Pending` under review policy by design — not a regression).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Every `Quotinator.Engine` namespace/file is gone from the solution | Unit test | `dotnet build --configuration Release` — 0 warnings, 0 errors; grep for `Quotinator.Engine` returns only deliberate historical mentions (ADR 004, CLAUDE.md, CHANGELOG.md, CVE update-history rows) |
| 2 | ✅ | `MasterDataReference` and all 8 masterdata response DTOs live in `Quotinator.Core.Models`; no duplicate type exists | Unit test | `dotnet build --configuration Release`; `src/Quotinator.Api/Models/` removed (empty) |
| 3 | ✅ | Full merged test suite passes unmodified — no behaviour change | Unit test | `dotnet test --configuration Release --verbosity normal` — all tests green, 0 warnings, 0 errors |
| 4 | ✅ | `SqlQueryGuardTests` and schema-drift tests specifically pass | Unit test | `Quotinator.Core.Tests.Security.SqlQueryGuardTests`, `Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema`/`...AcceptSameCheckConstraintValues` |
| 5 | ✅ | `Quotinator.slnx`, `Quotinator.Api.csproj`, ADR 004, and CLAUDE.md all updated (plus README.md, docker/Dockerfile, Quotinator.Data.csproj, docs/security/README.md — found during implementation, see Steps 2/7/9) | Doc/solution review | Files updated |
| 6 | ✅ | T1 — app starts in Visual Studio | Live (T1) | Developer confirmed 2026-07-19 — clean startup (schema v9/data v10 recognized, no migration replay needed), masterdata/conversations/admin endpoints all 200, pagination validation (422 on malformed/beyond-last page) working, admin reseed round-trip completed and re-staged the Series/Universe overlay correctly |
| 7 | ✅ | T2 — full smoke matrix against the built image | Live (T2) | `docker build`/`docker run` — health/version/random/search plus masterdata/conversations/import/audit/OpenAPI all 200, no container errors |

---

## Notes

`Quotinator.Core.Services.QuoteService` (dead in-memory `IQuoteService` implementation from v1) is found
but explicitly out of scope here — this issue merges namespaces, it doesn't sweep dead code. Worth its
own small follow-up issue once this lands, since after the merge it will sit directly alongside
`SqliteQuoteService` in the same folder, making the "which one is actually registered" question more
visible than it is today split across two projects.

This issue directly unblocks #192: once it lands, #192's plan doc no longer needs the Api-layer
`MasterDataReference` workaround its earlier drafts wrestled with — `QuoteResponse.Series`/`Universe` can
simply use `Quotinator.Core.Models.MasterDataReference` like every other masterdata reference in the
codebase. #192's plan doc will be rewritten after this issue closes to remove that entire discussion.
