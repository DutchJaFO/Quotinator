# #251 — FileResource/FileResourceLine: general import-file content provenance

**Status:** Planning
**GitHub issue:** #251
**Tiers required:** T1, T2
**Depends on:** Nothing

---

## Background

Split out of #227 (2026-08-01) as a genuinely undesigned feature, unrelated to that issue's naming
standardization. This project currently has no record of the actual file(s) an import batch was built
from — `Import_Batch` (post-#253's rename) records `Origin`/`Type` and a few summary fields, but never
the source file's own name, content, or a hash of it. This issue designs and implements that record,
owned by `Quotinator.Data` (generic, domain-agnostic import infrastructure, per ADR 015's `Import_`
domain), matching where `Import_Batch`/`Import_SourceFileOverride` already live.

**Nothing below is settled by the GitHub issue itself** — its own body states the granularity question
"not decided here." This plan doc proposes a recommendation for each open decision and flags it for
explicit developer confirmation before implementation starts (step 1), per
`docs/workflow/process.md`'s rule that scope/design decisions are surfaced, not silently picked.

---

## Decisions requiring confirmation

### 1. Granularity: whole-file only, no `FileResourceLine`

**Recommendation: reject line-level granularity. Whole-file hash + a full raw-content copy is
sufficient for every stated use case.**

Reasoning:
- Source files are JSON arrays, not line-oriented text — "a line" has no stable, meaningful unit
  inside a JSON file the way it would for a CSV or log file. A `FileResourceLine` would really mean
  "one row per JSON array entry," which duplicates what `QuoteIdentity`/`SourceEntry`/entity rows
  already represent once imported — a second, parallel decomposition of the same content.
- All three stated use cases (permanent reference, history reconstruction, future diff/replay) are
  fully satisfied by a whole-file content hash plus a stored copy of the raw content: reconstruction
  needs the bytes, not a pre-decomposed table; a future "diff this import against last time" feature
  can diff two whole-file snapshots directly (structured JSON diff tooling operates on the raw content,
  it doesn't need a per-line SQL table to do that).
- Per CLAUDE.md's Project Priorities, Simplicity ranks above Extensibility for this homelab project.
  A child table that grows by one row per array entry per distinct file version is a materially larger
  and harder-to-prune surface than the parent table alone — directly working against requirement 2
  ("needs a pruning mechanism from day one... a real concern for a SQLite-backed homelab deployment").

**If this recommendation is rejected**, `Import_FileResourceLine` would need its own pruning story
(cascade-deleted with its parent `Import_FileResource` row is the obvious answer, but that still means
importing a single 500-entry source file writes 501 rows instead of 1) — this plan doc's schema and
verification checklist below assume whole-file-only until told otherwise.

### 2. Batch-association shape: a lightweight join table, not a column on either side

**Recommendation:** `Import_FileResource` is deduplicated by content hash — re-importing an unchanged
file (the common case: reseeding from the same bundled JSON) does not create a new row, only updates
`LastSeenAtUtc`. This means a single `Import_FileResource` row can legitimately have been the source
for many `Import_Batch` rows over time (e.g. every reseed since the file was last edited). A one-to-one
FK on either table's own columns can't express that, so the design needs a small join table,
`Import_FileResourceBatch (FileResourceId, ImportBatchId, ImportedAt)` — three columns, no content
duplication, growing at the same rate `Import_Batch` itself already grows (proportional to import
events, not to file size or count).

**Alternative considered and rejected:** a `FileResourceId` column directly on `Import_Batch`. Rejected
because a single import call can span multiple source files (e.g. `POST /api/v1/import` with several
files in one request), so the relationship is many-to-many, not many-to-one.

### 3. Pruning policy: keep the N most-recently-seen distinct files, by `FileName`

**Recommendation:** the admin endpoint takes a `keepPerFile` parameter (default from a new
`Quotinator.Constants.Api.QueryParamDefaults` constant, proposed value 5) and, per distinct `FileName`,
deletes every `Import_FileResource` row beyond the N most recent by `LastSeenAtUtc`, cascading the
delete to matching `Import_FileResourceBatch` rows (FK `ON DELETE CASCADE`, since a batch's own
`Import_Batch` row is the permanent record — only the *file content copy* is being pruned, not the
batch's existence). This is a per-file retention count, not a global row cap or age-based expiry —
simplest to reason about, and directly bounds the thing that actually grows unboundedly (distinct file
*versions* over the file's own edit history), since unchanged reseeds never add a new row to prune in
the first place (per decision 2 above).

No precedent exists elsewhere in this codebase for a pruning/retention mechanism (confirmed via grep —
this is genuinely new ground), so there is no existing pattern to match against; this recommendation is
this plan doc's own proposal, not a "verified against docs" fact the way most of this project's other
design decisions are.

---

## Proposed schema

```sql
CREATE TABLE IF NOT EXISTS Import_FileResource (
    Id           TEXT    NOT NULL PRIMARY KEY,
    FileName     TEXT    NOT NULL,
    Origin       TEXT    NOT NULL CHECK (Origin IN ('Bundled', 'UserImports')),
    ContentHash  TEXT    NOT NULL,
    Content      TEXT    NOT NULL,
    FirstSeenAtUtc TEXT  NOT NULL,
    LastSeenAtUtc  TEXT  NOT NULL,
    DateCreated  TEXT    NOT NULL,
    DateModified TEXT,
    DateDeleted  TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_FileResource_ContentHash ON Import_FileResource (ContentHash);
CREATE INDEX IF NOT EXISTS IX_Import_FileResource_FileName ON Import_FileResource (FileName);

CREATE TABLE IF NOT EXISTS Import_FileResourceBatch (
    FileResourceId TEXT NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
    ImportBatchId  TEXT NOT NULL REFERENCES Import_Batch(Id),
    ImportedAt     TEXT NOT NULL,
    PRIMARY KEY (FileResourceId, ImportBatchId)
);
```

- `Origin` reuses the existing `SeedBatchOrigin` enum (`Bundled`/`UserImports`,
  `src/Quotinator.Data/Import/SeedBatchOrigin.cs`) via the project's standard `SafeValue<TEnum?>` +
  `RegisterEnumHandler<TEnum>` pattern — not a new enum. CHECK constraint per ADR 008.
- `ContentHash` is SHA-256 of the raw file bytes (hex-encoded), matching the "content hash" language in
  the issue body. Unique index enforces the dedup-by-content invariant from decision 2.
- `Content` stores the raw file text verbatim — this is what makes "reconstruct a bundled file's own
  history/state without relying on the filesystem or git history" (the issue's own stated goal)
  actually true.
- `Import_FileResourceBatch` has no `RecordBase` fields — it is a pure link row, matching
  `CharacterSources`-style association-table precedent (no soft-delete concept applies to a link that
  either exists or doesn't).
- References `Import_Batch(Id)` (#253's renamed table) — this migration must land after #253's, or in
  the same migration if implemented on the same branch after #253 merges. Table names in this schema
  already assume #253 has shipped.

---

## Steps

### 1. Confirm the three design decisions above with the developer

**Status:** ⬜ Not started

Blocking step — no migration or code is written until decisions 1–3 above are explicitly confirmed or
revised.

### 2. Write the migration and baseline

**Status:** ⬜ Not started

New `Quotinator.Data` migration (next version after #253's squashed rename) creating both tables per
the schema above. Update `DataBaselineSql` to match, per CLAUDE.md's baseline-drift rule.

### 3. Implement the write path

**Status:** ⬜ Not started

Hook into the existing import pipeline (`ISourceCacheUpdater`/`SqliteQuoteImportService`/wherever a
source file is read and turned into a batch) to: compute the file's content hash, look up an existing
`Import_FileResource` by hash (update `LastSeenAtUtc` if found) or insert a new row (not found), then
insert an `Import_FileResourceBatch` link row for the batch being created. New repository interfaces
(`IFileResourceRepository`, or extend an existing import repository) following this project's DI
registration policy.

### 4. Implement the pruning mechanism and admin endpoint

**Status:** ⬜ Not started

`POST /api/v1/admin/import/file-resources/prune?keepPerFile={n}` in the `adminGroup` (destructive —
requires `X-Api-Key`, `RateLimitPolicies.Admin`), implementing decision 3's per-`FileName` retention
sweep. Returns a count of rows pruned. Add the `keepPerFile` default to
`Quotinator.Constants.Api.QueryParamDefaults`, following the numeric query parameter binding pattern
(`string?`-bound, parsed, 422 on malformed input).

### 5. Tests

**Status:** ⬜ Not started

Exact test list matches the GitHub issue's own Expected tests table:

- `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalFileResourceSchema`
- `SqliteFileResourceRepositoryTests.WriteAsync_UnchangedFileContent_DoesNotDuplicateRow`
- `SqliteFileResourceRepositoryTests.WriteAsync_ChangedFileContent_CreatesNewRow`
- `SqliteFileResourceRepositoryTests.WriteAsync_LinksFileResourceToImportBatch`
- `SqliteFileResourceRepositoryTests.PruneAsync_KeepsOnlyKeepPerFileMostRecentRowsPerFileName`
- `SqliteFileResourceRepositoryTests.PruneAsync_CascadesDeleteToFileResourceBatchLinks`
- `AdminEndpointsTests.PruneFileResources_NoApiKey_Returns401`
- `AdminEndpointsTests.PruneFileResources_MalformedKeepPerFile_Returns422`

### 6. Full solution build, test, and Docker verification

**Status:** ⬜ Not started

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. T1 + T2.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Granularity decision (line-level vs whole-file) made and recorded | Live | This plan doc's "Decisions requiring confirmation" section, confirmed by the developer before step 2 begins |
| 2 | ❌ | Schema implemented (migration + baseline updated together) | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalFileResourceSchema` |
| 3 | ❌ | Re-importing unchanged file content does not duplicate the `Import_FileResource` row | Unit test | `SqliteFileResourceRepositoryTests.WriteAsync_UnchangedFileContent_DoesNotDuplicateRow`, `SqliteFileResourceRepositoryTests.WriteAsync_ChangedFileContent_CreatesNewRow` |
| 4 | ❌ | A batch's originating file(s) are queryable after import | Unit test | `SqliteFileResourceRepositoryTests.WriteAsync_LinksFileResourceToImportBatch` |
| 5 | ❌ | Pruning mechanism implemented and exposed via an admin endpoint | Unit test | `SqliteFileResourceRepositoryTests.PruneAsync_KeepsOnlyKeepPerFileMostRecentRowsPerFileName`, `SqliteFileResourceRepositoryTests.PruneAsync_CascadesDeleteToFileResourceBatchLinks` |
| 6 | ❌ | Prune endpoint follows this project's admin conventions | Endpoint test | `AdminEndpointsTests.PruneFileResources_NoApiKey_Returns401`, `AdminEndpointsTests.PruneFileResources_MalformedKeepPerFile_Returns422` |
| 7 | ❌ | Full solution builds and tests pass | Live | `dotnet build --configuration Release -nodeReuse:false` and `dotnet test --configuration Release -nodeReuse:false` both 0 warnings, 0 errors, all green |
| 8 | ❌ | T1 verified | Live | Developer starts app in Visual Studio, confirms no startup error |
| 9 | ❌ | T2 verified | Live | `docker build -f docker/Dockerfile -t quotinator:local .` succeeds; a smoke-test import followed by a prune call returns expected output |

---

## Scope changes

None yet — this plan doc is the first design pass. If decisions 1–3 above are revised during
confirmation, this doc's schema and steps are updated to match before implementation begins (per
`docs/workflow/process.md`'s "Scope changes and deferrals").

## Relationship to #252

#252 (confirm whether #153's `Import_SourceFileOverride` should be superseded by this mechanism) is
blocked on this issue reaching an implemented, confirmed schema — see #252's own plan doc.
