# #249 — Audit-trail bulk export and conflict-resolution data purge

**Status:** Waiting for release
**GitHub issue:** #249
**Tiers required:** T1, T2
**Depends on:** #253/#254 (done — targets the post-rename table names)

---

## Background

Originally filed while planning #151/ADR 014 as "export audit-trail tables to a dedicated folder
before a destructive Reset" — #156 will make Reset drop and rebuild the whole database from baseline,
which empties `Audit_Entry`/`Audit_Change`/`Import_Action`/`Import_Conflict` completely instead of
selectively preserving them the way Reset does today. ADR 014 accepts that loss as correct for Reset
itself, on the condition that an operator who wants to keep this data has a way to get it out first.

**Redesigned 2026-08-05, before implementation started**, after establishing (see below) that two of
the four originally-listed tables need fundamentally different handling, not a single export mechanism:

**Two separate concerns, not one:**
1. **Audit trail** (`Audit_Entry` + `Audit_Change`) — "what happened to our data." Permanent,
   general-purpose investigation data. Never purged automatically; retrievable on demand.
2. **Conflict-resolution data** (`Import_Action`) — "how did we resolve this import." Genuinely
   transient: relevant only while a batch isn't fully resolved. Once a batch reaches zero pending
   actions, the original file content is already safe (`Import_FileResource`, #251) and the actual
   data changes are already applied — the resolution-tracking rows have served their purpose and can
   be purged.

**`Import_Conflict` is out of scope entirely.** Confirmed live (2026-08-04): no code anywhere in the
current codebase writes to it, reads from it, or defines a `Sql.cs` query against it — it's dead schema
left over from before #154's unified staging engine (`Import_Action`) replaced it. Nothing to export,
nothing to purge. Its removal is a separate, smaller cleanup question, not this issue's concern.

This reframing changes what "before a destructive Reset" actually means: instead of an automatic
file-export step bolted onto Reset, an operator who wants to keep the audit trail retrieves it via the
new bulk-export endpoint below, whenever they want, using whatever storage they choose — Quotinator
itself doesn't need to know about "before Reset" as a distinct moment. Conflict-resolution data doesn't
need preserving before Reset at all, since by design it's only ever kept until a batch resolves anyway.

## Decisions (confirmed with the developer, 2026-08-05)

**1. One call, full data set — never make a caller stitch together multiple paginated calls.** Both new
endpoints below return everything requested in a single response; the caller already owns whatever UI/
tooling would reshape the data, so Quotinator's job is just to hand over the full set once.

**2. Auto-purge for conflict-resolution data defaults to ON, with a per-origin override for
debugging.** Normal operation purges a batch's `Import_Action` rows automatically once it reaches zero
pending actions — matching this project's existing pruning philosophy (`FileResource.PruneAsync`,
backup retention). Separate settings for bundled vs. user-imports origins (`Quotinator:AutoPurgeBundled
ImportActions` / `Quotinator:AutoPurgeUserImportActions`, both default `true`) — when investigating a
specific source (e.g. integrating a new bundled source, or a user debugging their own import), the
relevant one is temporarily set to `false` so resolution history is retained until the investigation is
done, then flipped back.

**3. `POST /import`/`POST /import/actions/apply` gain an opt-in `purgeOnSuccess` parameter for the
API-driven path — deliberately, not gated by the seeding-only auto-purge config above.** Seeding is
unattended (no human present to decide file-by-file), so a global config toggle is the right control
there. An API caller is present at call time and can decide per-call whether they're confident enough
to purge immediately. When `purgeOnSuccess=true` and the call results in the batch having zero pending
actions, that batch's `Import_Action` rows are purged immediately as part of the same response.

**This is a deliberate, argued exception to CLAUDE.md's "Endpoint side-effect policy," recorded here so
it isn't mistaken for an oversight.** That policy (added after #156's own reimport-flag finding)
forbids a request parameter that changes what data survives a call, because the flag in that case
controlled a *second, unrelated* decision (whether to reimport bundled quotes) bolted onto a
same-purpose endpoint (rebuild the schema). `purgeOnSuccess` is different in kind: it doesn't touch
unrelated data — it disposes of the *same call's own* temporary working data (the resolution-tracking
rows this exact import operation generated) once that data's only purpose (resolving this import) is
already fulfilled. The developer's own framing: "the side-effect is acceptable as it is directly
related to the data that was used... the import succeeded, so the temporary data we stored to do the
job has no value." **Must be documented clearly in the endpoint's own `[Description]`/OpenAPI text that
setting it forfeits `POST /import/actions/reverse` for that batch** — confirmed live that `ReverseBatchAsync`
throws `"has no actions and cannot be reversed"` once a batch's `Import_Action` rows are gone
(`SqliteImportActionService.cs:549-551`), so this is a real, consequential, irreversible choice the
caller must be making knowingly, not a hidden gotcha.

## Design details (resolved during implementation, 2026-08-05)

- **Audit-trail export endpoint shape:** `GET /api/v1/admin/audit/export?startDate=&endDate=`, returning
  a downloaded JSON file (`Content-Disposition: attachment`) with two top-level arrays (`entries` from
  `Audit_Entry`, `changes` from `Audit_Change`) rather than two separate endpoints — matches "audit
  trail" being one concern per Decision 1. Coexists with the existing paginated `GET /admin/audit`
  (kept as-is for interactive browsing/filtering; the new endpoint is specifically for bulk
  retrieval/download).
- **Date-range discovery endpoint:** `GET /api/v1/admin/audit/date-range` → `{earliestDate,
  latestDate}` spanning both tables, so a caller knows what range actually has data before requesting
  an export.
- **Size cap:** a row-count cap (not byte size, cheaper to check before assembling the response),
  configurable via `Quotinator:AdminAuditExportMaxRows` (default 50,000 —
  `QueryParamDefaults.AdminAuditExportMaxRows` — a homelab-scale install stays well under this for
  years), returning `422` with a message telling the caller to narrow the date range when exceeded,
  rather than silently truncating.
- **Purge trigger point for seeding:** `QuotinatorDatabaseInitializer.cs`'s existing "left staged
  awaiting review" branch (in `SeedIfEmptyInternalAsync`, right after a batch's `ApplyBatchAsync` call)
  is the hook — the `applyResult is null` (fully applied) branch now also purges the batch's
  `Import_Action` rows when the relevant per-origin auto-purge setting is `true`.
- **Cascading/FK behaviour of a purge:** confirmed via schema sweep — no other table carries a foreign
  key to `Import_Action`, so a plain `DELETE` is safe with no cascade cleanup needed.

---

## Steps

### 1. Confirm the design decisions above with the developer

**Status:** ✅ Done — see Decisions 1-3 above, confirmed 2026-08-05.

### 2. Audit-trail bulk export + date-range endpoints

**Status:** ✅ Done — `GET /api/v1/admin/audit/export?startDate=&endDate=` and
`GET /api/v1/admin/audit/date-range`, both public (no `X-Api-Key`, matching `GET /admin/audit`'s
precedent). Row cap is `Quotinator:AdminAuditExportMaxRows` (default 50,000,
`QueryParamDefaults.AdminAuditExportMaxRows`), checked via `COUNT` before assembling the response —
`422` over the cap, never a truncated file. `IAuditEntryReader`/`IChangeReader` gained
`GetAllInRangeAsync`/`CountInRangeAsync`/`GetDateRangeAsync`. 27 endpoint tests in
`AdminAuditEndpointTests.cs`.

### 3. Conflict-resolution data auto-purge (seeding path, config-driven)

**Status:** ✅ Done — `Quotinator:AutoPurgeBundledImportActions`/`Quotinator:AutoPurgeUserImportActions`
(both default `true`), read in `Program.cs` and threaded into `QuotinatorDatabaseInitializer` as plain
`bool`s (matching the existing `autoUpdateSources` DI pattern — a computed config value, not a service).
Purge happens in `SeedIfEmptyInternalAsync`'s existing "batch reached zero pending actions" branch, via
the new `IImportActionWriter.DeleteForBatchAsync`/`Sql.SystemImportActions.DeleteByBatchId`. A
`Audit_Entry` row (`TableName = "Import_Action"`, `Operation = Purged`) is written alongside every
purge, so the permanent audit trail retains a trace even once the resolution data itself is gone.
Exposed as HA add-on options `auto_purge_bundled_import_actions`/`auto_purge_user_import_actions` in
both `addon/` and `addon-beta/` (config.yaml + en/nl/de translations). 5 tests in
`DatabaseInitializerTests.cs` cover both origins × both settings, plus the audit-trace assertion.

### 4. `purgeOnSuccess` parameter on the import endpoints (API-driven path)

**Status:** ✅ Done — `purgeOnSuccess` added to `IImportActionService.ApplyBatchAsync` (the shared
choke point every apply path already goes through per #177) and to `IQuoteImportService.ImportAsync`/
`ApplyStagedBatchAsync`, threaded to `POST /import/actions/apply`, and `POST /import` (both file mode
and `?batchId=` batch mode). Reuses the exact same purge + `Audit_Entry` trace mechanism as the
seeding-path auto-purge (Step 3). OpenAPI descriptions document the `POST /import/actions/reverse`
forfeiture. 5 real-SQLite tests in `SqliteImportActionServiceTests.cs` (purge/retain/still-pending/
audit-trace/reverse-forfeited) plus 7 endpoint tests verifying the query parameter is forwarded
correctly and defaults to `false`.

### 5. `docs/milestones/maintenance-milestone-v1.8.0/overview.md` updated to reflect this issue no
longer being an automatic Reset side-effect, and its release-gate relationship to #156 (per ADR 014)

**Status:** ✅ Done — issue-list row and "Order of operations" entry #15 both updated to describe the
redesigned scope; the release-gate relationship to #156 (already accurate) is unchanged.

### 6. Full build/test/T1/T2 verification

**Status:** ✅ Done — build/test/T1/T2 all verified. T1: developer ran the app in Visual Studio and
exercised `/admin/audit/date-range`, `/admin/audit/export` (valid/invalid/out-of-order date
combinations), `/admin/database/reset`, and `/admin/audit` DELETE — every response matched expected
status codes with no errors.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Design decisions made and recorded | Live | This plan doc's "Decisions" section |
| 2 | ✅ | Audit-trail export returns both `Audit_Entry` and `Audit_Change` data for a date range, as a file | Endpoint test | `AdminAuditEndpointTests.ExportAudit_ReturnsBothTablesData` |
| 3 | ✅ | Date-range discovery endpoint returns the correct earliest/latest dates | Endpoint test | `AdminAuditEndpointTests.GetAuditDateRange_CombinesBothTables_ReturnsOverallEarliestAndLatest` |
| 4 | ✅ | Export request exceeding the row-count cap returns `422`, never a silently truncated file | Endpoint test | `AdminAuditEndpointTests.ExportAudit_CombinedRowCountExceedsCap_Returns422NotTruncatedFile` |
| 5 | ✅ | A seed batch reaching zero pending actions is auto-purged when the relevant per-origin setting is `true`, retained when `false` | Unit test | `DatabaseInitializerTests.InitialiseAsync_AutoPurgeBundledTrue_FullyAppliedBundledBatch_PurgesImportActionRows` + 4 sibling tests |
| 6 | ✅ | `purgeOnSuccess=true` on `POST /import`/`POST /import/actions/apply` purges the batch's `Import_Action` rows only when the call results in zero pending actions | Endpoint test + real-SQLite test | `ImportActionEndpointsTests.ApplyBatch_PurgeOnSuccessTrue_ForwardsTrueToService` + `SqliteImportActionServiceTests.ApplyBatchAsync_PurgeOnSuccessTrue_FullyApplied_PurgesImportActionRows` |
| 7 | ✅ | A purged batch correctly fails `POST /import/actions/reverse` with a clear reason, not a confusing error | Real-SQLite test | `SqliteImportActionServiceTests.ApplyBatchAsync_PurgeOnSuccessTrue_ThenReverseBatchAsync_ThrowsHasNoActionsException` |
| 8 | ✅ | `Import_Conflict` confirmed genuinely unreferenced before this issue closes (re-verify at implementation time, not just at planning time) | Live | `grep` sweep of `src/` for `Import_Conflict`/`ImportConflict*` (2026-08-05) — every hit is either frozen migration history (`ImportConflictMigrations.cs`, `DomainPrefixRenameMigrations.cs`, `DataConsolidatedMigrations.cs`, doc comments) or the unrelated legacy `ImportConflictEntry` response DTO (`ImportResultResponse.cs`), built in-memory from `ImportActionEntity` via `SqliteQuoteImportService.BuildConflictEntries` — never a SQL read/write against the `Import_Conflict` table itself |
| 9 | ✅ | Full solution builds and tests pass | Live | `dotnet build`/`dotnet test --configuration Release -nodeReuse:false` — 0 warnings/0 errors, 3140 tests passed |
| 10 | ✅ | T1 verified | Live | Developer ran the app in Visual Studio — `date-range`, `export` (valid/invalid/out-of-order dates), `database/reset`, `admin/audit` DELETE all returned correct status codes, no errors |
| 11 | ✅ | T2 verified | Live | `docs/smoke-tests.md` §31 — Docker build + live container: date-range/export endpoints, `Content-Disposition` header, row-count cap (422), auto-purge on (0 remaining `Import_Action` rows, 4 `Purged` audit traces) vs. off (1365 rows retained), `purgeOnSuccess=true` on a live import followed by `POST /import/actions/reverse` returning 422 |

---

## Relationship to #156

Per ADR 014, a tagged release must never ship #156's destructive full-rebuild Reset without this
issue's audit-trail export path already available in that same release — a release-level gate, not an
implementation-order dependency. #249 and #156 can still be designed, built, and merged in either order
or in parallel.

---

## Scope changes

**2026-08-05, before implementation started:** redesigned from "automatic file-export step bolted onto
Reset" to two on-demand HTTP endpoints (audit-trail bulk export) plus an auto-purge mechanism for the
separate, transient conflict-resolution dataset — see Background above for the full reasoning. This
resolves all of the original issue's "open design questions to resolve during planning" (trigger,
format/location, retention/pruning) by making most of them moot: there is no accumulating export file to
retain or prune, and no "when does the export run" question, since retrieval is always on-demand.
`Import_Conflict` (originally one of the four tables in scope) dropped entirely — confirmed dead code.

**2026-08-05, found live during T1:** an unscoped `DELETE /admin/audit` left `date-range`/`export`
still showing data, surfacing that the endpoint had never cleared `Audit_Change` — only `Audit_Entry`
— even though #249 now treats both as one combined "audit trail" concern everywhere else. Fixed:
`AuditEntryWriter.ClearAsync` now also clears `Audit_Change` when unscoped (`table` omitted); a
table-scoped clear still leaves `Audit_Change` untouched, since its `EntityType` vocabulary has no
equivalent to `TableName` scoping. This is a genuine behaviour change to a pre-existing endpoint, not
new #249 surface — recorded here since #249's own redesign is what exposed the gap.
