# #279 — Standardise endpoint naming (WithName/WithSummary) across CRUD and action endpoints

**Status:** Planning
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
**Status:** ⬜ Not started

### 2. Fix the 5 List/GetById deviations (2 breaking `WithName` renames)
**Status:** ⬜ Not started

### 3. Confirm action-endpoint standard (no code changes — audit already complete above)
**Status:** ⬜ Not started

### 4. Const-per-endpoint pattern across the 9 affected files
**Status:** ⬜ Not started

### 5. `docs/api-endpoints.md` / stale-reference sweep
**Status:** ⬜ Not started

### 6. Changelog (breaking-change highlight)
**Status:** ⬜ Not started

### 7. `docs/smoke-tests.md` sweep for stale operation-id references
**Status:** ⬜ Not started

### 8. Full verification (T1, T2)
**Status:** ⬜ Not started

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | CLAUDE.md documents the List/GetById/Action naming convention | Manual | New section present, matches Step 1 |
| 2 | ⬜ | `PersonEndpoints.cs` summary fixed | Live | `/openapi/v1.json`'s `GetAllPeople` summary reads `"List people"` |
| 3 | ⬜ | `QuoteEndpoints.cs` `GetAllQuotes` summary fixed | Live | `/openapi/v1.json`'s `GetAllQuotes` summary reads `"List quotes"` |
| 4 | ⬜ | `SeriesEndpoints.cs` summary fixed | Live | `/openapi/v1.json`'s `GetAllSeries` summary reads `"List series"` |
| 5 | ⬜ | `ImportBatchEndpoints.cs` renamed to `GetAllImportBatches`, summary fixed | Live | `/openapi/v1.json` shows `operationId: GetAllImportBatches`; GetById summary reads `"Import batch by ID"` |
| 6 | ⬜ | `ImportFileResourceEndpoints.cs` renamed to `GetAllFileResources`, summary fixed | Live | `/openapi/v1.json` shows `operationId: GetAllFileResources`; GetById summary reads `"Captured import file by ID"` |
| 7 | ⬜ | All 9 log-tag call sites use the const-per-endpoint pattern, no literal duplication remains | Manual | Grep sweep: no `Log(Page\|Id)Query\("\[Api` / `LogIdWithLang\("\[Api` literal calls remain in the 9 files |
| 8 | ⬜ | `QuoteEndpoints.cs`'s `GetById` log tag now matches its `WithName` | Live | Docker log line for `GET /quotes/{id}` reads `[Api - GetQuoteById]`, not `[Api - GetById]` |
| 9 | ⬜ | No stale old-`operationId` references remain in docs/tests | Manual | Full-repo grep for `GetImportBatches`/`GetFileResources` (excluding `...ById`) finds only the two endpoint files' own new names |
| 10 | ⬜ | Changelog highlights the breaking `operationId` renames | Manual | `changelog.en/nl/de.json` unreleased entry present, lockstep |
| 11 | ⬜ | `docs/smoke-tests.md` has no stale operation-id references | Manual | Grep sweep of `docs/smoke-tests.md` |
| 12 | ⬜ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 13 | ⬜ | Full test suite green | Build | `dotnet test --configuration Release` — all pass |
| 14 | ⬜ | T1 (developer's own Visual Studio run) | Live | Clean start, no error |
| 15 | ⬜ | T2 (Docker smoke tests) | Live | Full `docs/smoke-tests.md` pass + manual Scalar UI check that every summary reads consistently |

---

## Relationship to existing issues

- **#269** — introduced the log-tag duplication this issue's Step 4 fixes.
- **#278** — must land first; its notification mechanism is the intended vehicle for announcing this
  issue's breaking `operationId` renames to operators.
- **#276** — grandparent tracking issue (via #278).
