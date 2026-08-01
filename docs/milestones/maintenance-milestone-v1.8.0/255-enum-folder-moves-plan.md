# #255 — Move enums to dedicated Enums/ folders

**Status:** Planning
**GitHub issue:** #255
**Tiers required:** N/A
**Depends on:** Nothing

---

## Background

Implements the enum-placement half of [ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md):
every enum lives in a dedicated `Enums/` folder per project, never mixed into `Entities`/`Models`/
`Import`. Pure file/namespace relocation — no schema impact, no behavioural change. Independent of
#253/#254 (the table/entity renames) and safe to do in either order relative to them.

The list below was confirmed against the actual codebase on 2026-08-01 (grep for `enum ` under both
projects) rather than taken as-is from the issue body, which was itself a snapshot from #227's
planning pass.

## Full file inventory

### `Quotinator.Data` → `Quotinator.Data.Enums`

| Enum | Current file | New file |
|---|---|---|
| `InitiatorType` | `src/Quotinator.Data/Models/InitiatorType.cs` | `src/Quotinator.Data/Enums/InitiatorType.cs` |
| `ChangeAction` | `src/Quotinator.Data/Models/ChangeAction.cs` | `src/Quotinator.Data/Enums/ChangeAction.cs` |
| `ImportBatchType` | `src/Quotinator.Data/Entities/ImportBatchType.cs` | `src/Quotinator.Data/Enums/ImportBatchType.cs` |
| `ImportBatchStatus` | `src/Quotinator.Data/Entities/ImportBatchStatus.cs` | `src/Quotinator.Data/Enums/ImportBatchStatus.cs` |
| `CompletenessStatus` | `src/Quotinator.Data/Entities/CompletenessStatus.cs` | `src/Quotinator.Data/Enums/CompletenessStatus.cs` |
| `DuplicateResolutionPolicy` | `src/Quotinator.Data/Import/DuplicateResolutionPolicy.cs` | `src/Quotinator.Data/Enums/DuplicateResolutionPolicy.cs` |
| `DuplicateResolutionPolicyJsonConverter` | `src/Quotinator.Data/Import/DuplicateResolutionPolicyJsonConverter.cs` | `src/Quotinator.Data/Enums/DuplicateResolutionPolicyJsonConverter.cs` |
| `DownloadTarget` | `src/Quotinator.Data/Import/DownloadTarget.cs` | `src/Quotinator.Data/Enums/DownloadTarget.cs` |
| `FieldResolutionChoice` | `src/Quotinator.Data/Import/FieldResolutionChoice.cs` | `src/Quotinator.Data/Enums/FieldResolutionChoice.cs` |
| `SeedBatchOrigin` | `src/Quotinator.Data/Import/SeedBatchOrigin.cs` | `src/Quotinator.Data/Enums/SeedBatchOrigin.cs` |
| `SeedFileIssue` | `src/Quotinator.Data/Import/SeedFileIssue.cs` | `src/Quotinator.Data/Enums/SeedFileIssue.cs` |
| `SourceRefreshOutcome` | `src/Quotinator.Data/Import/SourceRefreshOutcome.cs` | `src/Quotinator.Data/Enums/SourceRefreshOutcome.cs` |

**Correction to the issue body**: the issue lists `DuplicateResolutionPolicy`/`DownloadTarget`/
`FieldResolutionChoice`/`SeedBatchOrigin`/`SeedFileIssue`/`SourceRefreshOutcome` as living in "`Models/`
or `Import/`" — confirmed they are all six already in `Import/`, none in `Models/`. No `Quotinator.Core`
equivalent of these six exists; they were only ever `Quotinator.Data`-owned. `Quotinator.Core.Enums`
below is the correct, distinct set.

### `Quotinator.Core` → `Quotinator.Core.Enums`

| Enum | Current file | New file |
|---|---|---|
| `ConversationLineType` | `src/Quotinator.Core/Models/ConversationLineType.cs` | `src/Quotinator.Core/Enums/ConversationLineType.cs` |
| `ConversationLineTypeJsonConverter` | `src/Quotinator.Core/Models/ConversationLineTypeJsonConverter.cs` | `src/Quotinator.Core/Enums/ConversationLineTypeJsonConverter.cs` |
| `FilteredResultStatus` | `src/Quotinator.Core/Models/FilteredResultStatus.cs` | `src/Quotinator.Core/Enums/FilteredResultStatus.cs` |
| `Genre` | `src/Quotinator.Core/Models/Genre.cs` | `src/Quotinator.Core/Enums/Genre.cs` |
| `QuoteType` | `src/Quotinator.Core/Models/QuoteType.cs` | `src/Quotinator.Core/Enums/QuoteType.cs` |
| `QuoteTypeJsonConverter` | `src/Quotinator.Core/Models/QuoteTypeJsonConverter.cs` | `src/Quotinator.Core/Enums/QuoteTypeJsonConverter.cs` |

**Correction to the issue body**: `DownloadTarget`, `DuplicateResolutionPolicy` (+ its converter),
`FieldResolutionChoice`, `SeedBatchOrigin`, `SeedFileIssue`, `SourceRefreshOutcome` were listed under
"`Quotinator.Core.Enums/`" in the original issue body — confirmed via grep they do not exist anywhere
under `src/Quotinator.Core`; they are `Quotinator.Data`-only (see the Data table above, where they are
listed instead). This is a scope correction, not new work — same 18 total files either way, just
attributed to the correct project.

18 files total (12 Data, 6 Core) — 15 bare enums + 3 paired JSON converters that move with their enum
(no independent purpose apart from it).

---

## Steps

### 1. Move Quotinator.Data enums

**Status:** ⬜ Not started

Create `src/Quotinator.Data/Enums/`, move the 12 files from the table above, update each file's
`namespace` line from `Quotinator.Data.Models`/`Quotinator.Data.Entities`/`Quotinator.Data.Import` to
`Quotinator.Data.Enums`. Add the corresponding `using Quotinator.Data.Enums;` (or update existing
`using Quotinator.Data.Models;`/`.Entities;`/`.Import;`) at every call site — the compiler finds every
miss (CS0246 unresolved type), this is a pure rename/move with no logic change.

### 2. Move Quotinator.Core enums

**Status:** ⬜ Not started

Create `src/Quotinator.Core/Enums/`, move the 6 files from the table above, same namespace-and-using
update as step 1.

### 3. Update Quotinator.slnx

**Status:** ⬜ Not started

No solution-folder change needed — these files live inside their project (`Quotinator.Data`/
`Quotinator.Core`), visible in Solution Explorer through the project node per CLAUDE.md's "Do not add
solution folders for files that are already part of a project" rule. Confirm this remains true after
the move (i.e. no stray `<Folder>` entry was pointing at the old paths).

### 4. Full solution build and test sweep

**Status:** ⬜ Not started

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. Run the grep sweep from
the Definition of Done (`enum ` outside any `Enums/` folder) to confirm nothing was missed. Full test
suite green.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Every enum listed above relocated to its project's `Enums/` folder, namespace updated | Live | `dotnet build --configuration Release -nodeReuse:false` reports 0 warnings, 0 errors |
| 2 | ❌ | A grep sweep for `enum ` outside `Enums/` folders in `Quotinator.Data`/`Quotinator.Core` returns nothing | Live | `rg "^public (sealed )?enum " src/Quotinator.Data src/Quotinator.Core --files-without-match` lists no file outside an `Enums/` path — or equivalently, every match's path contains `\Enums\` |
| 3 | ❌ | No behavioural regression from the move | Unit test | Full existing test suite (`dotnet test --configuration Release -nodeReuse:false`) passes unchanged — this is a rename, no new test is needed to prove new behaviour because there is none |

---

## Scope changes

None.
