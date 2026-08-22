# #156 — Reset: baseline script instead of drop-all-user-tables + replay, plus a system-reseed extension point

**Status:** Waiting for release
**GitHub issue:** #156
**Tiers required:** T1, T2 (from the step that changes observable Reset/Reseed behaviour onward — see per-step notes)
**Depends on:** #253/#254 (done — targets post-rename table names). Release gate (not an implementation-order
dependency): must ship in the same release as #249 (done), per [ADR 014](../architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md).

---

## Background

The GitHub issue's own proposal (see issue body) is two changes to Reset:

1. Use the baseline script (drop the entire database, recreate in one step) instead of
   `GetUserTables`-exclusion + drop + incremental-replay.
2. Reset must not reseed bundled/user quote content afterward — remove the `SeedIfEmptyInternalAsync`
   call from `OnResetAsync`. Bundled quote content is optional, discardable domain data (matching
   CLAUDE.md's "Endpoint side-effect policy").

The issue's own text carves out one exception: "pre-loaded system/reference tables, if any exist, are
expected to survive Reset — via the baseline, not via a reseed step... no separate 'is this a system
table' branch is needed at Reset time," and notes "today, no such tables exist" so this was left
theoretical.

**Redesigned 2026-08-05, before implementation started, after further discussion with the developer:**
static baseline `INSERT`s are inflexible for content that may need updating without a schema migration
— every existing bundled/user content mechanism in this project (`data/sources/*.json` + user imports)
is already file-driven for exactly that reason. Instead of baking system content into the baseline SQL,
this becomes a genuine third mechanism, parallel to (not instead of) the baseline:

**Two separate reseed actions, not one:**
1. **System reseed** — adds content to designated system tables. Runs unconditionally after *any*
   database reset (and after a genuinely fresh database is first created — a fresh DB is functionally
   "reset into nothing," so the same content is missing either way). This is vital content the
   application needs to function, not optional domain data — the developer's framing directly.
2. **Standard reseed** — today's existing mechanism (`SeedIfEmptyInternalAsync`): bundled (`data/sources/`)
   + user (`{DataDir}/imports/`) content. Unaffected by this issue except for losing its automatic
   call from `OnResetAsync` (point 2 above) — it remains fresh-install-only or explicit-call-only.

**Two bundled-file directories follow from this:** `data/sources/` stays exactly as it is today
(standard reseed content, zero disruption to existing docs/tests/manifest.json/Docker layout); a new
sibling `data/system/` holds system-reseed content files, empty for now since — as the original issue
observed — no real system/reference table exists yet.

## Decisions confirmed with the developer (2026-08-05)

**1. Prove the mechanism with test-only dummy fixtures before wiring real Reset behaviour to it.**
Two dummy datasets, deliberately never added to either project's real migration list or baseline, so
nothing about this proof-of-concept ships in the Docker image or a real user's schema:

- `SystemContent_` prefix — a dummy table representing "a dataset **we** (the library) define as a
  standard, vital feature" — lives in **Quotinator.Data**'s own test suite, proving the generic
  extension point works for the library itself.
- `UserContent_` prefix — a dummy table representing "a dataset **a user** (a downstream consumer of
  the library) might define" — lives in **Quotinator.Core**'s own test suite (Quotinator.Core is,
  architecturally, itself just a consumer/"user" of Quotinator.Data — see ADR 004/015), proving a
  third-party consumer can use the same extension point for their own content.

Chosen over (a) permanent example code alongside `Quotinator.Data.Example`'s existing repository-pattern
examples, or (b) real production tables removed later — both carry more risk or overhead for a
proof-of-concept than a test-only fixture, and ADR 015's own "a migration is frozen the moment it runs
once locally" lesson makes (b) specifically expensive to get wrong.

**2. New directory: `data/system/`, sibling to `data/sources/`.** `data/sources/` is untouched.

**3. Prefixes: `SystemContent_` / `UserContent_`.** Two new domains, neither reusing `Import_`/`Audit_`/
`System_`/`Quotinator_` — deliberately distinct from ADR 015's existing four so a reader isn't misled
into thinking these are part of the audit-trail/import-provenance/product-domain concerns those already
cover.

## Design details

**The generic extension point:** `DatabaseInitializer` gains one new protected virtual hook —

```csharp
protected virtual Task SeedSystemContentAsync(SqliteConnection connection) => Task.CompletedTask;
```

— a no-op by default, exactly like the existing `OnResetAsync`/`OnReseedAsync` hooks. It is invoked from
two places inside `DatabaseInitializer` itself (not left to each subclass to remember):

- At the end of `ApplyBaselineAsync`, after the baseline DDL and version rows commit (fresh-database path).
- At the end of `DropAndRebuildAsync`, after the schema rebuild succeeds (today's incremental-replay
  Reset implementation — the same call site this issue's own future baseline-based Reset rewrite will
  keep, since both paths fundamentally "rebuild the schema, then reseed system content").

This guarantees system content is (re)populated on both a first-ever install and every Reset, with no
per-subclass branch needed — matching the original issue's own "no separate 'is this a system table'
branch is needed" intent, just realized as a hook invocation instead of static baseline rows.

**Why this is scoped as *only* the extension point + proof, not the full Reset rewrite yet:** the
issue's two original proposed changes (baseline-based drop/rebuild; removing the automatic
`SeedIfEmptyInternalAsync` call) are a separate, larger, behaviour-visible change requiring full T1/T2
live verification and rewriting the existing `ResetAsync_AfterInitialise_PreservesExistingAuditEntries`-style
tests. Landing the extension point first, proven inert (no-op, zero behaviour change) and correct via
real-SQLite integration tests, de-risks that follow-up — it can be implemented and reviewed as "wire an
already-proven mechanism into the real Reset path," not "design and prove a new mechanism and change
Reset's behaviour in the same commit."

**File-driven system content loading is explicitly deferred, not part of this step.** The two dummy
fixtures seed via a simple test-provided override (a hardcoded row), not by parsing a real file from
`data/system/`. Building a full loader (mirroring the `IQuoteSourceConverter` plugin pattern used for
standard content) is real engineering with no concrete consumer yet — same pragmatism the original issue
applied to baseline rows ("today, no such tables exist"). `data/system/` is created now as the agreed
structural home for that future work, not populated by this step.

## Steps

### Step 1 — `SeedSystemContentAsync` extension point (Quotinator.Data, production)
**Status:** ✅ Done
Add the no-op virtual hook to `DatabaseInitializer` and call it from `ApplyBaselineAsync` and
`DropAndRebuildAsync`. No behaviour change for any existing caller (default no-op).

### Step 2 — `data/system/` directory
**Status:** ✅ Done
Create `data/system/` (empty, with a short `README.md` explaining its purpose and pointing at this plan
doc) as a sibling to `data/sources/`. Add to `Quotinator.slnx`.

### Step 3 — `SystemContent_` dummy fixture (Quotinator.Data.Tests)
**Status:** ✅ Done
`tests/Quotinator.Data.Tests/Database/SystemReseedConceptTests.cs` — a test-only
`SystemContent_ExampleSetting` table (ad-hoc `SchemaMigration` + `SchemaBaseline`, never added to
`DataOwnedMigrations`/`DataBaselineSql`) and a minimal test-only `DatabaseInitializer` subclass
(`SystemContentTestInitializer`, mirroring `DatabaseInitializerOwnershipTests.ResettableTestInitializer`)
overriding `SeedSystemContentAsync`. Three real-SQLite integration tests, all passing:
- `SeedSystemContentAsync_AfterFreshInitialise_PopulatesSystemContentTable`
- `SeedSystemContentAsync_AfterReset_RepopulatesSystemContentTable`
- `ReseedEquivalentCall_DoesNotInvokeSeedSystemContentAsync`

### Step 4 — `UserContent_` dummy fixture (Quotinator.Core.Tests)
**Status:** ✅ Done
`tests/Quotinator.Core.Tests/Database/UserSystemReseedConceptTests.cs` — same proof, from the consumer
side: a test-only `UserContent_ExampleWidget` table supplied via an ad-hoc `SchemaMigration`/
`SchemaBaseline` (never added to `QuotinatorMigrations.All`/`QuotinatorMigrations.Baseline`), with the
same `SeedSystemContentAsync` override pattern. Three real-SQLite integration tests, all passing:
- `SeedSystemContentAsync_AfterFreshInitialise_PopulatesUserContentTable`
- `SeedSystemContentAsync_AfterReset_RepopulatesUserContentTable`
- `ReseedEquivalentCall_DoesNotInvokeSeedSystemContentAsync`

### Step 5 — Reset behaviour rewrite (the original issue's own two proposed changes)
**Status:** ✅ Done
1. `DropAndRebuildAsync` now drops the *entire* database (`Sql.Schema.GetAllTables`, no exclusion of
   any kind — `Sql.Schema.GetUserTables` is retired) and recreates it via `ApplyMigrationsAsync`,
   which — once the DB reads as genuinely empty — takes the same baseline path a fresh install uses
   whenever a baseline is configured (Quotinator always configures one). `System_`/`Import_`/`Audit_`-
   prefixed tables, including the audit trail, no longer survive Reset — the deliberate tradeoff
   ADR 014/#249 already accounts for. `preserveSchemaVersion=true` now snapshots and restores **both**
   `System_SchemaVersion`'s and `System_ConsumerSchemaVersion`'s granular per-version rows (previously
   only the consumer's — Data's own was never touched pre-#156, so there was nothing to preserve).
   `SeedSystemContentAsync` fires exactly once per Reset: `ApplyBaselineAsync` already calls it
   internally once the DB is empty and a baseline is configured, so `DropAndRebuildAsync` only calls it
   directly when `ApplyMigrationsAsync` reports it did *not* take the baseline path (`tookBaselinePath`),
   avoiding a double-invocation for the real Quotinator scenario.
2. Removed the `SeedIfEmptyInternalAsync` call from `QuotinatorDatabaseInitializer.OnResetAsync`.
   `ResolveEffectiveBatchesAsync(forceSourceRefresh)` is still called for its on-disk source-cache-
   refresh side effect (a disk-level concern independent of database content, outside the Single
   Responsibility policy's scope) — its returned batches are now discarded, never imported. Also fixed
   a related staleness bug found live during T2: `LastSeedReport` was never cleared by Reset, so the
   response kept echoing whatever the *previous* real seed/reseed had reported, misleadingly implying
   the Reset call itself had imported something — now explicitly reset to `[]`.
3. Rewrote `ResetAsync_AfterInitialise_PreservesExistingAuditEntries` →
   `ResetAsync_AfterInitialise_WipesExistingAuditEntries` (asserts the opposite),
   `ResetAsync_AfterInitialise_RebuildsSchemaAndReseeds` → `...AndDoesNotReseed`, replaced
   `ResetAsync_AnyParameter_NeverTouchesDataSchemaVersion` with symmetric
   `ResetAsync_DefaultParameter_AlsoReplaysDataSchemaVersion`/
   `ResetAsync_PreserveSchemaVersionTrue_AlsoKeepsExistingDataVersionRows`, retired the two
   `GetUserTables_*` tests in favour of `GetAllTables_ReturnsEveryTableRegardlessOfPrefix`, and rewrote
   `ConflictResolutionTests.ResetAsync_PreservesExistingChangeLogRows` →
   `ResetAsync_WipesExistingChangeLogRowsAndDoesNotReseed`.
4. #141's outcome ("Reseed/reset must preserve System-classified data") is directly superseded by this
   step — needs a developer decision on whether/how to close it (not done here; GitHub issue actions
   require explicit permission).

### Step 6 — Docs sync + full verification
**Status:** ✅ Done
Updated CLAUDE.md's "No exception-based migration recovery" and "Audit-trail tables never purge
dangling references" sections (both described the pre-#156 preserve-on-reset behaviour as current
fact — now describe the full-wipe baseline rebuild, and the stale `System_AuditEntries`/
`System_ImportConflicts`/`System_ImportActions`/`System_ChangeLog` names were corrected to their
actual post-#253/#254 names in the same edit). Updated the `POST /admin/database/reset` endpoint's own
`[Description]` and `docs/api-endpoints.md`'s matching row (per the "Keeping API documentation in
sync" rule). Updated `docs/smoke-tests.md` (now `docs/automated-testing/`, whose README maps the old
section numbers): fixed two now-wrong expectations in existing sections
(#221's report-shape check, #254's degraded-state-recovery check both assumed Reset reseeds), and
added a new §32 dedicated to #156. Full `dotnet build`/`dotnet test` — 0 warnings, 0 errors, 0 test
failures. T2 (Docker) run end-to-end against §32's own checklist, including a real discrepancy found
and fixed live (the `LastSeedReport` staleness bug above, and the audit `totalCount` being `1` not
`0` after Reset — its own self-trace row, not a bug). T1 (Visual Studio) is the developer's own
action per this project's standing convention — not run here.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `SeedSystemContentAsync` hook exists, no-op by default, called from `ApplyBaselineAsync` and `DropAndRebuildAsync` | Build | `dotnet build --configuration Release` — 0 warnings, 0 errors; full `dotnet test` — 609/609 passed |
| 2 | ✅ | `data/system/` directory exists, documented, in `Quotinator.slnx` | Manual | `data/system/README.md` present; `Quotinator.slnx` lists it |
| 3 | ✅ | System content survives a fresh baseline-path initialise | Unit test | `SystemReseedConceptTests.SeedSystemContentAsync_AfterFreshInitialise_PopulatesSystemContentTable` |
| 4 | ✅ | System content is (re)populated after `ResetAsync` | Unit test | `SystemReseedConceptTests.SeedSystemContentAsync_AfterReset_RepopulatesSystemContentTable` |
| 5 | ✅ | Standard-reseed-equivalent call does not touch system content | Unit test | `SystemReseedConceptTests.ReseedEquivalentCall_DoesNotInvokeSeedSystemContentAsync` |
| 6 | ✅ | A downstream consumer (not just Quotinator.Data itself) can register system content via the same extension point | Unit test | `UserSystemReseedConceptTests.SeedSystemContentAsync_AfterFreshInitialise_PopulatesUserContentTable` + siblings |
| 7 | ✅ | Reset drops the entire database and rebuilds via baseline | Unit test + Live | `ResetAsync_AfterInitialise_WipesExistingAuditEntries`, `ResetAsync_WipesExistingChangeLogRowsAndDoesNotReseed`, `GetAllTables_ReturnsEveryTableRegardlessOfPrefix`; live Docker: audit `totalCount` 32→1 (self-trace only), quotes 799→0 |
| 8 | ✅ | Reset no longer reseeds standard (bundled/user) content | Unit test + Live | `ResetAsync_AfterInitialise_RebuildsSchemaAndDoesNotReseed`; live Docker: `POST /admin/database/reset` returns all-zero counts, `reports:[]`, `/quotes/random` → `200 NoResults` |
| 9 | ✅ | `ResetAsync_AfterInitialise_PreservesExistingAuditEntries` rewritten to assert the opposite | Unit test | `ResetAsync_AfterInitialise_WipesExistingAuditEntries` |
| 10 | ✅ | T1 (Visual Studio, developer) | Live | Developer's own VS run, 2026-08-06: reset requested → rebuilding schema from baseline → stats all zero → reset complete, `200` |
| 11 | ✅ | T2 (Docker) | Live | Full smoke-test §32 sequence run against `quotinator:local`; found and fixed the `LastSeedReport` staleness bug live |

---

## Relationship to existing issues

- **#141** ("Reseed/reset must preserve System-classified data") is **not** fully superseded by this
  issue — corrected 2026-08-06 after developer feedback. Three separate concerns, not one:
  1. **Audit trail surviving Reset** — genuinely reversed by Step 5. Resolved.
  2. **Content vital for the app to function** (the deeper concern #141 was really pointing at) —
     unresolved, not superseded. `SeedSystemContentAsync` (this issue) provides the *mechanism*, but
     nothing real exercises it yet — only the `SystemContent_`/`UserContent_` test-only dummies do.
     [#268](https://github.com/DutchJaFO/Quotinator/issues/268) (Data Enrichment milestone,
     genre-as-data) is the first real candidate to actually prove this in production.
  3. **Optional pure data** (quotes) — never really #141's concern; Reset correctly drops this and
     does not restore it, unaffected by this correction.

  Not closed or commented on here — GitHub issue actions require explicit developer permission
  (standing project convention); the developer should decide how to dispose of #141 once #268 (or an
  equivalent real vital-content table) actually exists and is verified, not before.
- **#151** / [ADR 014](../architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md)
  — dangling references within a surviving audit-trail table are permanent by design; ADR 014 also
  confirms this issue's Step 5 makes the audit trail stop surviving Reset entirely, which is what makes
  #249 (done) a release gate on this issue rather than an implementation-order dependency.
- **#227** / ADR 015 — the domain-prefix convention this plan doc's two new prefixes (`SystemContent_`,
  `UserContent_`) follow, and the source of the `GetUserTables` exclusion-pattern note this issue's own
  body already flagged as needing revisiting (resolved separately by #253/#254; Step 5 removes the
  exclusion-list approach entirely rather than extending it further).
