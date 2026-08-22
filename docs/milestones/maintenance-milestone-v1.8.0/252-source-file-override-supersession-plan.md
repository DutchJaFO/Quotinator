# #252 — Confirm whether SourceFileOverride should be superseded by FileResource

**Status:** Waiting for release
**GitHub issue:** #252
**Tiers required:** T1, T2
**Depends on:** #251 (done)

---

## Background

`Import_SourceFileOverride` (post-#253's rename; today `System_SourceFileOverrides`,
`src/Quotinator.Data/Entities/SourceFileOverride.cs`) is #153's narrow registry recording whether a
bundled source's `ruleFile`/`sourceAliasFile` has been overridden by a generated copy on the persistent
volume. #251's `Import_FileResource` covers a much broader version of the same underlying question —
"what file actually produced this" — for every import, not just rule-file overrides. This issue exists
to make the supersession call explicitly rather than leave it as a lingering "should we" with no owner,
per its own Definition of Done.

## Shape comparison (confirmed 2026-08-01, before #251 is implemented)

`SourceFileOverride` today has: `FileName` (string), `Origin` (`SafeValue<SeedBatchOrigin?>`),
`ContentHash` (SHA-256 hex string), `SourceBatchId` (loose string reference, explicitly "no FK — this
project doesn't know the consumer's batch table name," per its own doc comment).

#251's proposed `Import_FileResource` has: `FileName`, `Origin` (same `SeedBatchOrigin` enum),
`ContentHash` (same SHA-256 hex format), plus `Content` (the actual file bytes, which
`SourceFileOverride` does not store) and a proper `Import_FileResourceBatch` join table (a real FK,
unlike `SourceFileOverride`'s loose string reference — possible because `Import_FileResourceBatch`
lives in the same project as `Import_Batch` and can reference it directly).

On paper this made `Import_FileResource` look like a strict superset. The real comparison below (against
#251's actual, shipped schema) found that isn't the case.

---

## Decision 1: keep `SourceFileOverride` separate (confirmed with the developer, 2026-08-04)

Re-run against #251's actual shipped schema, not the original proposal. Four reasons, in order of
weight:

1. **`FileResource` is a general-purpose text-file-content store; `SourceFileOverride` is the seeding
   trust mechanism itself.** `Import_FileResource`'s only real requirement is "the content is
   text" — it is not conceptually tied to imports, and other unrelated future features could reasonably
   store other kinds of text file through it. `SourceFileOverride` isn't a file-storage concern at all;
   it *is* `#153`'s override-trust state. Folding a domain-specific trust registry into a generic storage
   primitive would couple `FileResource`'s future unrelated consumers to seeding-specific semantics they
   have no reason to know about. This is the deciding reason — the other three would each be individually
   fixable with more code, but this one is a reason not to want to.
2. **Different semantics: current-state registry vs. append-only history.** `ISourceFileOverrideRegistry`
   is upsert-keyed by `(FileName, Origin)` — at most one row per slot, always the *current* state, with an
   explicit `RemoveAsync`. `Import_FileResource` is content-addressed and append-only (dedup by
   `ContentHash`, `LastSeenAtUtc` bumped on a repeat capture, `PruneAsync` trims by count) — a *history* of
   every distinct version ever seen, with no "unregister this one" operation and no "which row is active"
   concept. Both would have to be built on top of `FileResource` to match today's behaviour, which is
   really just reimplementing `SourceFileOverride` a second time.
3. **Real trust-boundary weakening if merged.** `SourceFileOverride.RegisterAsync` is called from exactly
   one place (`ImportRuleEndpoints`'s admin-key-gated `/conflict/generate`). `Import_FileResource` is
   written from three pipelines, including the uploaded-import path and a flat scan of the user-imports
   folder that captures *any* file found there, not just intentional overrides.
   `EffectiveRuleFileResolver.ResolveEffectivePathAsync`'s entire job is "was this override file on disk
   genuinely the one our own generator last wrote" — answering that from a table other, less-trusted
   pipelines can also populate would need careful re-scoping to avoid a coincidentally-matching hash from
   an unrelated import vouching for a tampered override file.
4. **`SourceBatchId` points the opposite direction from `Import_FileResourceBatch`.**
   `SourceFileOverride.SourceBatchId` means "this override's content was *generated from* batch X" (output
   provenance). `Import_FileResourceBatch` means "this file was *read as input by* batch X" (input
   consumption). Folding both into one join table would conflate two different relationships.

**No migration, no removal.** `SourceFileOverride`/`ISourceFileOverrideRegistry`/`SourceFileOverrideRegistry`
stay exactly as they are. This decision only needs to be recorded so the question doesn't recur — see
Step 3 below.

---

## Decision 2: generalize `FileResourceOrigin` (new scope, confirmed with the developer, 2026-08-04)

Raised during the discussion above: if `FileResource` is meant to be a general-purpose text-file store
(reason 1 above), `FileResourceOrigin`'s current member names (`Bundled`, `UserImports`) are import/seed-
flavoured naming baked into what's supposed to be a generic primitive. Renaming alone doesn't fix
this — today `Bundled`/`UserImports` also implicitly say *which directory* `OriginalFolderPath` is
relative to (`data/sources/` / `{dataDir}/imports/`, per the entity's own doc comment), so a bare rename
would just relabel the same implicit convention rather than generalize it.

**Resolved shape:**

- `FileResourceOrigin.Bundled` → `System` (written by the app's own internal scan of a fixed/read-only
  local directory). `.UserImports` → `User` (written by a scan of a user-writable local directory).
  `.Uploaded` → `Upload`, for naming consistency with the other two (all three now a bare noun describing
  the write-path mechanism: system / user / upload).
- New nullable `HomeDirectoryKey` (`string?`) column on `Import_FileResource`, decoupling "which named
  root is `OriginalFolderPath` relative to" from `Origin` itself. `Origin` stays a small, closed set
  describing *the write-path mechanism* (system scan / user scan / upload); `HomeDirectoryKey` becomes the
  open-ended, per-consumer key identifying *which root* — `"sources"` for today's seed-file capture,
  `"imports"` for today's user-imports capture, `null` for `Upload` (matches `OriginalFolderPath`'s own
  null-ness there, since an upload carries no folder). Resolving a `HomeDirectoryKey` string to an actual
  filesystem path stays external to this table (config/a resolver), never hardcoded per-`Origin`-value
  again — a future `System`-origin consumer unrelated to quote sources can register its own key without
  stretching `Origin`'s meaning.
- **`OriginalFolderPath` is unpopulated in practice today** — confirmed via
  `QuotinatorDatabaseInitializer.cs`'s own comment: today's directory scan is flat (no subfolders under
  `data/sources/` or `{dataDir}/imports/}`), so every existing row already has `OriginalFolderPath = null`
  regardless of origin. This is good timing for the decoupling — no real data depends on the old implicit
  mapping being preserved.

**Requires a new migration (version 7) — editing #251's version 6 in place was considered and rejected.**
`#251`'s migration was created after `v1.8.2` (the last tagged release) and has never shipped, which
initially looked like a case for the "unreleased migrations are safe to squash/edit" precedent (ADR 015,
#155). **That reasoning is wrong, and ADR 015 has been corrected (see its own "Revision — issue #254"
section, added as part of this issue) after it already caused a real incident during #254's T1 pass**:
"never edit an already-applied migration" protects any database that has *actually run* the migration,
not only databases represented by a tagged release — and a developer's own local database routinely runs
an unreleased migration long before any tag exists. This project's own dev database already ran version 6
during #251's T1 verification earlier this same session. Worse, `Import_FileResource` is a **Data-owned**
migration (`System_SchemaVersion`), which — unlike a consumer's own domain migrations — is never wiped or
replayed by a Reset, so there is no "just Reset it" recovery path if version 6 is edited out from under an
already-migrated database. A new version-7 migration is the only safe option:

- Table rebuild of `Import_FileResource` only (`Import_FileResourceLine`/`Import_FileResourceBatch` don't
  reference `Origin`, so they're untouched): create the table under its final shape (new CHECK constraint
  values, new `HomeDirectoryKey` column), copy existing rows across remapping `'Bundled'` → `'System'`,
  `'UserImports'` → `'User'`, `'Uploaded'` → `'Upload'` in the copy's `SELECT`, drop the old table, rename.
  Matches this project's standard enum-value-change rebuild pattern (ADR 008).
- `DataOwnedMigrations` gains `new SchemaMigration { Version = 7, Sql = ... }`, appended after version 6 —
  version 6 itself stays byte-for-byte unedited.
- `DataBaselineSql` updated to match the final (post-version-7) shape directly, same as every other
  baseline update.
- Add a migration test proving a database that already ran version 6 (with real `'Bundled'`/`'UserImports'`
  rows) ends up with correctly remapped values and the new column after version 7 runs — this is the
  scenario version 6-edited-in-place would have silently gotten wrong.

---

## Steps

### 1. Wait for #251

**Status:** ✅ Done — #251 shipped and is `Waiting for release`.

### 2. Compare against the real #251 schema and decide

**Status:** ✅ Done — see Decision 1 and Decision 2 above. Both confirmed with the developer 2026-08-04.

### 3. Implement Decision 1 (documentation only)

**Status:** ✅ Done

The four reasons (condensed, with cross-references) added to `SourceFileOverrideEntity`'s XML doc
comment as a `<remarks>` block, so a future reader hits the reasoning at the point they'd naturally ask
"why isn't this just a FileResource."

### 4. Implement Decision 2 (rename + new column, via a new version-7 migration)

**Status:** ✅ Done

- `FileResourceOrigin`: renamed `Bundled` → `System`, `UserImports` → `User`, `Uploaded` → `Upload`.
- New `FileResourceOriginGeneralizationMigrations.GeneralizeOrigin`, `DataOwnedMigrations` version 7:
  table-rebuild of `Import_FileResource` remapping existing Origin values (`'Bundled'`→`'System'` etc.)
  and adding `HomeDirectoryKey TEXT`, backfilled from the remapped Origin (`System`→`"sources"`,
  `User`→`"imports"`, `Upload`→`NULL`). `DatabaseInitializer.DataBaselineSql` updated to match.
- `FileResourceEntity.HomeDirectoryKey` property; `FileResourceListItem`/`FileResourceResponse` updated
  to expose it.
- `SqliteFileResourceRepository.WriteAsync`/`IFileResourceRepository.WriteAsync` gained an optional
  `homeDirectoryKey` parameter (trailing, defaulted to `null` — matches `converter`/`converterOptions`'s
  own optional-trailing-parameter convention, so most existing call sites needed no change).
- `QuotinatorDatabaseInitializer.cs`'s seed-capture call site passes `"sources"`/`"imports"` depending on
  `SeedBatchOrigin`; `SqliteQuoteImportService.cs`'s upload-capture call site passes `null` explicitly.
- `ImportFileResourceEndpoints.cs`: `origin` query-param validation/enum parsing (unchanged logic, new
  accepted values), response mapping, `[Description]` text.
- `ApiMessages.cs`'s `FileResourceOriginInvalid` message + `UI.en-GB.json`/`UI.nl.json`/`UI.de.json`
  updated to list the new values.
- `FileResourceEntity`/`OriginalFolderPath`/`FileResourceOrigin` doc comments rewritten around the new
  `HomeDirectoryKey`-based model instead of the old implicit Origin→directory mapping.
- Tests updated: `SqliteFileResourceRepositoryTests.cs` (enum values renamed in both C# and its own
  inline test-schema SQL; two new tests for `HomeDirectoryKey`), `ImportFileResourceEndpointsTests.cs`,
  `FakeFileResourceRepository.cs`, `NoOpFileResourceRepository.cs`. `ImportRuleEndpointsTests.cs`/
  `SeedBatchesBuilderTests.cs` confirmed unaffected (use `SeedBatchOrigin`, a different enum). Three
  pre-existing tests with hardcoded Data-owned migration counts (`5`→`6` at #251, now `6`→`7`) fixed:
  `ApplyBaselineAsync_NoConsumerBaselineDefined_FallsThroughToIncremental`,
  `InitialiseAsync_LegacyV172SchemaVersionTable_SplitsCorrectlyAndReplaysRemainingMigrations`,
  `InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental`. New migration test
  `Migration007_RemapsPreGeneralizationOriginValuesAndPreservesChildRowLinks` — its first run caught a
  real bug in the test itself (not the migration): without explicitly disabling FK enforcement to match
  `ApplyMigrationsAsync`'s own `PRAGMA foreign_keys = OFF` window, SQLite treated the migration's
  `DROP TABLE Import_FileResource` as cascading the delete to `Import_FileResourceLine` (`ON DELETE
  CASCADE`), losing the very row the test exists to prove survives — fixed by toggling the same PRAGMA
  the real migration runner already does.
- `docs/api-endpoints.md` (the `origin` filter value list) and `docs/smoke-tests.md` §30 updated (now
  `docs/automated-testing/`, whose README maps the old section numbers).
  Found and fixed along the way: §30 still said "there is no list endpoint for `Import_FileResource`"
  and had zero curl coverage of the GET list/detail endpoints or the `linkedBatchCount`/`linkedBatchIds`
  cross-check against `GET /import/batches` — despite #251's own plan doc (verification row 18) already
  claiming this was T2-confirmed. It wasn't; that row's claim was wrong. Added properly as part of this
  step (see Step 5).

### 5. Full build/test/T1/T2 verification

**Status:** ✅ Done — T1 and T2 both confirmed

`dotnet build --configuration Release -nodeReuse:false` — 0 warnings, 0 errors. `dotnet test
--configuration Release -nodeReuse:false` — full suite green (939 Data.Tests + 1418 Core.Tests + 591
Api.Tests + smaller projects, 0 failures). `docker build -f docker/Dockerfile -t quotinator:local .` —
succeeded. T2 smoke-tested against a live container (`docs/smoke-tests.md` §30 — now
`docs/automated-testing/`, whose README maps the old section numbers — extended per the
living-checklist rule):
- Fresh container log confirmed `data v7` at baseline (the new `Origin`/`HomeDirectoryKey` shape applied
  directly, no incremental replay needed for a fresh install).
- `GET /import/file-resources` — all 5 bundled/manifest rows show `origin: "system"`,
  `homeDirectoryKey: "sources"`; `?origin=bogus` → `422`; `?origin=system` → `totalCount: 5`.
- `GET /import/file-resources/{manifest-id}` — `linkedBatchCount: 4`, `linkedBatchIds` containing
  exactly 4 ids.
- `GET /import/batches?type=seed` — `totalCount: 4`; `?status=bogus` → `422`; every id from the
  FileResource detail's `linkedBatchIds` resolves via `GET /import/batches/{id}`.
- Download reconstruction still byte-identical to the original on disk; prune still `401`/`200`.
- Found and fixed during verification (not a real bug in this issue's own code): `docker cp`-ing just
  `quotinatordata.db` without its `-wal`/`-shm` sidecars produced a stale snapshot (WAL-mode writes not
  yet checkpointed) that under-reported `manifest.json`'s batch links via the raw-SQL join check —
  re-copying with the sidecars included matched the live API exactly. Every `docker cp
  .../quotinatordata.db` step across `docs/smoke-tests.md` (§11, §22, §23, §30 — now
  `docs/automated-testing/`, whose README maps the old section numbers) updated to always copy
  the sidecars alongside it, since this affected sections this issue didn't otherwise touch.
- Also ran the **full** `docs/smoke-tests.md` suite (all 30 sections as it then stood, not just §30;
  now `docs/automated-testing/`) end to end as a
  regression pass — no functional failures; a few pre-existing doc-staleness notes found and left for a
  separate cleanup (unrelated to #251/#252's own code).

**T1 confirmed** — developer ran the app in Visual Studio 2026-08-04: `schema is up to date (data v7,
app v6)`, clean startup, correct stats (799 quotes etc.), no errors.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Decision made: superseded, or kept as a separate permanent registry | Live | Decision 1 above, confirmed with the developer 2026-08-04 |
| 2 | ✅ | Reasoning documented on `SourceFileOverrideEntity` so the question doesn't recur | Live | `SourceFileOverrideEntity`'s `<remarks>` block |
| 3 | ✅ | `FileResourceOrigin` renamed (`System`/`User`/`Upload`) via a new migration; an already-migrated database (existing `'Bundled'`/`'UserImports'` rows) is correctly remapped, not just fresh installs | Unit test | `Migration007_RemapsPreGeneralizationOriginValuesAndPreservesChildRowLinks`; `DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalFileResourceSchema` still passes |
| 4 | ✅ | New `HomeDirectoryKey` column populated correctly by both local-scan write sites, `null` for `Upload` | Unit test | `SqliteFileResourceRepositoryTests.WriteAsync_SystemOrigin_StoresSuppliedHomeDirectoryKey`, `...UploadOrigin_StoresNullHomeDirectoryKey` |
| 5 | ✅ | API surface (`origin` filter, response field) updated consistently | Endpoint test | `ImportFileResourceEndpointsTests` |
| 6 | ✅ | Full solution builds and tests pass | Live | `dotnet build`/`dotnet test --configuration Release -nodeReuse:false` — 0 warnings/0 errors, full suite green |
| 7 | ✅ | T1 verified | Live | Developer ran the app in Visual Studio 2026-08-04 — `schema is up to date (data v7, app v6)`, clean startup, correct stats |
| 8 | ✅ | T2 verified | Live | `docs/smoke-tests.md` §30 (now `docs/automated-testing/`, whose README maps the old section numbers) — origin filter with new values, `homeDirectoryKey`/`linkedBatchCount`/`linkedBatchIds` in responses, confirmed against a live Docker container; plus a full 30-section regression pass of the entire smoke-test suite |

---

## Scope changes

**2026-08-04, after Decision 1 was reached:** the developer raised generalizing `FileResourceOrigin`
beyond its current import-flavoured naming, since `FileResource` itself is meant to be usable by future
unrelated text-file consumers. Folded into this issue's scope as Decision 2 rather than filed separately,
since it directly follows from Decision 1's own reasoning (reason 1) about what `FileResource` is for.
