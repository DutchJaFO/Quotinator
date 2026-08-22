# #278 — Add a startup notification system surfaced in the #263 modals

**Status:** Released
**GitHub issue:** #278
**Tiers required:** T1, T2
**Depends on:** None

---

## Background

Split from #267's investigation, tracked under parent #276. Adds a persisted notification mechanism
(`Information`/`Warning`/`Error`/`Success`/`ActionRequired`) with its own lifecycle (expiry, and
dismiss-on-related-action), surfaced in `StartupSuccessModal`/`StartupErrorModal`, a dedicated
`GET /api/v1/notifications` + `POST /api/v1/notifications/{id}/dismiss` REST surface, and a permanent
Blazor page for reviewing notification history outside the transient startup modals. See the issue body
for the full background — not repeated here.

**#279 depends on this issue landing first** (breaking `WithName`/`operationId` renames need this
notification mechanism to exist before they ship, per developer direction 2026-08-09) — see
`overview.md`'s dependency map.

## Authoritative-source cross-check

Checked against `docs/architecture-decisions/` before designing anything below — no conflicts found:

- **ADR 002 (RecordBase on all tables, no exception)** — `Audit_Entry`/`AuditEntryEntity` already
  proves this applies even to a system/audit table (it originally shipped without `RecordBase` and was
  corrected retroactively; ADR 008 cites that exact deviation as a cautionary tale). The new
  `NotificationEntity` inherits `RecordBase`, no exception considered.
- **ADR 008 (enum-backed columns require CHECK constraints)** — both new enum columns (`Type`,
  `DismissTriggerKey`) get an inline `CHECK` on the same `ALTER TABLE`/`CREATE TABLE` statement that
  adds them, per `Import_FileResource`'s `Origin`/`LineEnding` precedent (`FileResourceMigrations.cs`).
- **ADR 015 (domain-prefixed table naming)** — this is operational/system content, not quote-domain
  content, and not audit-trail or import content either, so it takes the residual `System_` domain:
  table `System_Notification`, owned by `Quotinator.Data` (`DataOwnedMigrations`, not
  `Quotinator.Core`'s `QuotinatorMigrations`).
- **ADR 016 (class suffixes, enum placement)** — `NotificationEntity` in `Quotinator.Data.Entities`;
  `NotificationResponse` in `Quotinator.Core.Models` (matching `FileResourceResponse`'s precedent —
  every other `*Response` DTO already lives there, not in `Quotinator.Api.Models`); `NotificationType`
  and `NotificationDismissTrigger` in `Quotinator.Data.Enums` (matching `FileResourceOrigin`/
  `LineEndingStyle` — the other Data-owned, CHECK-constrained enums — not `Quotinator.Core.Enums`,
  which holds domain-content enums like `QuoteType`/`Genre`).

No scope mismatch found between the issue text and current authoritative sources — proceeding with the
issue as written.

---

## Approach

### Entity and table shape

`NotificationEntity : RecordBase`, table `System_Notification`:

| Column | Type | Notes |
|---|---|---|
| `Id`, `DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted` | — | From `RecordBase`. `DateCreated` **is** the notification's created-at timestamp — no separate duplicate column (avoiding `Audit_Entry`'s own documented `PerformedAt`/`DateCreated` duplication trade-off, since nothing here needs a timestamp distinct from creation). |
| `Type` | `SafeValue<NotificationType?>` | `NOT NULL`, `CHECK (Type IN ('Information','Warning','Error','Success','ActionRequired'))` |
| `Message` | `string` | `NOT NULL` — the specific reason/action text (e.g. "consider running a Reset") lives here, not as a separate enum value per #278's own scoping note |
| `ExpiresAt` | `SafeValue<DateTime?>` | Nullable; always populated at write time (explicit value, or the configured default — see below) |
| `IsDismissed` | `bool` | `NOT NULL DEFAULT 0` — mirrors `RecordBase`'s own `IsDeleted`/`DateDeleted` pairing style |
| `DismissedAt` | `SafeValue<DateTime?>` | Nullable |
| `DismissTriggerKey` | `SafeValue<NotificationDismissTrigger?>` | Nullable; `CHECK (DismissTriggerKey IS NULL OR DismissTriggerKey IN ('DatabaseReset'))` |

"Active" (per item 3 of the issue) = `IsDismissed = 0 AND IsDeleted = 0 AND (ExpiresAt IS NULL OR ExpiresAt > @now)`.

`NotificationDismissTrigger` starts with exactly one member, `DatabaseReset` — the concrete example
item 7 of the issue asks for (an `ActionRequired` notification recommending a Reset, dismissed once
`POST /admin/database/reset` succeeds). More triggers are added to this enum (+ its `CHECK`, via a
future migration) as future producer integrations need them — not spread speculatively now.

### Migration

Version 8 in `Quotinator.Data`'s `DataOwnedMigrations` (`DatabaseInitializer.cs`) —
`NotificationMigrations.CreateNotificationTable`, new file
`src/Quotinator.Data/Database/NotificationMigrations.cs`, following `FileResourceMigrations.cs`'s shape
(version 6's template — the most recent table-creation-with-enum-CHECK migration). `DataBaselineSql`
gets the matching `CREATE TABLE IF NOT EXISTS System_Notification (...)` in the same commit, and the
existing Data-owned baseline/incremental-replay schema-drift test is extended to cover it.

### Config

`QueryParamDefaults.NotificationDefaultExpiryHours` (new constant, `Quotinator.Constants.Api`),
following `AdminAuditExportMaxRows`'s exact precedent (same class, same
`configuration.GetValue<int?>(...) ?? NamedConstant` read site pattern) — default **720** (30 days), a
sensible homelab default for how long an unaddressed notification should stay visible before quietly
expiring, overridable via `Quotinator:NotificationDefaultExpiryHours`. Read once in `Program.cs` and
passed into `NotificationWriter`'s constructor via the DI factory-overload exception (`AddSingleton<
INotificationWriter>(sp => new NotificationWriter(...))`) — the container can't supply a computed `int`
at registration time, matching the project's documented DI exception.

### Reader / writer

`src/Quotinator.Data/Repositories/INotificationReader.cs` / `NotificationReader.cs`:
- `GetActiveNotificationsAsync()` — the undismissed-and-unexpired set, for the startup modals (item 3).
- `GetPagedAsync(page, pageSize)` — the full history (including dismissed/expired), for the REST list
  endpoint and the Blazor page — matching the Standard pagination contract, returning `PagedItems<
  NotificationEntity>`.

`src/Quotinator.Data/Repositories/INotificationWriter.cs` / `NotificationWriter.cs`:
- `WriteAsync(NotificationType type, string message, DateTime? expiresAt = null, NotificationDismissTrigger? dismissTrigger = null)`
  — applies the configured default expiry when `expiresAt` is omitted.
- `DismissAsync(Guid id)` — marks one notification dismissed; no-op-safe if already dismissed.
- `DismissByTriggerAsync(NotificationDismissTrigger trigger)` — marks every active notification
  matching the trigger dismissed; no-op if none match (item 7).

New `Sql.Notifications` nested class in `Sql.cs` for the reader's raw queries (`SelectActive`,
`SelectPage`, `CountActive` / `CountAll`) and the writer's update statements
(`UpdateDismissById`, `UpdateDismissByTrigger`); the insert itself uses Dapper.Contrib's `InsertAsync`
via `[Table]`/`[ExplicitKey]`, matching `AuditEntryWriter`'s precedent — no hand-written `INSERT` SQL
needed. Both registered `AddSingleton` in `Program.cs`, alongside the existing `IAuditEntryReader/
Writer` registrations.

### REST endpoints

New `src/Quotinator.Api/Endpoints/NotificationEndpoints.cs`, mirroring `ImportFileResourceEndpoints.cs`
exactly (explicit precedent named in the issue): both `publicGroup` and `adminGroup` map
`/api/v1/notifications`, tagged `ApiTags.Notifications` (new constant), rate-limited
`RateLimitPolicies.Admin` on both groups (matching `ImportFileResourceEndpoints`'s own choice, not the
lighter `api` policy).

- `publicGroup.MapGet("/")` — paginated list of **all** notifications (including dismissed/expired —
  the Blazor page needs history, not just active), full Standard pagination contract (8 test cases:
  page 0, malformed page, malformed/negative/>500 pageSize, pageSize=0, pageSize omitted, page beyond
  last). `.WithName("GetNotifications")` / `.WithSummary("List notifications")`.
- `adminGroup.MapPost("/{id}/dismiss")` — marks one notification dismissed; 404 for an unknown id
  (new `ApiMessages.NotificationNotFound` key), 401 via the existing `AdminApiKeyFilter` when no/wrong
  key. `.WithName("DismissNotification")` / `.WithSummary("Dismiss a notification")`. Returns the
  updated `NotificationResponse`.

`NotificationResponse` (`Quotinator.Core.Models`) — `Id`, `Type`, `Message`, `CreatedAt`, `ExpiresAt`,
`IsDismissed`, `DismissedAt`, `DismissTriggerKey`.

Both list-endpoint page/pageSize parameters registered in `NumericParameterSchemaTransformer.
NumericParamsByPath` (per CLAUDE.md's numeric-query-parameter-binding rule — a new paginated GET
endpoint that skips this registration is exactly #194's own found gap).

### Dismiss-on-action wiring

`POST /admin/database/reset`'s handler (`AdminEndpoints.cs`) calls
`notificationWriter.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset)` as part of its own
success path, after the reset itself completes — the concrete end-to-end example items 5 and 7 of the
issue ask for (write an `ActionRequired` notification tagged `DatabaseReset` somewhere reachable for a
test, confirm it's active, run Reset, confirm it's gone).

### Startup modals

New `src/Quotinator.Api/Components/Controls/NotificationSummary.razor` (+ code-behind), a sibling to
the existing `DatabaseStatsSummary` component (same shape: injects `INotificationReader`, populates a
list in `OnInitializedAsync`) rather than folding into `DatabaseStatsSummary` itself — one component per
data domain, matching the existing separation. Embedded in both `StartupSuccessModal.razor`
(`Information`/`Success`/non-fatal `ActionRequired`) and `StartupErrorModal.razor` (`Warning`/`Error`),
alongside the existing `<DatabaseStatsSummary />`, per item 4.

### Blazor Notifications page

New `src/Quotinator.Api/Components/Pages/Notifications.razor` (+ code-behind), route `/notifications`,
following `Stats.razor`'s precedent exactly: injects `INotificationReader`/`INotificationWriter`
directly (server-side Blazor, same process — no REST round-trip, matching how `Stats.razor` sources its
own data rather than calling its own REST endpoints). Lists all notifications (paginated) with a Dismiss
action per row. Added to `NavMenu.razor` alongside the existing `stats`/`about` links.

### i18n

New UI keys (page title, empty state, dismiss button/confirmation, per-type labels for the startup
modal and the Notifications page) added to all three `UI.*.json` files in the same commit as the Razor
markup that references them, per the existing localisation checklist — exact key names decided during
implementation, not enumerated here.

---

## Steps

### 1. Plan doc, slnx, overview.md
**Status:** ✅ Done

### 2. `NotificationType` / `NotificationDismissTrigger` enums
**Status:** ✅ Done

### 3. `NotificationEntity`, migration 8, baseline SQL, schema-drift test
**Status:** ✅ Done

### 4. `Sql.Notifications`, `INotificationReader`/`Reader`, `INotificationWriter`/`Writer`, config key, DI registration
**Status:** ✅ Done

Two existing guard tests enumerate a fixed, documented inventory and needed their lists extended,
matching the exact pattern prior issues (#251, etc.) already hit: `SqlBoundaryTests.
Sql_ContainsOnlyGenericInfrastructureQueries` (added `Notifications` to the allowed
generic-infrastructure nested-type set) and `SqlQueryGuardTests.AggregateQueries_MatchDocumentedInventory`
(added `Notifications.CountAll` — a plain `COUNT(*)`, no `GROUP BY`/`HAVING`, reviewed against
`docs/sql-safety.md`).

### 5. `NotificationResponse`, `ApiTags.Notifications`, `NotificationEndpoints.cs`, `NumericParameterSchemaTransformer` registration, `docs/api-endpoints.md`
**Status:** ✅ Done

### 6. Wire `POST /admin/database/reset` to `DismissByTriggerAsync(DatabaseReset)`
**Status:** ✅ Done

**Found while implementing:** Reset (`DropAndRebuildAsync`) drops and rebuilds every table with no
protected/excluded set (per CLAUDE.md's "No exception-based migration recovery" section) —
`System_Notification` is wiped along with everything else. This means the `DismissByTriggerAsync`
call added here always affects zero rows in practice immediately after a real Reset, since the table
is already empty by the time it runs. Implemented anyway, matching the issue's own explicit wiring
instruction (item 7) — it's the correct call site for the general mechanism, harmless for Reset
specifically, and would become load-bearing for a future trigger action that doesn't wipe the whole
database. The mechanism's own correctness is proven directly at the writer-unit level instead (Steps
9/Verification rows 5–6), not via a Reset round-trip. `AdminEndpointsTests.cs`'s shared
`WebApplicationFactory` needed a new `NoOpNotificationWriter` registration (`Quotinator.Data.Testing.
NoOps`) — its `NoOpDatabaseInitializer` skips migrations entirely, so the reset handler's new
`INotificationWriter` dependency would otherwise hit a real SQLite connection with no
`System_Notification` table.

### 7. `NotificationSummary` component; wire into `StartupSuccessModal`/`StartupErrorModal`
**Status:** ✅ Done

Added a `bi-bell-fill-nav-menu` icon to `NavMenu.razor.css` (same embedded-SVG pattern as the existing
nav icons) — used by Step 8's nav link, not this step, but added alongside since it lives in the same
file family.

**Revision — T1 finding, 2026-08-09.** The original design (per the issue's own literal wording, "e.g.
Information/Success/ActionRequired" for the success modal, "e.g. Warning/Error" for the error modal)
filtered `NotificationSummary` per modal by type via a `Types` parameter. Live testing surfaced the
flaw: which modal renders is already gated entirely by `DatabaseHealthState` (success vs. degraded
startup), not by notification type. A Warning/Error notification unrelated to database health at all
(e.g. #278's own founding example — "the pre-seed backup was skipped due to low disk space") would
never appear in *either* popup unless the database also happened to be independently broken at that
exact moment — defeating the mechanism for its own motivating use case. Developer-confirmed fix
(2026-08-09): both modals now show every active notification regardless of type via a shared
`NotificationTable` component (`Components/Controls/NotificationTable.razor`, new) — `NotificationSummary`
dropped its `Types` parameter entirely and now just renders whatever `GetActiveNotificationsAsync()`
returns. `NotificationTable` also unifies the popup and the `/notifications` page onto identical
Created/Type/Message/Status row rendering (a second developer-requested change, same session) — the
page passes `ShowDismissAction="true"` for its Dismiss column, the popup passes the default `false`.
`TypeLabel`/`BadgeClass` moved onto `NotificationTable` as `internal static` methods (still directly
testable, no bUnit needed) since they're now shared rather than duplicated between the two call sites.

### 8. `Notifications.razor` page + `NavMenu.razor` link + i18n keys
**Status:** ✅ Done

**Found while implementing:** `/notifications` needed adding to `DatabaseHealthGateMiddleware`'s
`ExemptPrefixes` list (matching `/stats`/`/about`/`/rest-api`'s own precedent as "the Blazor UI's own
page routes... stay reachable so the app never becomes fully unreachable") — otherwise the page would
503 during a degraded startup, unlike its `stats`/`about` nav siblings. `DatabaseHealthGateMiddlewareTests.
Unhealthy_ExemptPath_CallsNext` extended with a `/notifications` case. i18n key names were normalized
from an initial `NotificationsPage*` prefix to bare `Notifications*`, matching the existing
`Stats*`/`About*` sibling-page key convention (no `Page` infix) rather than inventing a new one.

**Revision — developer testing finding, 2026-08-09 (same session as Step 7's revision, after manually
seeding example rows).** Three follow-on fixes surfaced once real data was actually visible:
1. **Status column bug**: an undismissed-but-expired row displayed `Active`, since the Status column
   originally only branched on `IsDismissed`. Fixed via `NotificationTable.GetDisplayStatus(notification,
   now)` — a three-state `Dismissed`/`Expired`/`Active` classification (`Dismissed` takes priority over
   expiry), mirroring `Sql.Notifications.SelectActive`'s own `IsDismissed = 0 AND (ExpiresAt IS NULL OR
   ExpiresAt > @now)` definition of "active" so the two never disagree. `NotificationTable` renders a new
   third `Expired` badge (`bg-secondary`, `NotificationsExpiredLabel`) instead of misreporting `Active`.
2. **Expiration date now visible**: a new "Expires" column on `NotificationTable` (`ExpiresAt` formatted
   the same way as the Created column, `—` when null) — previously the expiry existed only in the
   database, invisible anywhere in the UI.
3. **`/notifications` filter**: the page previously always showed full history unfiltered. Now defaults
   to **Active only**, with a button-group filter (`Active`/`All`/`Expired only`) — `Notifications.
   razor.cs`'s `NotificationFilterMode` enum + `MatchesFilter`, reusing `NotificationTable.
   GetDisplayStatus` so the filter and the Status column can never disagree about what "Active"/"Expired"
   means. A distinct `NotificationsEmptyFiltered` message ("No notifications match this filter.") is
   shown when the filter itself produces zero rows, kept separate from `NotificationsEmpty` ("No
   notifications yet.") which means the underlying table is genuinely empty.

`NotificationTableTests` extended with `GetDisplayStatus` coverage (all three states, plus the
dismissed-takes-priority-over-expired case). Full test suite re-confirmed green (658 Api.Tests, up
from 654). T2 re-confirmed against a fresh container: `/notifications` still renders `200` with
"No notifications yet." and the filter button group correctly stays hidden when there's nothing to
filter (empty-state path only — the developer's own seeded-data VS instance is the faster path to
re-verify the fix against real rows).

### 9. Tests (all red before implementation, per issue's Expected tests table)
**Status:** ✅ Done

**Found while implementing — rows 7–8 of the issue's own Expected tests table substituted, not
followed literally.** This codebase has zero Blazor component-rendering test infrastructure (no
bUnit) — confirmed by grep: `StartupSuccessModal`/`StartupErrorModal`/`DatabaseStatsSummary` have no
existing automated tests of any kind, verified only via manual T1/T2 passes. Adding bUnit as a new
dependency for two render assertions would cut against CLAUDE.md's "keep the dependency footprint
small" priority. `NotificationTable.TypeLabel`/`BadgeClass` (moved there by Step 7's revision, see
above) are unit-tested directly by `NotificationTableTests` (`tests/Quotinator.Api.Tests/Components/`)
instead. Which notifications each modal shows is no longer a markup-level constant after Step 7's
revision (both modals show the same unfiltered active set), so there is nothing left to visually
spot-check beyond the ordinary "does the popup render" T1 check.

Full test roster added: `NotificationReaderTests`/`NotificationWriterTests`
(`Quotinator.Data.Tests`, real SQLite via `NotificationMigrations.CreateNotificationTable` — 12
tests), `NotificationTableTests` (`Quotinator.Api.Tests` — 6 tests), `NotificationEndpointsTests`
(`Quotinator.Api.Tests`, fake-backed per `ImportFileResourceEndpointsTests`'s own precedent — 22
tests including the full 8-case pagination contract and the live-spec tag check), a new
`FakeNotificationReader`/`FakeNotificationWriter` pair (`tests/Quotinator.Api.Tests/Fakes/`), plus
extensions to three existing files: `AdminEndpointsTests` (a spy-writer test proving
`DismissByTriggerAsync(DatabaseReset)` is actually called — see Step 6's note on why a real Reset
round-trip proves nothing here), `DatabaseHealthGateMiddlewareTests` (`/notifications` exempt-path
case), and `OpenApiSpecEndpointTests` (`/api/v1/notifications` page/pageSize integer-type rows, per
CLAUDE.md's "a new `NumericParameterSchemaTransformer` registration needs a live-pipeline test" rule).
Full solution test suite green: 0 failures across all projects.

### 10. Full verification (T1, T2), smoke-test suite update, changelog
**Status:** ✅ Done

Added `docs/smoke-tests.md` section 33 — now `docs/automated-testing/`, whose README maps the old
section numbers — (list/dismiss/tag checks against a fresh empty container,
since no production code path writes a real notification yet — the write→dismiss round trip itself is
covered by the real-SQLite `NotificationWriterTests`, not live-command-verified here). Added a
changelog entry (`en`/`nl`/`de`, lockstep) to `unreleased.added` plus a `highlights` entry (new
user-facing feature), issue #278 added to `unreleased.issues`; regenerated `CHANGELOG.md`,
`addon/CHANGELOG.md`, `addon-beta/CHANGELOG.md` via `scripts/changelog.csx --max-releases 3`.

**T2 confirmed (2026-08-09):** `docker build -f docker/Dockerfile -t quotinator:local .` succeeded.
Fresh container: baseline log shows `data v8, app v6` (migration 8 present). `GET /api/v1/notifications`
→ `200 {"items":[],"page":1,"pageSize":20,"totalCount":0,"totalPages":0}`. Dismiss with no key → `401`;
dismiss an unknown id with the correct key → `404 {"detail":"No notification exists with that ID."}`.
`/openapi/v1.json` contains the `Notifications` tag. `/notifications` and `/` (Home, with
`StartupSuccessModal`) both render `200` with no crash — `/notifications` shows "No notifications yet."
`POST /admin/database/reset` returned `200` with no exception in the logs, confirming the
`DismissByTriggerAsync` call added in Step 6 runs cleanly against the freshly-rebuilt (empty)
`System_Notification` table.

**T1 confirmed (2026-08-09):** developer ran the app in Visual Studio — clean startup log,
`schema is up to date (data v8, app v6)`, no errors. The developer additionally seeded 7 example
notifications directly via SQL (one per type, plus a dismissed and an expired row) to visually verify
rendering — this manual pass is what surfaced Step 7's type-filtering flaw above, fixed in the same
session and re-verified.

### 11. Execute the recommended action directly from `/notifications` (scope addition, 2026-08-09)
**Status:** ✅ Done

**Not in the original issue text — developer-requested mid-session, after seeing the `ActionRequired`
row's Dismiss button only clears the notification without doing anything.** Confirmed with the
developer before implementing (2026-08-09): (1) executing an action must require an explicit confirm
step first, since it calls the same destructive server-side operation the REST endpoint uses with no
`X-Api-Key` gate (Blazor Server, same process, same as how this page already reads/dismisses directly);
(2) build the trigger→action mapping generically now rather than hardcoding `DatabaseReset` alone, even
though it's the only trigger that exists today.

New `Quotinator.Api.Services.INotificationActionExecutor`/`NotificationActionExecutor` (`internal`,
matching this project's convention that Api-layer-only service classes — `DatabaseHealthState`,
`StartupUxState`, `StartupSummaryLogger` — stay `internal`, not `public`): a single `switch` on
`NotificationDismissTrigger` is the one place mapping a trigger to real work — currently just
`DatabaseReset` (calls `IDatabaseInitializer.ResetAsync()`, marks `DatabaseHealthState` healthy, and
dismisses matching notifications, mirroring `AdminEndpoints.cs`'s own reset-success wiring from Step
6). `CanExecute(trigger)` is a separate read-only check so `NotificationTable` can decide whether to
show the Action button per row without being able to trigger anything itself.

`NotificationTable` gained a `ShowActionColumn` parameter (page only, `false` on the popup — an
irreversible action has no place on a transient startup modal) and inline confirm/cancel state (no new
JS-interop dependency — this codebase has zero existing `IJSRuntime` usage, so a native `confirm()`
call would have been the *first*; a pure-Blazor two-click confirm/cancel button pair costs nothing new
and works the same with JS disabled). `NotificationTable` never calls
`INotificationActionExecutor.ExecuteAsync` itself — only the read-only `CanExecute` check — the actual
execution bubbles up via a new `OnExecuteAction` callback (mirroring `OnDismiss`'s own existing
bubble-up shape) so `Notifications.razor.cs` controls the post-execute reload, matching how Dismiss
already works.

New tests: `NotificationActionExecutorTests` (`Quotinator.Api.Tests/Services/`) — `CanExecute` returns
true for `DatabaseReset`, `ExecuteAsync` calls Reset/marks-healthy/dismisses-matching via a spy
`IDatabaseInitializer` + the existing `FakeNotificationWriter`. Full suite re-confirmed green (660
Api.Tests, up from 658). New i18n keys (`NotificationsActionColumn`, `NotificationsRunActionButton`,
`NotificationsConfirmActionButton`, `NotificationsCancelActionButton`, `NotificationsConfirmActionWarning`)
added lockstep across `en`/`de`/`nl`.

**T2 confirmed (2026-08-09):** rebuilt image; fresh container's `/notifications` still renders `200`
with "No notifications yet." (empty-state path — no active `ActionRequired` row exists in a fresh
container to exercise the button itself), no exceptions in the logs.

**T1 confirmed (2026-08-09):** developer re-verified against real seeded data in Visual Studio —
the expired example row now shows `Expired` (not `Active`), the Status filter defaults to Active and
correctly narrows to All/Expired only, the Expires column is visible, and Run → Confirm on the
`DatabaseReset` example row executed the reset successfully.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Active notifications exclude dismissed | Unit test | `NotificationReaderTests.GetActiveNotifications_ReturnsUndismissedOnly` |
| 2 | ✅ | Active notifications exclude expired | Unit test | `NotificationReaderTests.GetActiveNotifications_ExcludesExpiredNotifications` |
| 3 | ✅ | Writer persists all five `NotificationType` values | Unit test | `NotificationWriterTests.WriteAsync_PersistsAllFiveTypes` |
| 4 | ✅ | Omitted expiry applies configured default | Unit test | `NotificationWriterTests.WriteAsync_NoExpirySpecified_AppliesConfiguredDefault` |
| 5 | ✅ | Dismiss-by-trigger marks matching active notifications dismissed | Unit test | `NotificationWriterTests.DismissByTrigger_MarksMatchingActiveNotificationsAsDismissed` |
| 6 | ✅ | Dismiss-by-trigger is a no-op when nothing matches | Unit test | `NotificationWriterTests.DismissByTrigger_NoMatchingTrigger_IsNoOp` |
| 7 | ✅ | `StartupSuccessModal` shows every active notification regardless of type (revised from issue's original per-type split — see Step 7's Revision note) | Unit test + Live | `NotificationTableTests` covers the shared label/badge mapping; confirmed visually via T1/T2 that all active types render in the popup |
| 8 | ✅ | `StartupErrorModal` shows every active notification regardless of type (same revision) | Unit test + Live | Same as row 7 — both modals now share identical unfiltered rendering |
| 9 | ✅ | `page=0` → 422 | Unit test | `NotificationEndpointsTests.GetNotifications_PageZero_Returns422` |
| 10 | ✅ | Malformed `page` → 422 | Unit test | `NotificationEndpointsTests.GetNotifications_PageMalformed_Returns422` |
| 11 | ✅ | Malformed `pageSize` → 422 | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeMalformed_Returns422` |
| 12 | ✅ | Negative `pageSize` → 422 | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeNegative_Returns422` |
| 13 | ✅ | `pageSize` > 500 → 422, never clamped | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeAbove500_Returns422NotSilentClamp` |
| 14 | ✅ | `pageSize=0` → every row as one page | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeZero_ReturnsAllRowsAsOnePage` |
| 15 | ✅ | `pageSize` omitted → defaults to 20 | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeOmitted_DefaultsTo20` |
| 16 | ✅ | Page beyond last → 422, distinct detail | Unit test | `NotificationEndpointsTests.GetNotifications_PageBeyondLast_Returns422DistinctDetail` |
| 17 | ✅ | Dismiss existing id marks it dismissed | Unit test | `NotificationEndpointsTests.DismissNotification_ExistingId_MarksDismissed` |
| 18 | ✅ | Dismiss unknown id → 404 | Unit test | `NotificationEndpointsTests.DismissNotification_UnknownId_Returns404` |
| 19 | ✅ | Dismiss without API key → 401 | Unit test | `NotificationEndpointsTests.DismissNotification_NoApiKey_Returns401` |
| 20 | ✅ | Live OpenAPI spec tags notification endpoints correctly | Unit test | `NotificationEndpointsTests.NotificationEndpoints_OnLiveSpec_TaggedNotifications` |
| 21 | ✅ | `pageSize=0` returns every row at the reader/repository level, not just the endpoint fake | Unit test | `NotificationReaderTests.GetPagedAsync_PageSizeZero_ReturnsAllRows` (real SQLite, per Standard pagination contract's Case 6 rule) |
| 22 | ✅ | Data-owned baseline and incremental replay produce identical `System_Notification` schema | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema` + `...AcceptSameNotificationCheckConstraintValues` |
| 23 | ✅ | `POST /admin/database/reset` calls `DismissByTriggerAsync(DatabaseReset)` as part of its success path | Unit test | `AdminEndpointsTests.ResetDatabase_CorrectKey_CallsDismissByTriggerWithDatabaseReset` (spy writer — see Step 6's note on why a real Reset round-trip proves nothing here) |
| 24 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 25 | ✅ | Full test suite green | Build | `dotnet test --configuration Release` — 1074 Data.Tests + 1437 Core.Tests + 660 Api.Tests, 0 failures |
| 26 | ✅ | T1 (developer's own Visual Studio run) | Live | Confirmed 2026-08-09 — clean startup, `schema is up to date (data v8, app v6)`, no errors; developer manually seeded example notifications and confirmed rendering, which surfaced the Step 7 revision above |
| 27 | ✅ | T2 (Docker smoke tests) | Live | Section 33 pass, 2026-08-09 — see Step 10 |
| 28 | ✅ | The smoke-test suite updated with a Notifications section | Manual | Section 33 added; the suite is now `docs/automated-testing/`, whose README maps the old section numbers |
| 29 | ✅ | `docs/api-endpoints.md` documents the new endpoints | Manual | New rows added in Step 5 |
| 30 | ✅ | `NotificationActionExecutor.CanExecute`/`ExecuteAsync` for `DatabaseReset` | Unit test | `NotificationActionExecutorTests.CanExecute_DatabaseReset_ReturnsTrue` + `ExecuteAsync_DatabaseReset_CallsResetAndMarksHealthyAndDismissesMatchingNotifications` |
| 31 | ✅ | Run → Confirm on an `ActionRequired` row actually executes a Reset from the UI, and Cancel does not | Live | Confirmed 2026-08-09 — see Step 11 |

---

## Relationship to existing issues

- **#276** — parent tracking issue.
- **#267** — original investigation this was split from.
- **#279** — depends on this issue landing first (breaking `operationId` renames need this notification
  mechanism to exist first, per developer direction 2026-08-09).
- **#280** — depends on this issue landing first (its bonus startup-progress display is meant to reuse
  this notification/status infrastructure rather than invent a second one).
