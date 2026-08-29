# #349 — Admin endpoints to list, delete and report status for database backups

**Status:** Planning
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

**Execute this plan, starting at step 1.** Both design questions raised while refining it are decided
(2026-08-29) and written into the Design below: what the status endpoint does about #348's probe write,
and how a backup deletion is recorded in the audit trail.

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

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

Every method named in the Verification checklist below, red before any handler exists.

Both halves of the traversal cases are asserted deliberately — rejected *and* nothing deleted — since a
status assertion alone would pass even if the file had already gone. Per the positive-control rule in
[`docs/testing-policy.md`](../../testing-policy.md), the delete and status suites each carry their
working case beside the refusals, not only the refusals.

### 3. Declare the `Backup` tag and register the group

**Status:** ⬜ Not started

`ApiTags.Backup`, its own `MapGroup` chaining the admin API key and `RateLimitPolicies.Admin`, and the
`document.Tags` entry with a real description in `Program.cs`'s `AddDocumentTransformer` — in the same
commit, per [ADR 020](../../architecture-decisions/020-openapi-tags-are-declared-with-descriptions.md).
This is that rule's first live application since #339 found the gap it was written for.

### 4. Implement `GET /api/v1/admin/backups`

**Status:** ⬜ Not started

Paginated via `PaginationParsing.TryParse`, returning `PagedItems<T>`, with `pageSize = 0` meaning every
row and the response reporting the actual count. `GetAllBackups` / "List backups", the `WithName` value
held in a `private const string` referenced by both the registration and its logging tag.

### 5. Implement `DELETE /api/v1/admin/backups/{name}`

**Status:** ⬜ Not started

`{name}` is a file name, never a path: anything carrying a separator or a traversal segment is rejected
before it reaches the filesystem, and the resolved path is verified to sit inside `BackupsPath`. An
unknown name is a `404`, so a caller can tell "removed" from "was never there". A successful deletion
writes an `AuditOperation.BackupDeleted` entry naming the file.

The guard is written once here and reused by step 6, not implemented twice.

### 6. Implement `GET /api/v1/admin/backups/{name}/content`

**Status:** ⬜ Not started

Streamed rather than buffered, `Content-Disposition` naming the stored file, the same `{name}` guard and
the same `404` as step 5. `GetBackupContent` / "Backup content by name" — a fetch, but by name rather
than by id, with the deviation noted at the call site.

The `admin` limiter is concurrency-1, so a download holds the only admin permit until it finishes. At
~8 MB that is immaterial; over a slow link it locks out every other admin route. Accepted, and recorded
so it is a known trade-off rather than a surprise.

### 7. Implement `POST /api/v1/admin/backups/create`

**Status:** ⬜ Not started

`CreateBackup` / "Create a backup", calling step 1's newly exposed initialiser method. It refuses in
#348's vocabulary — obstacle plus remedies, the same shape a refused Reset returns — rather than
reporting success for an attempt that produced no file, and names the file it wrote so the caller can
download it via step 6. The audit entry uses the existing `AuditOperation.Backup`.

### 8. Implement `GET /api/v1/admin/backups/status`

**Status:** ⬜ Not started

`CheckBackupReadiness` for the readiness half, step 1's figures for the quota half, and free disk space
via `IDiskSpaceProvider` — reported alongside the quota, not folded into it. Obstacle wording comes from
`BackupObstacleGuidance`, not a second vocabulary. `GetBackupStatus` / "Backup status", with the
deviation from the three standard endpoint shapes noted at the call site as the naming convention
requires: this is not a list, not a fetch by id, and not an action.

### 9. Point #348's remedy text at these endpoints

**Status:** ⬜ Not started

`BackupObstacleGuidance.Remedies(BudgetExceeded)` currently says *"Remove one or more old backups to free
quota"* with no way to do it, and `InsufficientDiskSpace` says removing backups reclaims space the same
way. Both name the endpoints once they exist — which is the coupling #348's own dependency note
recorded, and the reason it was written as advice rather than an instruction.

### 10. Update the API documentation

**Status:** ⬜ Not started

`docs/api-endpoints.md` and the `[Description]` attributes, in the same commit. The issue body's
"read-only" claim about the status endpoint was corrected in place on 2026-08-29, so what remains here
is the repository documentation.

### 11. Add the T2 document for a full-quota refusal

**Status:** ⬜ Not started

`docs/automated-testing/backup/` has documents 01–04 and none of them covers `BudgetExceeded` — the one
obstacle these endpoints remedy. The new document sabotages by filling the quota, asserts the `409`, then
resolves it through `DELETE /api/v1/admin/backups/{name}` and confirms a reset then succeeds: the
positive control and the proof that the remedy the message names actually works, in one step. Its port
comes from outside 18381–18384, per the reuse guard named above.

The operator's whole loop is reachable in that one document — create a backup, download it, delete
enough to clear the quota, reset — so it exercises four of the five endpoints against a real container
rather than only the one being advertised as the remedy.

### 12. T1 and T2 verification

**Status:** ⬜ Not started

T1 is the developer's own to run. T2 is a rebuilt image with the smoke set plus this issue's own
document from step 11.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | The list reports every backup with the facts needed to choose one — name, size, when taken | Unit test | `AdminBackupEndpointsTests.GetBackups_ReturnsEachBackupWithItsNameSizeAndTimestamp` |
| 2 | ❌ | An empty backups folder is an empty page, not a `404` | Unit test | `AdminBackupEndpointsTests.GetBackups_NoBackupsExist_ReturnsAnEmptyPageNotA404` |
| 3 | ❌ | A deletion removes the named file and nothing else | Unit test | `AdminBackupEndpointsTests.DeleteBackup_RemovesOnlyTheNamedFile` |
| 4 | ❌ | A deletion writes an audit entry, so "why is there no backup from that date" has an answer | Unit test | `AdminBackupEndpointsTests.DeleteBackup_WritesAnAuditEntry`. `AuditOperation.BackupDeleted` needs no migration — `Audit_Entry.Operation` is `TEXT NOT NULL` with no CHECK constraint, per #348's row 6 |
| 5 | ❌ | Deleting a name that does not exist is a `404`, distinguishable from a successful removal | Unit test | `AdminBackupEndpointsTests.DeleteBackup_UnknownName_Returns404` |
| 6 | ❌ | No `{name}` route can escape the backups folder — rejected **and** nothing removed or served | Unit test | `AdminBackupEndpointsTests.DeleteBackup_PathTraversalAttempt_IsRejectedAndDeletesNothing`, `...DeleteBackup_AbsolutePathAttempt_IsRejectedAndDeletesNothing`, `AdminBackupDownloadEndpointTests.Download_PathTraversalAttempt_IsRejectedAndServesNothing`. Both halves asserted: a rejected status alone would pass against a build that had already deleted or served the file |
| 7 | ❌ | All five endpoints sit behind the admin API key | Unit test | `AdminBackupEndpointsTests.GetBackups_WithoutApiKey_Returns401`, `...DeleteBackup_WithoutApiKey_Returns401`, `AdminBackupDownloadEndpointTests.Download_WithoutApiKey_Returns401`, `AdminBackupCreateEndpointTests.Create_WithoutApiKey_Returns401`, `AdminBackupStatusEndpointTests.GetStatus_WithoutApiKey_Returns401` |
| 8 | ❌ | A download returns the stored file unaltered, named so it can be kept | Unit test | `AdminBackupDownloadEndpointTests.Download_ReturnsTheFilesBytes_ByteForByte` and `...Download_SetsAnAttachmentNameMatchingTheStoredFile` — byte-for-byte, since a backup that does not round-trip is not a restore point |
| 9 | ❌ | Downloading a name that does not exist is a `404`, not an empty file | Unit test | `AdminBackupDownloadEndpointTests.Download_UnknownName_Returns404` |
| 10 | ❌ | An operator can take a backup on demand, and is told what it produced | Unit test | `AdminBackupCreateEndpointTests.Create_WritesABackupFile_AndNamesItInTheResponse` |
| 11 | ❌ | A create that cannot take a backup refuses with the obstacle and its remedies, never a success with no file | Unit test | `AdminBackupCreateEndpointTests.Create_WhenNoBackupCanBeTaken_RefusesWithTheObstacleAndItsRemedies` — the same shape a refused Reset returns |
| 12 | ❌ | A creation writes an audit entry | Unit test | `AdminBackupCreateEndpointTests.Create_WritesAnAuditEntry`, using the existing `AuditOperation.Backup` (`"BackedUp"`) — declared since the audit trail was built and without a producer until now |
| 13 | ❌ | The five endpoints compose: what create writes, list shows and download returns | Unit test | `AdminBackupCreateEndpointTests.Create_TheCreatedFileAppearsInTheList_AndCanBeDownloaded` — the operator's actual loop, asserted end to end rather than one endpoint at a time |
| 14 | ❌ | The status endpoint says whether a backup can be taken right now, and names the obstacle when it cannot | Unit test | `AdminBackupStatusEndpointTests.GetStatus_WhenABackupIsPossible_SaysSo` and `...GetStatus_WhenABackupIsNotPossible_NamesTheObstacle` — the possible case is the positive control for the whole status suite |
| 15 | ❌ | The status endpoint reports used, operating quota, absolute ceiling, what remains, and the percentage | Unit test | `AdminBackupStatusEndpointTests.GetStatus_ReportsUsedQuotaCeilingAndPercentage` |
| 16 | ❌ | It reports whether the reserve between quota and ceiling is currently being relied on | Unit test | `AdminBackupStatusEndpointTests.GetStatus_ReportsWhetherTheReserveAboveTheQuotaIsInUse` |
| 17 | ❌ | Real free disk space is reported alongside the quota, not folded into it | Unit test | `AdminBackupStatusEndpointTests.GetStatus_ReportsRealFreeDiskSpaceSeparatelyFromTheQuota` — the two are independent constraints and the backup path checks both |
| 18 | ❌ | No backups is zero used, not an error | Unit test | `AdminBackupStatusEndpointTests.GetStatus_NoBackupsExist_ReportsZeroUsedNotAnError` |
| 19 | ❌ | The status endpoint reads no database content, and answers while degraded | Unit test | `AdminBackupStatusEndpointTests.GetStatus_ReadsNoDatabaseContent_AndAnswersWhileDegraded`. The filesystem write it *does* perform is one zero-byte probe, by design — see the Design section |
| 20 | ❌ | What status promises is what create actually does | Unit test | `AdminBackupStatusEndpointTests.GetStatus_AgreesWithWhatACreateAttemptActuallyDoes` — a status endpoint that says yes where create then refuses is worse than none; the check-and-attempt-agree property #348 found was worth its own test |
| 21 | ❌ | All five routes reach their handlers while degraded, rather than being answered by the health gate | Unit test | `AdminBackupEndpointsTests.AllRoutes_RemainReachableWhileDegraded` — tested for these routes specifically rather than assumed to follow from #326's `Startup_DataDirectoryNotWritable_OpenApiRemainsReachableForRecovery` |
| 22 | ❌ | The list honours the standard pagination contract in full | Unit test | `AdminBackupEndpointsPaginationTests.Page_Zero_Returns422`, `...Page_Malformed_Returns422`, `...PageSize_Malformed_Returns422`, `...PageSize_Negative_Returns422`, `...PageSize_AboveMax_Returns422_NeverClamped`, `...PageSize_Zero_ReturnsEveryRow_AndReportsTheActualCount`, `...PageSize_Omitted_DefaultsToTwenty`, `...Page_BeyondLastPage_Returns422_DistinctFromPageZero` |
| 23 | ❌ | The `Backup` tag is declared with a real description, not used bare | Unit test | `OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription` — already green, must stay green once the tag exists. Plus `OpenApiSpecEndpointTests.BackupRoutes_AreTaggedBackup_NotAdmin`, since staying green would also be satisfied by never adding the tag at all |
| 24 | ❌ | The published figures and the figure a Reset refuses on come from one place | Unit test | `DatabaseBackupQuotaTests` continues to pass unchanged against step 1's extracted helper, and `BackupStorageBudgetTests` asserts the reader and `CheckBackupReadiness` agree at the quota boundary |
| 25 | ❌ | #348's remedy text names these endpoints rather than describing an action with no route | Unit test | `AdminEndpointsTests.ResetRefusedForBudget_RemedyNamesTheBackupEndpoints` |
| 26 | ❌ | A full quota is resolvable end to end, from inside the application | Live | T2: the new `docs/automated-testing/backup/` document — fill the quota, `409` with `backupObstacle: BudgetExceeded`, `DELETE` a backup through the endpoint, reset then returns `200`. Exercises create, list, download and delete against a real container along the way |
| 27 | ❌ | `docs/api-endpoints.md` and the `[Description]` attributes describe all five endpoints | Live | Both updated in the implementing commit; the status endpoint's description states what it touches rather than claiming read-only |
| 28 | ❌ | The Blazor UI still renders and the endpoints answer against a real container | Live | T1 (developer) and T2 smoke set against a rebuilt `quotinator:local` |

---

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
