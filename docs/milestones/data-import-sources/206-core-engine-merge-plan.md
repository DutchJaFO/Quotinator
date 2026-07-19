# #206 — Merge Quotinator.Engine into Quotinator.Core

**Status:** Planning
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

**Status:** Not started.

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

**Status:** Not started.

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

**Status:** Not started.

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

**Status:** Not started.

Remove the `Quotinator.Engine` `<ProjectReference>` from `Quotinator.Api.csproj` (the `Quotinator.Core`
reference already covers everything). Update `using Quotinator.Engine.X;` → `using Quotinator.Core.X;`
in:

`Program.cs` (5 using lines: `Database`, `Entities`, `Helpers`, `Repositories`, `Services`),
`OpenApi/EnumParameterSchemaTransformer.cs`, `Endpoints/ConversationEndpoints.cs`,
`Endpoints/PersonEndpoints.cs`, `Endpoints/ImportEndpoints.cs`, `Endpoints/CharacterEndpoints.cs`,
`Endpoints/UniverseEndpoints.cs`, `Endpoints/StageDirectionEndpoints.cs`,
`Endpoints/SeriesEndpoints.cs`, `Endpoints/SourceEndpoints.cs`, `Endpoints/SoundCueEndpoints.cs`.

### 5. Move `MasterDataReference` and every masterdata response DTO to `Quotinator.Core.Models`

**Status:** Not started.

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

**Status:** Not started.

`Quotinator.Core.Tests` and `Quotinator.Engine.Tests` files are covered by Step 3's move. The remaining
category is `Quotinator.Api.Tests` — 23 files (`Endpoints/*.cs` test files and `Fakes/*.cs`) referencing
`Quotinator.Engine.Entities`/`Repositories`/`Models`/`Services` — each gets the same
`using Quotinator.Engine.X;` → `using Quotinator.Core.X;` mechanical update. No test logic changes.

### 7. Update `Quotinator.slnx`

**Status:** Not started.

Remove the `Quotinator.Engine` and `Quotinator.Engine.Tests` project entries. Confirm the merged files
under `Quotinator.Core`/`Quotinator.Core.Tests` don't need explicit `<Folder>` entries — per the File
placement rule, files inside a project's own folder structure are visible via the project node and don't
need one; only files living outside any project need an explicit `<Folder>`.

### 8. Update ADR 004

**Status:** Not started.

Add a new `## Revision — issue #206 merged Quotinator.Engine back into Quotinator.Core` section:
record that the three-project split's second half (Core must stay Dapper/SQLite-free) is retired,
`Quotinator.Data` stays domain-agnostic and unaffected, and the project graph returns to two Quotinator
layers (`Quotinator.Data` infrastructure; `Quotinator.Core` everything domain-specific including its own
SQLite implementation) under `Quotinator.Api`. Do not edit the existing 2026-06-25/#121/#157/#158
sections' text — ADRs are append-only history per this project's own doc-update convention; a merge is a
new revision section, not a rewrite of the old ones.

### 9. Update `CLAUDE.md`

**Status:** Not started.

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

**Status:** Not started.

`dotnet build --configuration Release` → 0 warnings, 0 errors. `dotnet test --configuration Release
--verbosity normal` → full merged suite green, 0 warnings, 0 errors — every existing test from both
former projects must pass unmodified, proving the merge changed no behaviour.
`SqlQueryGuardTests` and the schema-drift tests (`QuotinatorMigrationsTests`, baseline/incremental-replay
comparisons) are the highest-risk regression surface since they reflect over type/namespace shape
directly — confirm both explicitly, not just "suite is green."

T2 (Docker): full `docker build`/`docker run` smoke matrix per CLAUDE.md's step 6 checklist — this issue
touches `Program.cs`, package references, and the solution structure, independently satisfying the T2
trigger regardless of the general "always run T2" rule.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Every `Quotinator.Engine` namespace/file is gone from the solution | Unit test | `dotnet build --configuration Release` — 0 warnings, 0 errors; manual grep for `Quotinator.Engine` returns zero hits outside CHANGELOG/ADR history |
| 2 | ❌ | `MasterDataReference` and all 8 masterdata response DTOs live in `Quotinator.Core.Models`; no duplicate type exists | Unit test | `dotnet build --configuration Release` |
| 3 | ❌ | Full merged test suite passes unmodified — no behaviour change | Unit test | `dotnet test --configuration Release --verbosity normal` — all tests green, 0 warnings, 0 errors |
| 4 | ❌ | `SqlQueryGuardTests` and schema-drift tests specifically pass | Unit test | `dotnet test --filter SqlQueryGuard\|SchemaDrift\|QuotinatorMigrations` |
| 5 | ❌ | `Quotinator.slnx`, `Quotinator.Api.csproj`, ADR 004, and CLAUDE.md all updated | Doc/solution review | Files updated |
| 6 | ❌ | T1 — app starts in Visual Studio | Live (T1) | Developer confirmed |
| 7 | ❌ | T2 — full smoke matrix against the built image | Live (T2) | `docker build`/`docker run` — CLAUDE.md step 6 |

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
