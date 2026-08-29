# #349 — Admin endpoints to list, delete and report status for database backups

**Status:** Waiting for release
**GitHub issue:** #349
**Tiers required:** T1, T2
**Depends on:** none — pairs with [#348](https://github.com/DutchJaFO/Quotinator/issues/348), either order

---

## Description

Quotinator writes a safety backup before every migration, seed and Reset, into `{dataDir}/backups/`.
Nothing can see them, and nothing can remove one — the only route is filesystem access to the
container's volume, which a Home Assistant add-on user does not meaningfully have.

That matters rather than being untidy: the folder carries a hard size budget, and since #348 landed a
full folder is a *refusal* — a startup degrades, or a Reset declines. The remedy is to remove old
backups, and there is no in-app way to do it.

There is a second gap of the same kind. A backup only ever happens as a side effect of a migration, a
seed or a Reset — an operator who wants a restore point *before* doing something cannot ask for one, and
cannot get a copy of one off the container.

Endpoints are preferred over telling an operator to delete or copy files by hand because an endpoint
carries authorization and writes an audit trail — useful precisely when diagnosing a problem that arose
from the action (developer direction, 2026-08-27).

---

## Next action

**Nothing outstanding — ready for release.** All 37 verification rows are green, T1 and T2 are both
complete, the solution builds at 0 warnings, and the full suite passes.

**Three defects were found by running things rather than by writing tests**, and each was invisible to
the layer above it. T2 found an unhandled `500` on `DELETE` against a read-only mount (row 30). T1 then
found a second unhandled `500` on download, caused by a pooled connection holding every backup file
open — invisible in a Linux container, so no T2 pass could have caught it (row 32). Fixing that exposed
the read path's missing mirror of row 30's own fix (row 31).

---

## What #348 already built that this issue consumes

Recorded here rather than left for whoever starts this to rediscover — #348 landed after this plan was
written and changed several of its premises.

**The status endpoint's data source exists.** `IDatabaseInitializer.CheckBackupReadiness(bool
allowReserve = false)` returns a `BackupOutcome`: `Succeeded`, or which of the obstacles is in the way.
That is exactly requirement 3's "can a backup be made right now", so the endpoint **reports** it rather
than reimplementing the question. It inspects storage headroom and destination writability — including
a probe-file write, because `Directory.CreateDirectory` on an existing directory is a no-op that
succeeds on a read-only mount — and never reads database content, so it is safe to call while degraded.

**Cause and remedy text already exists too.** `Quotinator.Api.Startup.BackupObstacleGuidance` maps an
obstacle to `Cause(...)` and `Remedies(..., overrideAlreadyTried)`. The status endpoint should use it
rather than growing a second vocabulary for the same conditions; `Quotinator.Data` deliberately carries
the typed outcome only, so all operator-facing wording lives in the Api layer.

**Quota is two levels, so status reports two.** `DatabaseOptions.BackupQuotaPercent` (default 90) is the
operating quota; `MaxBackupStorageGb` is the absolute ceiling; the reserve between them is reachable
only when a caller explicitly asks. A status response showing a single number would misdescribe the
model — see #348's plan for why the ceiling alone cannot be the answer.

**Half the enumeration already exists, privately.** `DatabaseInitializer.ExistingBackupBytes()` sums the
backups folder. That bears directly on this plan's open question: the choice is between promoting it and
building the reader beside it, not between building something and nothing.

**So does the arithmetic around it, and it is already duplicated.** `EffectiveQuotaPercent()` is private
beside it, and the ceiling is an inline `MaxBackupStorageGb * 1_073_741_824L` written out at three
separate call sites. An endpoint that recomputed those would be a fourth copy, free to drift from the
check that actually refuses a Reset — which is why step 1 extracts rather than exposes.

**A deletion audit entry needs no migration.** `AuditOperation.BackupSkipped` set the precedent in #348,
and `Audit_Entry.Operation` is `TEXT NOT NULL` with no CHECK constraint (verified 2026-08-27), so
ADR 008's enum-column checklist does not apply to a new operation. What that rests on is itself a
convention deviation — see *Scope changes*.

**Ports 18381–18384 are taken** by `docs/automated-testing/backup/`, the category #348 created.
`RepositoryStructureTests.EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother`
fails on a reuse, so any document this issue adds picks from elsewhere. That category is also where an
endpoint document most likely belongs rather than a new one.

---

## Design

Five endpoints, all under a new `Backup` OpenAPI category (developer decision, 2026-08-27) rather than
the `Admin` tag every `/admin/**` route carries today — backup management is a distinct operator task,
and burying it among reseed/reset/audit hides it from the operator who came looking because a backup
failed. Their own `MapGroup` still chains the admin API key and the concurrency-1 rate-limit policy: the
category is a documentation grouping, never relaxed access.

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/admin/backups` | List what exists — name, size, when taken. Paginated per the standard contract |
| `DELETE /api/v1/admin/backups/{name}` | Remove one, by the name the list returns |
| `GET /api/v1/admin/backups/status` | Can a backup be made right now, and are we within quota |
| `GET /api/v1/admin/backups/{name}/content` | Download one, so a restore point survives the container |
| `POST /api/v1/admin/backups/create` | Take one now, rather than waiting for a migration or a Reset to do it |

**The status endpoint answers the question an operator actually has**, before they act rather than when
a Reset declines. It surfaces #348's pre-flight check (a yes, or a no naming which obstacle) alongside
the quota picture (used, operating quota, absolute ceiling, what remains, whether the reserve is in
use), plus real free disk space — an independent constraint from the self-imposed quota, so showing only
one would mislead exactly when the other binds.

**All five must answer while the database is degraded**, which is the state they exist for.

**Nothing here writes to the live database**, and that is what keeps this issue separable from
[#352](https://github.com/DutchJaFO/Quotinator/issues/352) (restore) and
[#353](https://github.com/DutchJaFO/Quotinator/issues/353) (upload), both filed 2026-08-29. Every
endpoint here either reads the backups folder or adds a file to it.

**Create exists because restore deliberately does not take a backup** (developer decision, 2026-08-29).
A restore that snapshotted the current database first would bolt a second data-retention decision onto
an endpoint with one job, and would deposit a copy of the state the operator was discarding into the
quota #348 refuses on. The operator takes a restore point when they want one, explicitly, here — and
`AuditOperation.Backup` (`"BackedUp"`) already exists for it, with no producer until now.

### The status endpoint writes a probe file, and says so

The issue calls this endpoint "read-only and cheap … so the degraded UI can call it on render". The
first half is not true of the check it wraps: `CheckBackupReadiness` writes and deletes a zero-byte
`.writable-probe`, deliberately, because #348 measured that `Directory.CreateDirectory` is a no-op on an
existing directory and returns happily on a read-only mount — a check that writes nothing proves nothing.

**Decided (developer decision, 2026-08-29): keep the write, and correct the wording.** Reporting a
writability answer without testing writability would reproduce the exact defect #348 was filed for. What
the endpoint guarantees is narrower than "read-only" and is stated in those terms instead: *it reads no
database content, and touches the filesystem only to the extent of one zero-byte probe*. That is what
makes it safe to call while degraded, which is the property the issue actually needed. The UI may still
call it on render. The issue body's wording is corrected as part of step 8.

### A deletion is audited as a string constant, and the deviation is filed separately

`AuditEntryEntity.Operation` is a bare `string` backed by twelve `const string` members on a static
`AuditOperation` class, with no CHECK constraint on the column. By this project's convention — an enum
wherever the value set is bounded and not expected to grow during normal operation — that column should
be a `SafeValue<AuditOperation?>` with the CHECK constraint
[ADR 008](../../architecture-decisions/008-enum-backed-columns-require-check-constraints.md) requires,
exactly as `ImportActionStatus`, `NotificationType` and `ChangeAction` already are.

**Decided (developer decision, 2026-08-29):** this issue adds `BackupDeleted` as a thirteenth `const
string`, matching the shape that is there, and the conversion is filed as its own issue — see *Scope
changes*. Converting here would pull a table-rebuild migration, a baseline update, a schema-drift test
and ~50 call sites into an endpoint issue. Recorded rather than left implicit, so the next reader knows
the const string was a sequencing decision and not another copy of the previous member's shape — the
compounding failure `CLAUDE.md`'s *Authoritative sources* section warns about.

---

## Steps

### 1. Expose backup enumeration and the storage figures

**Status:** ✅ Done

The handlers must not touch the filesystem directly, so this adds a reader/writer pair in
`Quotinator.Data` consumed through DI — `IDatabaseBackupReader` (enumerate the folder; report the
storage figures; open one file for streaming) and `IDatabaseBackupWriter` (delete one file). Taking a
backup stays with `DatabaseInitializer`, which already does it: `CreateBackup` is private and needs
exposing on `IDatabaseInitializer`, returning the `DatabaseBackupResult` it already builds so the create
endpoint inherits #348's obstacle vocabulary for free. Restoring belongs to #352 and is not built here.

The storage arithmetic moves with it, into one helper both this reader and
`DatabaseInitializer.CheckBackupReadiness` call — used bytes, the effective quota percent, the operating
quota, and the absolute ceiling. The point is that the number the status endpoint publishes and the
number a Reset refuses on cannot diverge.

### 2. Write the red tests

**Status:** ✅ Done

Every method named in the Verification checklist below, red before any handler exists.

`BackupStorageBudgetTests` and `DatabaseBackupReaderTests` are separate classes, not folded into
`DatabaseBackupQuotaTests` because its fixture was already there. That folding was tried and was wrong:
the agreement test it produced would hold just as well if both sides computed the same wrong number, and
writing the reader's own class properly is what found the probe-artefact defect in row 27.

Both halves of the traversal cases are asserted deliberately — rejected *and* nothing deleted — since a
status assertion alone would pass even if the file had already gone. Per the positive-control rule in
[`docs/testing-policy.md`](../../testing-policy.md), the delete and status suites each carry their
working case beside the refusals, not only the refusals.

### 3. Declare the `Backup` tag and register the group

**Status:** ✅ Done

`ApiTags.Backup`, its own `MapGroup` chaining the admin API key and `RateLimitPolicies.Admin`, and the
`document.Tags` entry with a real description in `Program.cs`'s `AddDocumentTransformer` — in the same
commit, per [ADR 020](../../architecture-decisions/020-openapi-tags-are-declared-with-descriptions.md).
This is that rule's first live application since #339 found the gap it was written for.

### 4. Implement `GET /api/v1/admin/backups`

**Status:** ✅ Done

Paginated via `PaginationParsing.TryParse`, returning `PagedItems<T>`, with `pageSize = 0` meaning every
row and the response reporting the actual count. `GetAllBackups` / "List backups", the `WithName` value
held in a `private const string` referenced by both the registration and its logging tag.

### 5. Implement `DELETE /api/v1/admin/backups/{name}`

**Status:** ✅ Done

`{name}` is a file name, never a path: anything carrying a separator or a traversal segment is rejected
before it reaches the filesystem, and the resolved path is verified to sit inside `BackupsPath`. An
unknown name is a `404`, so a caller can tell "removed" from "was never there". A successful deletion
writes an `AuditOperation.BackupDeleted` entry naming the file.

The guard is written once here and reused by step 6, not implemented twice.

### 6. Implement `GET /api/v1/admin/backups/{name}/content`

**Status:** ✅ Done

Streamed rather than buffered, `Content-Disposition` naming the stored file, the same `{name}` guard and
the same `404` as step 5. `GetBackupContent` / "Backup content by name" — a fetch, but by name rather
than by id, with the deviation noted at the call site.

The `admin` limiter is concurrency-1, so a download holds the only admin permit until it finishes. At
~8 MB that is immaterial; over a slow link it locks out every other admin route. Accepted, and recorded
so it is a known trade-off rather than a surprise.

### 7. Implement `POST /api/v1/admin/backups/create`

**Status:** ✅ Done

`CreateBackup` / "Create a backup", calling step 1's newly exposed initialiser method. It refuses in
#348's vocabulary — obstacle plus remedies, the same shape a refused Reset returns — rather than
reporting success for an attempt that produced no file, and names the file it wrote so the caller can
download it via step 6. The audit entry uses the existing `AuditOperation.Backup`.

### 8. Implement `GET /api/v1/admin/backups/status`

**Status:** ✅ Done

`CheckBackupReadiness` for the readiness half, step 1's figures for the quota half, and free disk space
via `IDiskSpaceProvider` — reported alongside the quota, not folded into it. Obstacle wording comes from
`BackupObstacleGuidance`, not a second vocabulary. `GetBackupStatus` / "Backup status", with the
deviation from the three standard endpoint shapes noted at the call site as the naming convention
requires: this is not a list, not a fetch by id, and not an action.

### 9. Point #348's remedy text at these endpoints

**Status:** ✅ Done

`BackupObstacleGuidance.Remedies(BudgetExceeded)` currently says *"Remove one or more old backups to free
quota"* with no way to do it, and `InsufficientDiskSpace` says removing backups reclaims space the same
way. Both name the endpoints once they exist — which is the coupling #348's own dependency note
recorded, and the reason it was written as advice rather than an instruction.

### 10. Update the API documentation

**Status:** ✅ Done

`docs/api-endpoints.md` and the `[Description]` attributes, in the same commit. The issue body's
"read-only" claim about the status endpoint was corrected in place on 2026-08-29, so what remains here
is the repository documentation.

### 11. Add the T2 document for a full-quota refusal

**Status:** ✅ Done (written; running it is step 12)

`docs/automated-testing/backup/` has documents 01–04 and none of them covers `BudgetExceeded` — the one
obstacle these endpoints remedy. The new document sabotages by filling the quota, asserts the `409`, then
resolves it through `DELETE /api/v1/admin/backups/{name}` and confirms a reset then succeeds: the
positive control and the proof that the remedy the message names actually works, in one step. Its port
comes from outside 18381–18384, per the reuse guard named above.

The operator's whole loop is reachable in that one document — create a backup, download it, delete
enough to clear the quota, reset — so it exercises four of the five endpoints against a real container
rather than only the one being advertised as the remedy.

### 12. T1 and T2 verification

**Status:** ✅ Done

T1 is the developer's own to run. T2 is a rebuilt image with the smoke set plus this issue's own
document from step 11.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The list reports every backup with the facts needed to choose one — name, size, when taken | Unit test | `AdminBackupEndpointsTests.GetBackups_ReturnsEachBackupWithItsNameSizeAndTimestamp` |
| 2 | ✅ | An empty backups folder is an empty page, not a `404` | Unit test | `AdminBackupEndpointsTests.GetBackups_NoBackupsExist_ReturnsAnEmptyPageNotA404` |
| 3 | ✅ | A deletion removes the named file and nothing else | Unit test | `AdminBackupEndpointsTests.DeleteBackup_RemovesOnlyTheNamedFile` |
| 4 | ✅ | A deletion writes an audit entry, so "why is there no backup from that date" has an answer | Unit test | `AdminBackupEndpointsTests.DeleteBackup_WritesAnAuditEntry`. `AuditOperation.BackupDeleted` needs no migration — `Audit_Entry.Operation` is `TEXT NOT NULL` with no CHECK constraint, per #348's row 6 |
| 5 | ✅ | Deleting a name that does not exist is a `404`, distinguishable from a successful removal | Unit test | `AdminBackupEndpointsTests.DeleteBackup_UnknownName_Returns404` |
| 6 | ✅ | No `{name}` route can escape the backups folder — rejected **and** nothing removed or served | Unit test | `AdminBackupEndpointsTests.DeleteBackup_PathTraversalAttempt_IsRejectedAndDeletesNothing`, `...DeleteBackup_AbsolutePathAttempt_IsRejectedAndDeletesNothing`, `AdminBackupDownloadEndpointTests.Download_PathTraversalAttempt_IsRejectedAndServesNothing`. Both halves asserted: a rejected status alone would pass against a build that had already deleted or served the file |
| 7 | ✅ | All five endpoints sit behind the admin API key | Unit test | `AdminBackupEndpointsTests.GetBackups_WithoutApiKey_Returns401`, `...DeleteBackup_WithoutApiKey_Returns401`, `AdminBackupDownloadEndpointTests.Download_WithoutApiKey_Returns401`, `AdminBackupCreateEndpointTests.Create_WithoutApiKey_Returns401`, `AdminBackupStatusEndpointTests.GetStatus_WithoutApiKey_Returns401` |
| 8 | ✅ | A download returns the stored file unaltered, named so it can be kept | Unit test | `AdminBackupDownloadEndpointTests.Download_ReturnsTheFilesBytes_ByteForByte` and `...Download_SetsAnAttachmentNameMatchingTheStoredFile` — byte-for-byte, since a backup that does not round-trip is not a restore point |
| 9 | ✅ | Downloading a name that does not exist is a `404`, not an empty file | Unit test | `AdminBackupDownloadEndpointTests.Download_UnknownName_Returns404` |
| 10 | ✅ | An operator can take a backup on demand, and is told what it produced | Unit test | `AdminBackupCreateEndpointTests.Create_WritesABackupFile_AndNamesItInTheResponse` |
| 11 | ✅ | A create that cannot take a backup refuses with the obstacle and its remedies, never a success with no file | Unit test | `AdminBackupCreateEndpointTests.Create_WhenNoBackupCanBeTaken_RefusesWithTheObstacleAndItsRemedies` — the same shape a refused Reset returns |
| 12 | ✅ | A creation writes an audit entry | Unit test | `AdminBackupCreateEndpointTests.Create_WritesAnAuditEntry`, using the existing `AuditOperation.Backup` (`"BackedUp"`) — declared since the audit trail was built and without a producer until now |
| 13 | ✅ | The five endpoints compose: what create writes, list shows and download returns | Unit test | `AdminBackupCreateEndpointTests.Create_TheCreatedFileAppearsInTheList_AndCanBeDownloaded` — the operator's actual loop, asserted end to end rather than one endpoint at a time |
| 14 | ✅ | The status endpoint says whether a backup can be taken right now, and names the obstacle when it cannot | Unit test | `AdminBackupStatusEndpointTests.GetStatus_WhenABackupIsPossible_SaysSo` and `...GetStatus_WhenABackupIsNotPossible_NamesTheObstacle` — the possible case is the positive control for the whole status suite |
| 15 | ✅ | The status endpoint reports used, operating quota, absolute ceiling, what remains, and the percentage | Unit test | `AdminBackupStatusEndpointTests.GetStatus_ReportsUsedQuotaCeilingAndPercentage` |
| 16 | ✅ | It reports whether the reserve between quota and ceiling is currently being relied on | Unit test | `AdminBackupStatusEndpointTests.GetStatus_ReportsWhetherTheReserveAboveTheQuotaIsInUse` |
| 17 | ✅ | Real free disk space is reported alongside the quota, not folded into it | Unit test | `AdminBackupStatusEndpointTests.GetStatus_ReportsRealFreeDiskSpaceSeparatelyFromTheQuota` — the two are independent constraints and the backup path checks both |
| 18 | ✅ | No backups is zero used, not an error | Unit test | `AdminBackupStatusEndpointTests.GetStatus_NoBackupsExist_ReportsZeroUsedNotAnError` |
| 19 | ✅ | The status endpoint reads no database content, and answers while degraded | Unit test | `AdminBackupStatusEndpointTests.GetStatus_ReadsNoDatabaseContent_AndAnswersWhileDegraded`. The filesystem write it *does* perform is one zero-byte probe, by design — see the Design section |
| 20 | ✅ | What status promises is what create actually does | Unit test | `AdminBackupStatusEndpointTests.GetStatus_AgreesWithWhatACreateAttemptActuallyDoes` — a status endpoint that says yes where create then refuses is worse than none; the check-and-attempt-agree property #348 found was worth its own test |
| 21 | ✅ | All five routes reach their handlers while degraded, rather than being answered by the health gate | Unit test | `AdminBackupEndpointsTests.AllRoutes_RemainReachableWhileDegraded` — tested for these routes specifically rather than assumed to follow from #326's `Startup_DataDirectoryNotWritable_OpenApiRemainsReachableForRecovery` |
| 22 | ✅ | The list honours the standard pagination contract in full | Unit test | `AdminBackupEndpointsPaginationTests.Page_Zero_Returns422`, `...Page_Malformed_Returns422`, `...PageSize_Malformed_Returns422`, `...PageSize_Negative_Returns422`, `...PageSize_AboveMax_Returns422_NeverClamped`, `...PageSize_Zero_ReturnsEveryRow_AndReportsTheActualCount`, `...PageSize_Omitted_DefaultsToTwenty`, `...Page_BeyondLastPage_Returns422_DistinctFromPageZero` |
| 23 | ✅ | The `Backup` tag is declared with a real description, not used bare | Unit test | `OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription` — already green, must stay green once the tag exists. Plus `OpenApiSpecEndpointTests.BackupRoutes_AreTaggedBackup_NotAdmin`, since staying green would also be satisfied by never adding the tag at all |
| 24 | ✅ | The published figures and the figure a Reset refuses on come from one place | Unit test | `DatabaseBackupQuotaTests.PublishedUsage_AgreesWithTheLimitAReadinessCheckRefusesOn` — checked at 50/89/95/100% of the ceiling, so agreement is proven across the quota boundary rather than at one safe point. Its other 11 tests pass unchanged against the extracted helper |
| 25 | ✅ | The shared arithmetic is correct, not merely self-consistent | Unit test | `BackupStorageBudgetTests` — 19 cases over the ceiling, the quota percentage, the limit and the used total, each with the honoured value beside the rejected one. Distinct from row 24 on purpose: agreement would also hold if both sides computed the same wrong number. Shown able to fail by mutation — clamping the out-of-range percentage instead of falling back, and recursing into subdirectories, failed 7 between them |
| 26 | ✅ | The reader answers correctly for a present backup and for an absent one | Unit test | `DatabaseBackupReaderTests` — 14 cases: listing, ordering, an empty folder, a missing folder, usage below and above the quota, free disk space, and open/exists/valid-name each proven on both a real backup and a name with nothing behind it |
| 27 | ✅ | The pre-flight's own probe file is never offered as a backup | Unit test | `DatabaseBackupReaderTests.List_ExcludesTheWritabilityProbeArtefact` — written red and it failed: a `.writable-probe` left behind by a failed delete was listed, downloadable and deletable as if it were a restore point. Now excluded from listing, opening and deletion alike, and deliberately still counted in the storage total, which is a claim about bytes on disk |
| 28 | ✅ | #348's remedy text names these endpoints rather than describing an action with no route | Unit test | `AdminEndpointsTests.ResetRefusedForBudget_RemedyNamesTheBackupEndpoints` |
| 29 | ✅ | A full quota is resolvable end to end, from inside the application | Live | T2: `docs/automated-testing/backup/05-a-full-quota-is-resolvable-from-inside-the-application.md` — fill the quota, `409` with `backupObstacle: BudgetExceeded`, `DELETE` a backup through the endpoint, reset then returns `200`. Run 2026-08-29: the loop closed as designed, and the pass found three defects in the document plus an unhandled `500` in the application — see row 30 |
| 30 | ✅ | A removal the filesystem refuses is a stated answer, not an unhandled failure | Unit test | `DatabaseBackupReaderTests.Delete_FileCannotBeRemoved_IsReported_NotThrown` and `AdminBackupEndpointsTests.DeleteBackup_FileCannotBeRemoved_Returns409NotAnUnhandled500`, plus `backup/05` step 7 live. Found by running `backup/05` rather than by any unit test: `DELETE` on a read-only data directory threw out of `File.Delete` and reached the caller as a bare `500` — #348's own defect class, on the one path an operator is most likely to take. `IDatabaseBackupWriter` now returns `BackupDeleteOutcome`, which also separates "was never there" from "could not be removed" |
| 31 | ✅ | A read the filesystem refuses is a stated answer, not an unhandled failure | Unit test | `DatabaseBackupReaderTests.OpenRead_FileCannotBeOpened_IsReported_NotThrown` and `AdminBackupDownloadEndpointTests.Download_FileCannotBeOpened_Returns409NotAnUnhandled500`. The read-side mirror of row 30, which was fixed without it — the reader now returns `BackupReadOutcome` and opens with `FileShare.ReadWrite`, so a file another handle holds writable is still downloadable |
| 32 | ✅ | A backup we have just written is not still held open by us | Unit test | `DatabaseBackupQuotaTests.CreateBackupAsync_LeavesNoHandleOnTheFileItWrote`, plus `AdminBackupCreateEndpointTests.Create_ThenDownloadImmediately_Succeeds` and `backup/05` step 6 reading the stored file from the host. Found in T1: `Microsoft.Data.Sqlite` pools by default, so the destination connection kept its handle after disposal and every backup ever written stayed locked — a download moments later was an unhandled `500`. Invisible on Unix, which is why the T2 pass missed it and mistook it for a host quirk |
| 33 | ✅ | Every endpoint is visible in the log, at the level its kind deserves | Unit test | `AdminBackupLoggingTests` — reads at Debug (the status endpoint is polled on every degraded-UI render), create and delete at Information, refusals at Warning and never demoted. Asserted through a real Serilog pipeline via `CaptureSink`, per `docs/logging.md`'s rule that a MEL double cannot prove a `{:l}` specifier is present. **These endpoints originally logged nothing at all**: logging was never made a requirement, so no test could have caught its absence — and requirement 14's "referenced by both the registration and its own logging tag" was half-unread |
| 34 | ✅ | Every log tag is declared in `docs/logging.md` before it appears in code | Unit test | `AdminBackupLoggingTests.EveryBackupTag_IsRegisteredInTheLoggingDocument` — reads the document itself rather than a list here. That rule existed with nothing enforcing it, which is how five unregistered tags nearly shipped |
| 35 | ✅ | The audit trail records what it is documented to record, and nothing more | Unit test | `AdminBackupEndpointsTests.DeleteBackup_WritesAnAuditEntry` and `AdminBackupCreateEndpointTests.Create_WritesAnAuditEntry`, both with `TableName = "Database"` and `RecordId = null` per `docs/logging.md`'s audit schema. A `BackupDownloaded` operation was added and then removed: the document is explicit that reads are not audited, and a download leaves the backup in place (developer decision, 2026-08-29) |
| 36 | ✅ | `docs/api-endpoints.md` and the `[Description]` attributes describe all five endpoints | Live | Both updated in the implementing commit; the status endpoint's description states what it touches rather than claiming read-only |
| 37 | ✅ | The Blazor UI still renders and the endpoints answer against a real container | Live | T2 re-run 2026-08-29 after the handle fix — create, download and host-side read all confirmed. T1 in Visual Studio is the developer's own and is what remains |

---

## Deferred — a per-file record for backups

**A backup file gets no metadata record today, and that belongs to
[#330](https://github.com/DutchJaFO/Quotinator/issues/330), not here** (developer direction,
2026-08-29). The project already keeps per-file records for imported content; a backup is a file this
application creates and should be treated the same way.

This also settles a limitation found while wiring the audit trail. `docs/logging.md`'s audit schema
reserves `RecordId` for an affected row's UUID and states that an admin action carries `null`, so an
audit entry can say a backup was removed but not *which* one — the file name lives in the log line
instead. Extending the audit schema to carry a file name was considered and rejected: the file identity
belongs in #330's file-keyed record, which exists for exactly this, rather than in a table keyed by
database rows.

#330 covers this in principle but not yet in wiring — its background enumerates bundled sources, the
manifest, downloaded caches and user imports, and its establishment hook is "first download or first
inspection, during manifest creation", which a backup never passes through. Its own justification for
including the manifest is that *"applies to all files we create" wins*, which is the argument for
backups too.

## Scope changes

**`AuditOperation` should be an enum, and is not.** Twelve `const string` members on a static class,
backing a `TEXT NOT NULL` column with no CHECK constraint — a bounded set that only grows when a new
kind of auditable action is designed, which is exactly what this project's convention and ADR 008
govern. #348 added the twelfth (`BackupSkipped`) into that shape, and this issue adds a thirteenth.

Filed as [#351](https://github.com/DutchJaFO/Quotinator/issues/351) rather than absorbed here (developer
decision, 2026-08-29), in this same milestone so the deviation does not outlive it: the conversion needs
`SafeValue<AuditOperation?>`, a CHECK constraint added to an existing column — a table rebuild, since
SQLite cannot add one in place — the baseline update and schema-drift test ADR 008 requires, and ~20
source and ~30 test call sites. That is a schema issue, not an endpoint issue.
