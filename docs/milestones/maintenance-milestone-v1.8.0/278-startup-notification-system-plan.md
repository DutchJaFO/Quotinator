# #278 — Add a startup notification system surfaced in the #263 modals

**Status:** Planning
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
**Status:** ⬜ Not started

### 2. `NotificationType` / `NotificationDismissTrigger` enums
**Status:** ⬜ Not started

### 3. `NotificationEntity`, migration 8, baseline SQL, schema-drift test
**Status:** ⬜ Not started

### 4. `Sql.Notifications`, `INotificationReader`/`Reader`, `INotificationWriter`/`Writer`, config key, DI registration
**Status:** ⬜ Not started

### 5. `NotificationResponse`, `ApiTags.Notifications`, `NotificationEndpoints.cs`, `NumericParameterSchemaTransformer` registration, `docs/api-endpoints.md`
**Status:** ⬜ Not started

### 6. Wire `POST /admin/database/reset` to `DismissByTriggerAsync(DatabaseReset)`
**Status:** ⬜ Not started

### 7. `NotificationSummary` component; wire into `StartupSuccessModal`/`StartupErrorModal`
**Status:** ⬜ Not started

### 8. `Notifications.razor` page + `NavMenu.razor` link + i18n keys
**Status:** ⬜ Not started

### 9. Tests (all red before implementation, per issue's Expected tests table)
**Status:** ⬜ Not started

### 10. Full verification (T1, T2), `docs/smoke-tests.md` update, changelog
**Status:** ⬜ Not started

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Active notifications exclude dismissed | Unit test | `NotificationReaderTests.GetActiveNotifications_ReturnsUndismissedOnly` |
| 2 | ⬜ | Active notifications exclude expired | Unit test | `NotificationReaderTests.GetActiveNotifications_ExcludesExpiredNotifications` |
| 3 | ⬜ | Writer persists all five `NotificationType` values | Unit test | `NotificationWriterTests.WriteAsync_PersistsAllFiveTypes` |
| 4 | ⬜ | Omitted expiry applies configured default | Unit test | `NotificationWriterTests.WriteAsync_NoExpirySpecified_AppliesConfiguredDefault` |
| 5 | ⬜ | Dismiss-by-trigger marks matching active notifications dismissed | Unit test | `NotificationWriterTests.DismissByTrigger_MarksMatchingActiveNotificationsAsDismissed` |
| 6 | ⬜ | Dismiss-by-trigger is a no-op when nothing matches | Unit test | `NotificationWriterTests.DismissByTrigger_NoMatchingTrigger_IsNoOp` |
| 7 | ⬜ | `StartupSuccessModal` shows Information/Success notifications | Unit test | `StartupSuccessModalTests.ShowsInformationAndSuccessNotifications` (exact class name confirmed during implementation) |
| 8 | ⬜ | `StartupErrorModal` shows Warning/Error notifications | Unit test | `StartupErrorModalTests.ShowsWarningAndErrorNotifications` (exact class name confirmed during implementation) |
| 9 | ⬜ | `page=0` → 422 | Unit test | `NotificationEndpointsTests.GetNotifications_PageZero_Returns422` |
| 10 | ⬜ | Malformed `page` → 422 | Unit test | `NotificationEndpointsTests.GetNotifications_PageMalformed_Returns422` |
| 11 | ⬜ | Malformed `pageSize` → 422 | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeMalformed_Returns422` |
| 12 | ⬜ | Negative `pageSize` → 422 | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeNegative_Returns422` |
| 13 | ⬜ | `pageSize` > 500 → 422, never clamped | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeAbove500_Returns422NotSilentClamp` |
| 14 | ⬜ | `pageSize=0` → every row as one page | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeZero_ReturnsAllRowsAsOnePage` |
| 15 | ⬜ | `pageSize` omitted → defaults to 20 | Unit test | `NotificationEndpointsTests.GetNotifications_PageSizeOmitted_DefaultsTo20` |
| 16 | ⬜ | Page beyond last → 422, distinct detail | Unit test | `NotificationEndpointsTests.GetNotifications_PageBeyondLast_Returns422DistinctDetail` |
| 17 | ⬜ | Dismiss existing id marks it dismissed | Unit test | `NotificationEndpointsTests.DismissNotification_ExistingId_MarksDismissed` |
| 18 | ⬜ | Dismiss unknown id → 404 | Unit test | `NotificationEndpointsTests.DismissNotification_UnknownId_Returns404` |
| 19 | ⬜ | Dismiss without API key → 401 | Unit test | `NotificationEndpointsTests.DismissNotification_NoApiKey_Returns401` |
| 20 | ⬜ | Live OpenAPI spec tags notification endpoints correctly | Unit test | `NotificationEndpointsTests.NotificationEndpoints_OnLiveSpec_TaggedNotifications` |
| 21 | ⬜ | `pageSize=0` returns every row at the reader/repository level, not just the endpoint fake | Unit test | `NotificationReaderTests.GetPagedAsync_PageSizeZero_ReturnsAllRows` (real SQLite, per Standard pagination contract's Case 6 rule) |
| 22 | ⬜ | Data-owned baseline and incremental replay produce identical `System_Notification` schema | Unit test | Extended `DataOwnedBaseline_And_IncrementalReplay_Produce...` schema-drift test |
| 23 | ⬜ | `POST /admin/database/reset` dismisses matching `DatabaseReset`-triggered notifications | Unit test | New case in `AdminEndpointsTests` (or `SqliteQuoteServiceTests` equivalent) covering the write → active → reset → dismissed round trip |
| 24 | ⬜ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 25 | ⬜ | Full test suite green | Build | `dotnet test --configuration Release` — all pass, 0 warnings |
| 26 | ⬜ | T1 (developer's own Visual Studio run) | Live | Confirmed clean start, no error |
| 27 | ⬜ | T2 (Docker smoke tests) | Live | Full `docs/smoke-tests.md` pass including new Notifications section |
| 28 | ⬜ | `docs/smoke-tests.md` updated with a Notifications section | Manual | New section added, referenced in Step 10 |
| 29 | ⬜ | `docs/api-endpoints.md` documents the new endpoints | Manual | New rows added, referenced in Step 5 |

---

## Relationship to existing issues

- **#276** — parent tracking issue.
- **#267** — original investigation this was split from.
- **#279** — depends on this issue landing first (breaking `operationId` renames need this notification
  mechanism to exist first, per developer direction 2026-08-09).
- **#280** — depends on this issue landing first (its bonus startup-progress display is meant to reuse
  this notification/status infrastructure rather than invent a second one).
