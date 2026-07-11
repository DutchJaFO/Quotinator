# #141 — System table preservation on Reset (AuditEntries, SchemaVersion)

**Status:** All spec requirements resolved in code, all unit tests green, **T1 verified** 2026-07-02 (real running app, existing non-empty database, both `preserveSchemaVersion` values). **T2 blocked** — this cloud session has no working Docker daemon; needs a local machine or CI to complete. Issue cannot close until T2 is verified and the change ships in a release.
**GitHub issue:** #141
**Tiers required:** T1, T2 — corrected 2026-07-02 (user disagreed with the initial "none required" call). This issue changes `DatabaseInitializer.DropAndRebuildAsync` and `Sql.Schema.GetUserTables` — the actual table-wipe logic behind `Reset` — which `docs/release-verification.md`'s T1/T2 criteria now explicitly call out as a trigger regardless of whether `.razor`/Dockerfile/`Program.cs` are touched. Reasoning: unit tests run against a fresh temp SQLite file every time and already caught one real bug during this work (`preserveSchemaVersion:true` silently dropping every data table without recreating them) — a second, independent verification pass against a real running app (T1) and a fresh container (T2) is warranted precisely because the failure mode is "the database ends up structurally broken," which unit tests can miss if the test fixture doesn't happen to mirror a real deployment's state. T3 still does not apply — no ingress/`X-Ingress-Path`/DataProtection/SSL/`addon/config.yaml`/log-format surface touched.
**Depends on:** #62 (`ImportBatchType` accuracy fix) — done, unblocked this issue

---

## Scope narrowed from the original issue text (record before implementing)

The GitHub issue as filed proposed a general "table-level classification mechanism... System-prefixed table names... or a metadata table" to preserve everything `ImportBatchType.System` represents, including curated rows inside `Quotes`/`Sources`/`Characters`/`People`. Cross-checking the spec against the current code (mandatory step per `process.md`) surfaced two problems with that framing, both resolved with the user before this plan was written:

1. **The issue's premise about `AuditEntries` was wrong.** The issue states `AuditEntries` "is not wiped by reseed/reset today" and treats its own admin-clear endpoint (`DELETE /api/v1/admin/audit`) as sufficient prior art. That is true only for **Reseed** (`TruncateDataAsync` never touches it). It is false for full **Reset** (`DropAndRebuildAsync`) — that method drops every table returned by `Sql.Schema.GetUserTables`, which today excludes only `SchemaVersion`. `AuditEntries` (created by migration 4) is a normal user table with no exclusion, so a full Reset today silently destroys the entire audit trail. **User decision: bring this fix into #141's scope** — it is the actual bug the issue exists to prevent, just for a table the issue's author didn't realise was affected.

2. **`ImportBatchType.System` is a row-level tag, not a table-level one**, and does not need table-level protection. `Quotes`/`Sources`/`Characters`/`People` each mix `System` rows (curated data) together with `Seed`/`UserSeed`/`Import` rows in the same table — a whole-table exemption can protect one classification only by protecting all of them, which is not the goal. Curated content is re-seeded from `data/sources/quotinator-curated.json` on every reseed/reset anyway, so it never actually needs protecting from data loss. **User decision: scope #141 to genuinely whole-table system tables only (`AuditEntries`, `SchemaVersion`) — no row-level filtering is added to the four entity tables.**

3. **`SchemaVersion`'s open question is resolved as: keep default behaviour, make it optional.** Reset continues to clear and replay `SchemaVersion` by default — unchanged from today's documented behaviour ("Clears all data and schema version history, reapplies all migrations from scratch"). A new opt-in parameter lets a caller preserve it instead.

A comment documenting this scope narrowing will be posted on #141 before work starts, per `process.md`'s deferral rule — the GitHub issue's "table-level classification mechanism... System-prefixed table names" language no longer matches what will actually ship.

---

## Spec requirements (as narrowed)

1. `AuditEntries` survives a full `Reset` — table-level protection via a named exclusion, the same mechanism `SchemaVersion` already uses in `Sql.Schema.GetUserTables`
2. `SchemaVersion` continues to be cleared and replayed on `Reset` **by default** — no change to today's behaviour
3. A new opt-in parameter (`ResetAsync(bool preserveSchemaVersion = false)`) lets a caller skip the `SchemaVersion` clear + migration replay, preserving existing version history instead
4. `POST /api/v1/admin/database/reset` exposes the new option via a `preserveSchemaVersion` query parameter, default `false`
5. `Reseed` is unaffected — `TruncateDataAsync` already leaves both tables untouched; no code change needed there, but the behaviour gets an explicit regression test since it was previously implicit
6. `README.md`, `addon/DOCS.md`, and the endpoint's `WithDescription` text updated to reflect the new behaviour and parameter (per `CLAUDE.md`'s "Keeping API documentation in sync")

---

## Step status

- [x] `Sql.Schema.GetUserTables` excludes `AuditEntries` in addition to `SchemaVersion`
- [x] `IDatabaseInitializer.ResetAsync` gains `bool preserveSchemaVersion = false` parameter
- [x] `DatabaseInitializer.DropAndRebuildAsync` accepts and honours `preserveSchemaVersion`
- [x] `QuotinatorDatabaseInitializer.OnResetAsync` threads the parameter through
- [x] `POST /api/v1/admin/database/reset` accepts `?preserveSchemaVersion=true`
- [x] `README.md`, `addon/DOCS.md`, and endpoint `WithDescription` text updated
- [x] Unit tests added and green
- [x] Scope-narrowing comment posted on #141
- [x] Unreleased changelog entry added

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

---

## Notes

- No new migration is needed. Excluding `AuditEntries` from `GetUserTables` is a query change in `Sql.cs`, not DDL — `DropAndRebuildAsync` simply stops dropping the table, and the `CREATE TABLE IF NOT EXISTS` in migration 4 becomes a no-op for it on replay, which is already idempotent by design.
- `preserveSchemaVersion` is declared as a plain `bool preserveSchemaVersion = false`. Initially planned to mirror the `string?` + `TryParse` pattern from `CLAUDE.md`'s "Year parameter binding pattern," but re-checked against the current codebase (`src/Quotinator.Api/Middleware/BadRequestExceptionHandler.cs`) and found that pattern is now superseded by a global `IExceptionHandler` registered ahead of `AddProblemDetails()`: it catches any `BadHttpRequestException` from parameter binding — not just the year params — and returns a proper localized 422 `Problem`. Its own doc comment confirms the year-param `TryParseYear` path exists only for the specific case that predates this handler; "this handler fires only for unexpected binding failures on other parameter types," which is exactly this case. No new `ApiMessages` key or translation entries needed.

**Bug found and fixed during this work:** the first implementation of `preserveSchemaVersion:true` simply skipped `Sql.Schema.DeleteAll` before `DropAllTablesAsync`/`ApplyMigrationsAsync`. `ResetAsync_PreserveSchemaVersionTrue_KeepsExistingRows` immediately caught the consequence: `DropAllTablesAsync` still drops every data table unconditionally, but with `SchemaVersion`'s rows untouched, `ApplyMigrationsAsync` saw `current >= _migrations.Count` and took its "already up to date" early return — skipping the `CREATE TABLE` DDL replay entirely and leaving the database with no `Quotes` table at all (`SQLite Error 1: 'no such table: Quotes'` on the immediate reseed attempt). Fixed by decoupling the two concerns: `DropAndRebuildAsync` now always runs the full, already-tested clear+rebuild path unconditionally (so every table always comes back), and only when `preserveSchemaVersion` is `true` does it snapshot `SchemaVersion`'s rows beforehand and restore that exact snapshot afterward — the rebuild itself is untouched, only the final `SchemaVersion` row content is swapped back. This avoids touching `ApplyMigrationsAsync`/`ApplyPendingMigrationsAsync` at all, keeping the well-tested migration-replay logic frozen.

**Message-accuracy drift found, not fixed here (out of scope):** the shared `BadRequestExceptionHandler` returns `localizer[ApiMessages.NumericParameterInvalid]` for *any* unparsable primitive query parameter, including this new boolean one. Its English text reads "Numeric parameters (yearFrom, yearTo, year, decade, page, pageSize, n) must be whole numbers." — accurate for the year params it was written for, slightly misleading for a malformed `preserveSchemaVersion` value (it *is* a structurally correct 422 `Problem`, just with wording that doesn't mention booleans). Not fixed as part of #141 since it is a pre-existing, shared message used well beyond this endpoint; worth a small follow-up if it comes up again.
