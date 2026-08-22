# #279 — Standardise endpoint naming (WithName/WithSummary) across CRUD and action endpoints

**Status:** Released
**GitHub issue:** #279
**Tiers required:** T1, T2
**Depends on:** #278 (must land first so its notification mechanism exists to announce this issue's breaking `operationId` renames)

---

## Background

#269 converted 8 masterdata `GetAll`/`GetById` endpoint pairs to call shared `LogPageQuery`/
`LogIdQuery`/`LogIdWithLang` extension methods, passing a hardcoded `"[Api - X]"` tag string that
duplicates the endpoint's own `.WithName("X")` value with no compiler link between them. Auditing every
`WithName`/`WithSummary` pair across every endpoint file surfaced a broader pre-existing inconsistency
in the naming/summary text itself. #278 has now landed (committed to this branch, `Waiting for
release`), unblocking this issue's breaking renames per the dependency `overview.md` records.

## Fresh audit (2026-08-09, re-verified against current code — not assumed from the issue's own text)

Re-ran the full audit directly against today's code rather than trusting the issue body, since #269
and #278 both landed since the issue was filed. All findings below are confirmed still present.

**List endpoints** — dominant pattern `WithName("GetAllX")` + `WithSummary("List x")` (lowercase plural
noun). Deviations, all still present:
- `PersonEndpoints.cs`: `WithSummary("All people (paginated)")`
- `QuoteEndpoints.cs` (`GetAllQuotes`): `WithSummary("All quotes (paginated)")`
- `SeriesEndpoints.cs`: `WithSummary("List Series")` — capitalised
- `ImportBatchEndpoints.cs`: `WithName("GetImportBatches")`, not `GetAllImportBatches` — **breaking**
- `ImportFileResourceEndpoints.cs`: `WithName("GetFileResources")`, not `GetAllFileResources` — **breaking**

**GetById endpoints** — dominant pattern `WithName("GetXById")` + `WithSummary("X by ID")`. Deviations,
both still present:
- `ImportBatchEndpoints.cs`: `WithSummary("Import batch by id")` — lowercase `id`
- `ImportFileResourceEndpoints.cs`: `WithSummary("Captured import file by id")` — lowercase `id`

**Exempt by design, confirmed unchanged**: `GetRandomQuotes` (`"Random quote(s)"`), `SearchQuotes`
(`"Search quotes"`), `ImportQuotes` (`"Import quotes, or apply an already-staged batch"`).

**Action endpoints** — audited every `WithName`/`WithSummary` in `AdminEndpoints.cs`,
`ImportEndpoints.cs`, `ImportRuleEndpoints.cs`, `ImportFileResourceEndpoints.cs`'s own
`PruneFileResources`/`DownloadFileResource`, and `NotificationEndpoints.cs`'s `DismissNotification`
(new since the issue was filed — already compliant, see below). **Zero deviations found** — every
single one already reads as `WithName` = PascalCase verb+object, `WithSummary` = imperative-verb-first
phrase (`"Reset the database"`, `"Apply every decided action in a batch"`, `"Dismiss a notification"`,
etc.). Confirmed as the deliberate standard per this audit — no fixes needed for this category, only
documentation (Step 1).

**#269's own log-tag duplication (item 4)** — confirmed present in exactly 9 files, matching the
issue's own count:
- 8 masterdata files (`CharacterEndpoints.cs`, `ConversationEndpoints.cs`, `PersonEndpoints.cs`,
  `SeriesEndpoints.cs`, `SoundCueEndpoints.cs`, `SourceEndpoints.cs`, `StageDirectionEndpoints.cs`,
  `UniverseEndpoints.cs`) — two call sites each (`LogPageQuery`/`LogIdQuery` in `GetAll`/`GetById`).
- `QuoteEndpoints.cs`'s `GetById` — one `LogIdWithLang` call site. **Worse than pure duplication**: the
  hardcoded tag is `"[Api - GetById]"`, which doesn't even match `.WithName("GetQuoteById")` — the two
  have already drifted apart from each other, not just from a shared source. Fixing this is a genuine
  bug fix, not just a DRY cleanup, for this one call site.

`NotificationEndpoints.cs` (added by #278, same branch) uses neither `LogPageQuery` nor `LogIdQuery` —
not affected by this duplication, no change needed there.

No test or doc anywhere references the two old `operationId` strings being renamed (`GetImportBatches`,
`GetFileResources`) outside the two endpoint files themselves — confirmed by a full-repo grep. `docs/api-
endpoints.md` never lists `operationId`/`WithName` values at all, so it needs no naming-specific edit
beyond what Step 1 adds.

---

## Approach

### Step 1's documented convention (new CLAUDE.md section)

Added immediately after the existing "Masterdata reference shape" section (the issue's own suggested
location, "alongside the existing masterdata routing convention"):

```
### Endpoint naming convention (WithName/WithSummary)

Every endpoint's `.WithName(...)`/`.WithSummary(...)` pair follows one of three shapes, chosen by what
kind of operation the endpoint performs:

- **List** (returns a full/paginated collection): `WithName("GetAllX")` + `WithSummary("List x")`
  (lowercase plural noun).
- **GetById** (returns a single item by id): `WithName("GetXById")` + `WithSummary("X by ID")`
  (capitalised `ID`).
- **Action** (does something — import, decide, apply, discard, reset, dismiss, etc.): `WithName` is a
  PascalCase verb+object matching the action; `WithSummary` is an imperative-verb-first phrase.

A genuinely different operation shape (e.g. `GetRandomQuotes`/`SearchQuotes`, which don't return a full
list; `ImportQuotes`, one handler deliberately doing two things) may deviate — the point of a standard
is to deviate only with a reason, documented at the call site.

**`WithName`'s value becomes the OpenAPI `operationId`, which a generated client can depend on —
renaming it is a breaking change.** Treat it accordingly: batch renames into one release, call them out
in that release's changelog highlights.

**Every endpoint's `.WithName(...)` value is held in a `private const string` referenced by both the
`.WithName(...)` call and its own logging tag** (`logger.LogPageQuery($"[Api - {ConstName}]", ...)` —
a `const string` composed entirely of other `const string`s is itself a compile-time constant in C#, so
this interpolation costs nothing at runtime and never risks CA1873). This is what actually prevents the
"same name spelled out twice with no compiler link" class of drift #269 introduced and #279 fixed.
```

### Step 2's fixes

| File | Change |
|---|---|
| `PersonEndpoints.cs` | `WithSummary("All people (paginated)")` → `"List people"` |
| `QuoteEndpoints.cs` | `WithSummary("All quotes (paginated)")` → `"List quotes"` |
| `SeriesEndpoints.cs` | `WithSummary("List Series")` → `"List series"` |
| `ImportBatchEndpoints.cs` | `WithName("GetImportBatches")` → `"GetAllImportBatches"` (**breaking**); `WithSummary("Import batch by id")` → `"Import batch by ID"` |
| `ImportFileResourceEndpoints.cs` | `WithName("GetFileResources")` → `"GetAllFileResources"` (**breaking**); `WithSummary("Captured import file by id")` → `"Captured import file by ID"` |

### Step 4's const-per-endpoint pattern (applied to the 9 files)

Example (`CharacterEndpoints.cs`):
```csharp
private const string GetAllCharactersName = "GetAllCharacters";
private const string GetCharacterByIdName = "GetCharacterById";
```
`.WithName(GetAllCharactersName)` / `.WithName(GetCharacterByIdName)`, and
`logger.LogPageQuery($"[Api - {GetAllCharactersName}]", page, pageSize)` /
`logger.LogIdQuery($"[Api - {GetCharacterByIdName}]", id)`. Verified directly (throwaway console
project) that a `$"..."` interpolation of only `const string` operands is itself a compile-time
`const string` in this SDK — no runtime string-building, so no new CA1873 exposure from this change.

`QuoteEndpoints.cs`'s `GetById` gets the same treatment — its tag changes from the already-wrong
literal `"[Api - GetById]"` to the correct `$"[Api - {GetQuoteByIdName}]"` (`"[Api - GetQuoteById]"`),
fixing the drift noted above.

---

## Steps

### 1. Document the convention in `CLAUDE.md`
**Status:** ✅ Done

### 2. Fix the 5 List/GetById deviations (2 breaking `WithName` renames)
**Status:** ✅ Done

### 3. Confirm action-endpoint standard (no code changes — audit already complete above)
**Status:** ✅ Done — zero deviations found

### 4. Const-per-endpoint pattern across the 9 affected files
**Status:** ✅ Done

Grep sweep confirmed zero remaining `Log(Page|Id)Query("[Api`/`LogIdWithLang("[Api` literal calls
anywhere in `src/Quotinator.Api/Endpoints/`.

### 5. `docs/api-endpoints.md` / stale-reference sweep
**Status:** ✅ Done

`docs/api-endpoints.md` never lists `operationId`/`WithName` values at all (confirmed by reading it in
full) — no edit needed there specifically for the renames. A full-repo grep for the two old
`operationId` strings (excluding their own `...ById` siblings, which are unrelated names that happen to
share a prefix) found no reference anywhere outside the two endpoint files' own new values.

### 6. Changelog (breaking-change highlight)
**Status:** ✅ Done

Added a `highlights` entry (plain-English, explains the breaking change is only relevant to a generated
API client) plus two `changed` entries (the naming standardisation itself, and the log-tag
const-sharing fix) to `changelog.en/nl/de.json`'s `unreleased` section, lockstep. Regenerated
`CHANGELOG.md`/`addon/CHANGELOG.md`/`addon-beta/CHANGELOG.md`.

### 7. Smoke-test suite sweep for stale operation-id references
**Status:** ✅ Done

No existing section referenced either old `operationId` string. Added new section 34 — live checks that
both renamed `operationId`s appear (and the old ones don't), every List summary reads consistently, both
breaking-rename `GetById` summaries capitalise `ID`, and `GET /quotes/{id}`'s log line now reads
`[Api - GetQuoteById]`.

### 8. Full verification (T1, T2)
**Status:** ✅ Done

**T2 confirmed (2026-08-09):** `docker build` succeeded; fresh container's live `/openapi/v1.json`
confirmed both breaking renames present (`GetAllImportBatches`, `GetAllFileResources`) and both old
names absent; every List summary reads `"List x"` consistently (13 distinct endpoints checked,
including the three previously-deviant `List people`/`List quotes`/`List series`); both breaking-rename
`GetById` summaries read `"...by ID"` with a capitalised `ID`. `GET /api/v1/quotes/{id}`'s container log
line confirmed reading `[Api - GetQuoteById]`, not the old mismatched `[Api - GetById]`. Full test suite
re-confirmed green: 1074 Data.Tests + 1437 Core.Tests + 660 Api.Tests, 0 failures (unchanged Api.Tests
count from before this issue — no test depended on any of the renamed/changed text).

**T1 confirmed (2026-08-09):** developer's own Visual Studio run — clean startup against a real
populated database (schema v6, data v8, 799 quotes), no errors, backup completed normally.

### 9. Notify operators via #278's notification mechanism (developer-flagged gap, 2026-08-09)
**Status:** ✅ Done

**Found live — the whole point of this issue's dependency on #278 had gone unfulfilled.** The issue's
own background text, and `overview.md`'s dependency map, both state the breaking `operationId` renames
should not ship before the notification mechanism exists *and is actually used* to announce them. Steps
1–8 above fixed every naming deviation but never wrote a real notification — #278 built the mechanism
but shipped with no real producer (by its own explicit scoping), and this issue was meant to be that
first producer. Caught by the developer, not by this session's own review.

New `Quotinator.Api.Startup.NotificationSeeding.SeedOnceAsync(reader, writer, type, dedupeKey, message,
trigger)` — a small, genuinely reusable one-time-seed helper (checks the full notification history for
an existing message containing `dedupeKey` before writing, so a repeated call across restarts never
duplicates the row). New `NotificationSeedingTests` (3 tests: writes when no match, skips when a match
exists, writes against an empty history) plus a `WrittenMessages` list added to the existing
`FakeNotificationWriter` test double so a write can be asserted without a real database.

**Found live during this step's own full-suite check — a real regression, caught before it shipped.**
The first wiring placed the `SeedOnceAsync` call *inside* `Program.cs`'s existing DB-init `try` block,
sharing its `catch` with `dbInitializer.InitialiseAsync()`. 336 of 663 `Quotinator.Api.Tests` immediately
failed: many `WebApplicationFactory`-based tests register `NoOpDatabaseInitializer` (skips real
migrations) but still resolve the *real* `NotificationReader`/`NotificationWriter` via `Program.cs`'s own
DI, so the seeding query hit a nonexistent `System_Notification` table, threw, and — sharing the DB-init
`catch` — marked the entire app unhealthy, turning every one of those tests' expected `200`s into `503`.
Fixed by moving the call **outside** the critical DB-init `try`/`catch` entirely, gating it on
`dbHealth.IsHealthy`, and wrapping it in its own non-fatal `try`/`catch` that only logs a warning —
writing an announcement notification is inherently non-critical and must never be able to take down
startup health, unlike schema initialisation itself.

**T2 re-confirmed (2026-08-09, after the fix):** rebuilt image; `GET /api/v1/notifications` after first
startup shows exactly one `Warning` notification naming both renamed operation IDs, 30-day expiry (the
configured default). Restarted the same container — `totalCount` stayed `1`, same notification id —
confirms the dedupe check works against a real database, not just the fake-backed unit tests. Full test
suite re-confirmed green: 1074 Data.Tests + 1437 Core.Tests + 664 Api.Tests, 0 failures.

Added a dedicated regression test, `ProgramNotificationSeedingRegressionTests.
Health_NoOpDatabaseInitializer_StaysHealthyDespiteMissingNotificationTable` — deliberately does not
override `INotificationReader`/`INotificationWriter` (unlike most endpoint test files), so it exercises
the exact failure path this bug took, and would fail again if a future change reintroduced it.

**T1 confirmed (2026-08-09):** developer's own Visual Studio run, against a real populated database
(not a fresh/empty one — schema v6, data v8, 799 quotes, 461 sources, existing history). The startup
success modal shows the notification exactly as intended: `Warning` type, message naming both renamed
operation IDs, 30-day expiry, `Active` status — confirming the seeder fires correctly against a
database that already has prior notification history, not just a fresh one.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | CLAUDE.md documents the List/GetById/Action naming convention | Manual | New "Endpoint naming convention" section present |
| 2 | ✅ | `PersonEndpoints.cs` summary fixed | Live | `/openapi/v1.json`'s `GetAllPeople` summary reads `"List people"` |
| 3 | ✅ | `QuoteEndpoints.cs` `GetAllQuotes` summary fixed | Live | `/openapi/v1.json`'s `GetAllQuotes` summary reads `"List quotes"` |
| 4 | ✅ | `SeriesEndpoints.cs` summary fixed | Live | `/openapi/v1.json`'s `GetAllSeries` summary reads `"List series"` |
| 5 | ✅ | `ImportBatchEndpoints.cs` renamed to `GetAllImportBatches`, summary fixed | Live | `/openapi/v1.json` shows `operationId: GetAllImportBatches`; GetById summary reads `"Import batch by ID"` |
| 6 | ✅ | `ImportFileResourceEndpoints.cs` renamed to `GetAllFileResources`, summary fixed | Live | `/openapi/v1.json` shows `operationId: GetAllFileResources`; GetById summary reads `"Captured import file by ID"` |
| 7 | ✅ | All 9 log-tag call sites use the const-per-endpoint pattern, no literal duplication remains | Manual | Grep sweep: zero `Log(Page\|Id)Query\("\[Api` / `LogIdWithLang\("\[Api` literal calls remain |
| 8 | ✅ | `QuoteEndpoints.cs`'s `GetById` log tag now matches its `WithName` | Live | Docker log line for `GET /quotes/{id}` confirmed reading `[Api - GetQuoteById]` |
| 9 | ✅ | No stale old-`operationId` references remain in docs/tests | Manual | Full-repo grep for `GetImportBatches`/`GetFileResources` (excluding `...ById`) found only the two endpoint files' own new names |
| 10 | ✅ | Changelog highlights the breaking `operationId` renames | Manual | `changelog.en/nl/de.json` unreleased entries added, lockstep |
| 11 | ✅ | The smoke-test suite has no stale operation-id references | Manual | Grep sweep clean; new section 34 added. The suite is now `docs/automated-testing/`, whose README maps the old section numbers |
| 12 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 13 | ✅ | Full test suite green | Build | 1074 Data.Tests + 1437 Core.Tests + 664 Api.Tests, 0 failures |
| 14 | ✅ | T1 (developer's own Visual Studio run) | Live | Clean startup, notification visible in success modal with correct message/expiry/status |
| 15 | ✅ | T2 (Docker smoke tests) | Live | Section 34 pass, 2026-08-09 — see Step 8 |
| 16 | ✅ | A real notification announcing the breaking renames is actually written, once, idempotently | Unit test + Live | `NotificationSeedingTests` (3 tests); Docker: `GET /api/v1/notifications` shows exactly one matching row after first startup and after a restart |
| 17 | ✅ | A failure to seed the announcement notification never marks the app unhealthy | Unit test | `ProgramNotificationSeedingRegressionTests.Health_NoOpDatabaseInitializer_StaysHealthyDespiteMissingNotificationTable` (dedicated regression test, deliberately doesn't override `INotificationReader`/`Writer`) |

---

## Relationship to existing issues

- **#269** — introduced the log-tag duplication this issue's Step 4 fixes.
- **#278** — must land first; its notification mechanism is the intended vehicle for announcing this
  issue's breaking `operationId` renames to operators.
- **#276** — grandparent tracking issue (via #278).
