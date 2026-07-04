# #141 — System table preservation on Reset (System_AuditEntries, System_SchemaVersion)

**Status:** Fully verified (T1+T2), pending release (as of 2026-07-03). See "Scope amendment" for the `System_`-prefix rework and rows 22–23 for verification detail.
**GitHub issue:** #141
**Tiers required:** T1, T2 — unchanged reasoning from the original pass (this still changes `DatabaseInitializer`/`Sql.Schema.GetUserTables`, the actual table-wipe logic behind `Reset`). T3 still does not apply.
**Depends on:** #62 (`ImportBatchType` accuracy fix) — done, unblocked this issue

---

## Scope narrowed from the original issue text (record before implementing)

The GitHub issue as filed proposed a general "table-level classification mechanism... System-prefixed table names... or a metadata table" to preserve everything `ImportBatchType.System` represents, including curated rows inside `Quotes`/`Sources`/`Characters`/`People`. Cross-checking the spec against the current code (mandatory step per `process.md`) surfaced two problems with that framing, both resolved with the user before this plan was written:

1. **The issue's premise about `AuditEntries` was wrong.** The issue states `AuditEntries` "is not wiped by reseed/reset today" and treats its own admin-clear endpoint (`DELETE /api/v1/admin/audit`) as sufficient prior art. That is true only for **Reseed** (`TruncateDataAsync` never touches it). It is false for full **Reset** (`DropAndRebuildAsync`) — that method drops every table returned by `Sql.Schema.GetUserTables`, which today excludes only `SchemaVersion`. `AuditEntries` (created by migration 4) is a normal user table with no exclusion, so a full Reset today silently destroys the entire audit trail. **User decision: bring this fix into #141's scope** — it is the actual bug the issue exists to prevent, just for a table the issue's author didn't realise was affected.

2. **`ImportBatchType.System` is a row-level tag, not a table-level one**, and does not need table-level protection. `Quotes`/`Sources`/`Characters`/`People` each mix `System` rows (curated data) together with `Seed`/`UserSeed`/`Import` rows in the same table — a whole-table exemption can protect one classification only by protecting all of them, which is not the goal. Curated content is re-seeded from `data/sources/quotinator-curated.json` on every reseed/reset anyway, so it never actually needs protecting from data loss. **User decision: scope #141 to genuinely whole-table system tables only (`AuditEntries`, `SchemaVersion`) — no row-level filtering is added to the four entity tables.**

3. **`SchemaVersion`'s open question is resolved as: keep default behaviour, make it optional.** Reset continues to clear and replay `SchemaVersion` by default — unchanged from today's documented behaviour ("Clears all data and schema version history, reapplies all migrations from scratch"). A new opt-in parameter lets a caller preserve it instead.

A comment documenting this scope narrowing will be posted on #141 before work starts, per `process.md`'s deferral rule — the GitHub issue's "table-level classification mechanism... System-prefixed table names" language no longer matches what will actually ship.

---

## Scope amendment: hardcoded exclusion list replaced with a naming convention (2026-07-02, same day)

Before release, the user reviewed the mechanism above and rejected it — the hardcoded `NOT IN ('SchemaVersion', 'AuditEntries')` list in `Sql.Schema.GetUserTables` makes `Quotinator.Data` (the generic, domain-agnostic layer) aware of two specific table names that belong to a consuming project's feature (the audit trail). The user's design intent, verbatim:

> "The original concept was that System-tables would be easy to identify by users. It makes the exclusion list easier to fill as Data project need not know about system tables that Quotinator and other projects would want to define. The concept behind system tables is that these are considered part of the database as they contain meta-data essential for the app that needs to survive reseeding and resets. An example would be if we wanted to store an enum-like data set in a table. We wouldn't have to recompile the app to change the list as we would with a regular enum. However a reset would also empty those tables, which is what we would not want. Regular content is provided by seeding."

Two design questions were resolved before implementing:

1. **Can SQLite support a real `dbo.TableName`-style namespace?** No — checked against sqlite.org docs: `schema-name.table-name` only ever resolves to `main`, `temp`, or an `ATTACH`ed separate database file, never a logical namespace within one file. A prefix on the table name itself is the only option.
2. **Table-level prefix: `System_TableName` (underscore) or `SystemTableName` (concatenated)?** The user chose `System_` with a literal underscore, mirroring SQLite's own `sqlite_` prefix convention for its internal tables — and specifically so a future table like `SystemInventory` (starts with "System" but has no underscore) is never accidentally treated as protected. This requires an `ESCAPE` clause: SQL `LIKE` treats `_` as a single-character wildcard, so an unescaped `'System_%'` would also match `SystemInventory`. Checked against sqlite.org docs: `LIKE 'System\_%' ESCAPE '\'` matches only a literal underscore. **C# class names** use the concatenated form (`SystemAuditEntry`, `SystemSchemaVersion`) — normal PascalCase, no underscore.

Renamed at both the SQL and C# level:

| Old (SQL table / C# type) | New |
|---|---|
| `SchemaVersion` | `System_SchemaVersion` |
| `AuditEntries` | `System_AuditEntries` |
| `AuditEntry` (entity) | `SystemAuditEntry` |
| `IAuditReader` / `AuditReader` | `ISystemAuditReader` / `SystemAuditReader` |
| `IAuditWriter` / `AuditWriter` | `ISystemAuditWriter` / `SystemAuditWriter` |
| `AuditPageResult` | `SystemAuditPageResult` |
| `NoOpAuditReader` / `NoOpAuditWriter` | `NoOpSystemAuditReader` / `NoOpSystemAuditWriter` |
| `Sql.Audit` (nested class) | `Sql.SystemAudit` |

`AuditMigrations` (the class/file holding migration SQL) was deliberately **not** renamed — it holds migration004's frozen, historically-accurate original SQL (which genuinely creates a table named `AuditEntries`) alongside the new migration006 rename SQL; renaming the container added risk for no benefit.

`SchemaVersion` is bootstrapped directly in code before any numbered migration runs (the migration engine's own version-tracking table), so its rename is a conditional bootstrap check in `DatabaseInitializer.ApplyMigrationsAsync` — not a numbered migration: if a table literally named `SchemaVersion` exists in `sqlite_master`, it is renamed; a fresh database has no such table, so `CREATE TABLE IF NOT EXISTS System_SchemaVersion` runs directly with zero detour. `AuditEntries` was created by an already-applied migration004 (frozen, not edited), so its rename is a new migration006 (`ALTER TABLE AuditEntries RENAME TO System_AuditEntries` + index recreation under new names).

**Bug found during this amendment, fixed:** `System_AuditEntries` surviving the Reset table wipe (by design) collides with `SchemaVersion`'s default wipe-and-replay behaviour. On a default `Reset`, `System_SchemaVersion`'s rows are cleared, so `ApplyMigrationsAsync` reads `current = 0` and replays every migration from scratch — migration004's `CREATE TABLE IF NOT EXISTS AuditEntries` recreates a stray empty legacy-named table (harmless on its own), but migration006's `ALTER TABLE AuditEntries RENAME TO System_AuditEntries` then fails because the destination — the real, preserved `System_AuditEntries` — already exists (`SQLite Error 1: 'there is already another table or index with this name: System_AuditEntries'`). Fixed by extending `IsKnownMigrationError`'s existing recovery pattern (already used for migration003's ALTER TABLE ADD COLUMN non-idempotency) to also recognise this "already exists" case for migration006, and — specifically for that case — dropping the stray empty `AuditEntries` duplicate (`Sql.SystemAudit.DropStrayLegacyAuditEntriesTable`) before recording the version as applied.

---

## Spec requirements (as amended)

1. `System_AuditEntries` survives a full `Reset` — table-level protection via `Sql.Schema.GetUserTables`'s generic, escaped `System\_%` pattern match (no hardcoded names)
2. `System_SchemaVersion` continues to be cleared and replayed on `Reset` **by default** — no change to documented behaviour
3. A new opt-in parameter (`ResetAsync(bool preserveSchemaVersion = false)`) lets a caller skip the `System_SchemaVersion` clear + migration replay, preserving existing version history instead
4. `POST /api/v1/admin/database/reset` exposes the new option via a `preserveSchemaVersion` query parameter, default `false`
5. `Reseed` is unaffected — `TruncateDataAsync` already leaves both tables untouched
6. `README.md`, `addon/DOCS.md`, and the endpoint's `WithDescription` text updated to reflect the new behaviour and parameter
7. Any table a consuming project names with a literal `System_` prefix is automatically protected from the `Reset` table wipe, with zero changes required in `Quotinator.Data`
8. A pre-existing database with the legacy `SchemaVersion`/`AuditEntries` names is upgraded transparently — no manual migration step, no data loss, no orphaned tables left behind
9. A fresh database creates `System_SchemaVersion`/`System_AuditEntries` directly — never created under the legacy name and then renamed

---

## Step status

1. [x] `Sql.Schema.GetUserTables` excludes `AuditEntries` in addition to `SchemaVersion` *(superseded — see below)*
2. [x] `IDatabaseInitializer.ResetAsync` gains `bool preserveSchemaVersion = false` parameter
3. [x] `DatabaseInitializer.DropAndRebuildAsync` accepts and honours `preserveSchemaVersion`
4. [x] `QuotinatorDatabaseInitializer.OnResetAsync` threads the parameter through
5. [x] `POST /api/v1/admin/database/reset` accepts `?preserveSchemaVersion=true`
6. [x] `README.md`, `addon/DOCS.md`, and endpoint `WithDescription` text updated
7. [x] Unit tests added and green
8. [x] Scope-narrowing comment posted on #141
9. [x] Unreleased changelog entry added
10. [x] `Sql.Schema.GetUserTables` generalised to an escaped `System\_%` pattern match (no hardcoded names)
11. [x] `SchemaVersion`/`AuditEntries` renamed to `System_SchemaVersion`/`System_AuditEntries` at the SQL level
12. [x] `AuditEntry`, `IAuditReader`/`AuditReader`, `IAuditWriter`/`AuditWriter`, `AuditPageResult`, `NoOpAuditReader`/`NoOpAuditWriter`, `Sql.Audit` renamed to their `System*` equivalents
13. [x] Conditional bootstrap rename for legacy `SchemaVersion` added to `DatabaseInitializer.ApplyMigrationsAsync`
14. [x] Migration006 added (`AuditMigrations.RenameAuditEntriesToSystemAuditEntries`), registered in `QuotinatorMigrations.All`
15. [x] `IsKnownMigrationError` extended for migration006's two recoverable failure modes; stray-table cleanup added
16. [x] New unit tests: generic `System_` exclusion (positive + negative/escape-clause proof), fresh-database no-detour, legacy rename path, migration006 row/index preservation
17. [x] All existing tests updated to new table/type names; full suite green, 0 warnings
18. [x] T1 (VS/local, real running app with existing legacy-named database) — verified 2026-07-02, including live confirmation of the Reset collision fix
19. [x] T2 (Docker) — verified 2026-07-03, see row 23
20. [x] Amendment comment posted on #141
21. [x] Unreleased changelog entry updated to describe final shipped behaviour

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Full Reset preserves existing `AuditEntries` rows | Unit test | `DatabaseInitializerTests.ResetAsync_AfterInitialise_PreservesExistingAuditEntries` |
| 2 | ✅ | Full Reset with default parameter still clears and replays `SchemaVersion` (unchanged behaviour) | Unit test | `DatabaseInitializerTests.ResetAsync_DefaultParameter_StillReplaysSchemaVersion` |
| 3 | ✅ | Full Reset with `preserveSchemaVersion: true` leaves existing `SchemaVersion` rows untouched | Unit test | `DatabaseInitializerTests.ResetAsync_PreserveSchemaVersionTrue_KeepsExistingRows` |
| 4 | ✅ | `POST /api/v1/admin/database/reset?preserveSchemaVersion=true` threads the flag through and returns 200 | Unit test | `AdminEndpointsTests.ResetDatabase_PreserveSchemaVersionTrue_Returns200AndPassesFlagThrough` |
| 5 | ✅ | `POST /api/v1/admin/database/reset` with no query param defaults to `false` and behaves exactly as before (regression) | Unit test | `AdminEndpointsTests.ResetDatabase_NoQueryParam_DefaultsPreserveSchemaVersionFalse`, `ResetDatabase_CorrectKey_Returns200WithStatsShape` (existing test, still green) |
| 6 | ✅ | Reseed (not Reset) leaves both `AuditEntries` and `SchemaVersion` untouched | Unit test | `DatabaseInitializerTests.ReseedAsync_AfterInitialise_LeavesAuditEntriesAndSchemaVersionUntouched` (new — makes previously-implicit behaviour explicit) |
| 7 | ✅ | `README.md` admin endpoints table describes the new parameter and `AuditEntries` preservation | Code review | `README.md` "Admin Endpoints" table row for `database/reset` |
| 8 | ✅ | `addon/DOCS.md` admin endpoints table matches `README.md` | Code review | `addon/DOCS.md` "Admin Endpoints" table row for `database/reset` |
| 9 | ✅ | Build clean | Live | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s). Confirmed 2026-07-02 |
| 10 | ✅ | All tests pass | Live | `dotnet test --configuration Release --verbosity normal` — 609/609 passed across all test projects, 0 warnings. Confirmed 2026-07-02 |
| 11 | ✅ | Malformed `preserveSchemaVersion` value doesn't crash — returns structured error | Live | `POST /api/v1/admin/database/reset?preserveSchemaVersion=notabool` → `422` with an RFC 7807 `Problem` body (verified manually 2026-07-02; see Notes) |
| 12 | ✅ | T1: against a real running app with an existing non-empty database, `POST /api/v1/admin/database/reset` (default) fully rebuilds the schema and reseeds correctly, and `AuditEntries` rows survive | Live | `dotnet run --project src/Quotinator.Api` against a persistent `Quotinator__DataDir` (not a unit-test temp file). Baseline: 6 `AuditEntries` rows, `SchemaVersion` all `13:33:19`, 788 quotes. After default reset: `AuditEntries` grew to 13 (proving the original 6 survived, not just new ones), `SchemaVersion` timestamps all moved to `13:34:02` (confirming default clear+replay is unchanged), 788 quotes rebuilt correctly. Verified 2026-07-02 via `Quotinator.Tools.DbInspector` against the real `quotinatordata.db` file. |
| 13 | ✅ | T1: `preserveSchemaVersion=true` against the same real running app leaves `SchemaVersion` untouched while still fully rebuilding all data tables | Live | Same run, immediately after row 12: called reset again with `?preserveSchemaVersion=true`. `SchemaVersion` timestamps stayed exactly `13:34:02` (unchanged from the prior reset — proving preservation), `AuditEntries` kept growing (13 → 20), 788 quotes rebuilt again, `/api/v1/health`/`/api/v1/version`/`/api/v1/quotes/random` all returned correct data. Verified 2026-07-02. |
| 14 | ⚠️ | T2: fresh Docker container builds and reset behaves identically to T1 | Live | **Blocked in this session** — the Docker daemon cannot start in this sandboxed cloud environment (`sudo service docker start` fails: `ulimit: error setting limit (Operation not permitted)`, confirmed even with the sandbox override). `docker build -f docker/Dockerfile -t quotinator:local .` could not be attempted. This is an environment limitation, not a code gap — T1 already exercises the exact same `DropAndRebuildAsync`/`Sql.Schema.GetUserTables` code path that runs in the container. T2 must be completed in an environment with a working Docker daemon (local developer machine or CI) before this issue can close — do not treat T1 alone as satisfying the T2 requirement. |

### Rows 15–21: amendment (System_-prefix naming convention)

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 15 | ✅ | `GetUserTables` excludes any `System_`-prefixed table, not just the two known names | Unit test | `DatabaseInitializerTests.GetUserTables_SystemPrefixedTable_IsExcluded` (creates a throwaway `System_FooBar` table and confirms exclusion) |
| 16 | ✅ | A table starting with "System" but no underscore (e.g. `SystemInventory`) is NOT treated as protected — proves the `ESCAPE` clause works | Unit test | `DatabaseInitializerTests.GetUserTables_SystemPrefixWithoutUnderscore_IsNotExcluded` |
| 17 | ✅ | A fresh database creates `System_SchemaVersion` directly, never under the legacy name | Unit test | `DatabaseInitializerTests.InitialiseAsync_FreshDatabase_CreatesSystemSchemaVersionDirectly` |
| 18 | ✅ | A pre-existing legacy `SchemaVersion` table is renamed to `System_SchemaVersion` with all version history rows preserved | Unit test | `DatabaseInitializerTests.InitialiseAsync_LegacySchemaVersionTable_IsRenamedWithRowsPreserved` |
| 19 | ✅ | Migration006 renames `AuditEntries` to `System_AuditEntries`, preserving existing rows and both indexes under their new names | Unit test | `DatabaseInitializerTests.InitialiseAsync_LegacyAuditEntriesTable_MigratesToSystemAuditEntriesWithRowsPreserved` |
| 20 | ✅ | A default `Reset` (which wipes and replays `System_SchemaVersion` while `System_AuditEntries` survives, by design) does not crash on the resulting migration004/migration006 collision | Unit test | `DatabaseInitializerTests.ResetAsync_AfterInitialise_RebuildsSchemaAndReseeds`, `ResetAsync_AfterInitialise_PreservesExistingAuditEntries`, `ResetAsync_DefaultParameter_StillReplaysSchemaVersion`, `ResetAsync_PreserveSchemaVersionTrue_KeepsExistingRows` — all previously crashed with `SQLite Error 1: 'there is already another table or index with this name: System_AuditEntries'` until `IsKnownMigrationError`/stray-table cleanup was added; all now green |
| 21 | ✅ | Build clean and full test suite green after the amendment | Live | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s); `dotnet test --configuration Release --verbosity normal` — 617/617 passed across all 6 test projects, 0 warnings. Confirmed 2026-07-02 |
| 22 | ✅ | T1 (amendment): real running app in Visual Studio, existing database with legacy `SchemaVersion`/`AuditEntries` names, confirms transparent upgrade AND the Reset collision fix | Live | Verified 2026-07-02 by the user. Run 1 (`dotnet run`, existing v5 database): log showed `applying 1 pending migration(s) (version 5 → 6)`, `schema updated at version 6`, stats unchanged (788 quotes/478 sources/2 characters). DB inspection confirmed exactly 12 tables — `System_AuditEntries`/`System_SchemaVersion` present, no `AuditEntries`/`SchemaVersion` remaining, indexes correctly renamed to `IX_System_AuditEntries_TableName_RecordId`/`IX_System_AuditEntries_PerformedAt`; `System_SchemaVersion` had all 6 version rows. `GET /api/v1/admin/audit` → 200, `DELETE /api/v1/admin/audit` (no key) → 401 (auth gate unaffected). Run 2 (fresh start, `schema is up to date at version 6`): `POST /api/v1/admin/database/reseed` → 200 (788 quotes rebuilt). `POST /api/v1/admin/database/reset` (default) → **exercised the exact migration004/migration006 collision the fix targets** — log showed the recovery path firing (`SqliteException` caught, recorded version, continued) and `reset complete` → 200, 788 quotes rebuilt correctly. `POST /api/v1/admin/database/reset?preserveSchemaVersion=true` → same successful recovery path, 200. **Follow-up fix from this run:** the recovery log message was misleadingly worded ("migration 6 was previously partially applied... use Reset Database" — confusing when it fires *during* a Reset, and it is not actually rare, it is the expected path on every default Reset). Split into a distinct, accurate `LogInformation` message for this specific case; the true partial-apply case (migration003, and a genuinely interrupted migration006) still logs the original `LogWarning`. Full suite re-verified green (617/617) after the message fix. |
| 23 | ✅ | T2 (amendment): fresh Docker container, confirms identical behaviour | Live | Confirmed 2026-07-03 — `docker build -f docker/Dockerfile -t quotinator:local .`, fresh container, no pre-existing volume. Fresh-DB baseline created `System_SchemaVersion`/`System_AuditEntries` directly (log: `fresh database detected — creating schema directly at baseline`). `System_AuditEntries` grew monotonically across a reseed and two resets (6 → 13 → 20 → 34 rows via `GET /api/v1/admin/audit`), never wiped. `System_ConsumerSchemaVersion` verified via `Quotinator.Tools.DbInspector` against the container's DB file (copied out with `docker cp`): unchanged (`AppliedAt=21:38:53`) across a `preserveSchemaVersion=true` reset, then updated to new timestamps (`21:39:40`) on the very next default reset — confirming the flag's effect in both directions. `System_SchemaVersion` stayed at its single original row throughout, unaffected by any reset (per #143's amendment). No `Exception thrown:` at any point. |

---

## Notes

- No new migration is needed. Excluding `AuditEntries` from `GetUserTables` is a query change in `Sql.cs`, not DDL — `DropAndRebuildAsync` simply stops dropping the table, and the `CREATE TABLE IF NOT EXISTS` in migration 4 becomes a no-op for it on replay, which is already idempotent by design.
- `preserveSchemaVersion` is declared as a plain `bool preserveSchemaVersion = false`. Initially planned to mirror the `string?` + `TryParse` pattern from `CLAUDE.md`'s "Year parameter binding pattern," but re-checked against the current codebase (`src/Quotinator.Api/Middleware/BadRequestExceptionHandler.cs`) and found that pattern is now superseded by a global `IExceptionHandler` registered ahead of `AddProblemDetails()`: it catches any `BadHttpRequestException` from parameter binding — not just the year params — and returns a proper localized 422 `Problem`. Its own doc comment confirms the year-param `TryParseYear` path exists only for the specific case that predates this handler; "this handler fires only for unexpected binding failures on other parameter types," which is exactly this case. No new `ApiMessages` key or translation entries needed.

**Bug found and fixed during this work:** the first implementation of `preserveSchemaVersion:true` simply skipped `Sql.Schema.DeleteAll` before `DropAllTablesAsync`/`ApplyMigrationsAsync`. `ResetAsync_PreserveSchemaVersionTrue_KeepsExistingRows` immediately caught the consequence: `DropAllTablesAsync` still drops every data table unconditionally, but with `SchemaVersion`'s rows untouched, `ApplyMigrationsAsync` saw `current >= _migrations.Count` and took its "already up to date" early return — skipping the `CREATE TABLE` DDL replay entirely and leaving the database with no `Quotes` table at all (`SQLite Error 1: 'no such table: Quotes'` on the immediate reseed attempt). Fixed by decoupling the two concerns: `DropAndRebuildAsync` now always runs the full, already-tested clear+rebuild path unconditionally (so every table always comes back), and only when `preserveSchemaVersion` is `true` does it snapshot `SchemaVersion`'s rows beforehand and restore that exact snapshot afterward — the rebuild itself is untouched, only the final `SchemaVersion` row content is swapped back. This avoids touching `ApplyMigrationsAsync`/`ApplyPendingMigrationsAsync` at all, keeping the well-tested migration-replay logic frozen.

**Message-accuracy drift found, not fixed here (out of scope):** the shared `BadRequestExceptionHandler` returns `localizer[ApiMessages.NumericParameterInvalid]` for *any* unparsable primitive query parameter, including this new boolean one. Its English text reads "Numeric parameters (yearFrom, yearTo, year, decade, page, pageSize, n) must be whole numbers." — accurate for the year params it was written for, slightly misleading for a malformed `preserveSchemaVersion` value (it *is* a structurally correct 422 `Problem`, just with wording that doesn't mention booleans). Not fixed as part of #141 since it is a pre-existing, shared message used well beyond this endpoint; worth a small follow-up if it comes up again.

**Amendment notes (2026-07-02, naming-convention pass):**
- The migration004/migration006 collision described in "Scope amendment" above was only discoverable once `System_AuditEntries` became protected by the generic `System_%` pattern — the original hardcoded-exclusion-list version of #141 never hit it, because `AuditEntries` (old name) itself was the excluded/protected table, and migration004's `CREATE TABLE IF NOT EXISTS AuditEntries` is naturally idempotent against its own already-existing table (no rename step existed). The bug is specific to introducing a *rename* migration whose target becomes newly protected.
- `SqlQueryGuardTests.AggregateQueries_MatchDocumentedInventory` required updating: `Sql.Audit.CountPagedBase` → `Sql.SystemAudit.CountPagedBase`, and the new `Schema.LegacySchemaVersionExists` constant (`COUNT(*)`, no GROUP BY/HAVING) added to the documented aggregate-query inventory.
- `RepositorySqlGuardTests.cs` (in `Quotinator.Data.Tests`) also referenced `Sql.Audit.*` by name and required the same rename.
