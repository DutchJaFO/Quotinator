# #152 — Review endpoint grouping: split Admin / Quote / Import

**Status:** Waiting for release
**GitHub issue:** #152
**Tiers required:** T1, T2
**Depends on:** #149 (introduced the `Import` tag and `/api/v1/import` route group this issue completes)

---

## Scope note (recorded before implementation)

#149 already introduced `ApiTags.Import` and a `/api/v1/import` route group for its new
conflict-review endpoints, but left the original `POST /api/v1/quotes/import` and
`POST /api/v1/quotes/import/preview` endpoints under the `Quotes` tag in
`QuoteEndpoints.cs` — the exact mismatch #152 was filed to close. This milestone
(`Data Import & Sources`, #10) has not shipped a release yet, so moving the route is
free: both paths appear only in `changelog.en.json`'s `unreleased` section, never in
`CHANGELOG.md`.

While planning the move, two adjacent gaps in the same tag/transformer code were found
and confirmed in scope for this issue by the user, rather than deferred:

1. `document.Tags` in `Program.cs` never registered `ApiTags.Import` at all.
2. The OpenAPI security-requirement operation transformer only matched the `Admin` tag by
   name, so every `Import`-tagged endpoint that actually requires `X-Api-Key`
   (`.../conflicts/{id}/decide`, `.../undo`, `.../apply`, and the two endpoints moved
   here) never showed a security requirement in the spec/Scalar UI.

Also confirmed: while renaming the Blazor `RestApi.razor` "Protected quote endpoints"
section to "Import", fill in the conflict-review rows #149 never added to that page.

---

## 1. Move `/quotes/import` + `/quotes/import/preview` into `ImportEndpoints.cs`

**Status:** ✅ Done

`ImportDescription`, the two `MapPost` registrations, and the `HandleImportAsync` helper
moved from `QuoteEndpoints.cs` into `ImportEndpoints.cs`, mapped onto the file's existing
`adminGroup` (already `/api/v1/import` + `RateLimitPolicies.Admin` +
`AddEndpointFilter<AdminApiKeyFilter>()`) as `adminGroup.MapPost("/", ...)` and
`adminGroup.MapPost("/preview", ...)` — the same `MapGet("/", ...)` pattern
`QuoteEndpoints.GetAll` already uses for `GET /api/v1/quotes`. `ImportEndpoints` gained
its own private `Log` marker class for `ILogger<Log>`. Unused usings
(`Microsoft.AspNetCore.Mvc`, `Quotinator.Data.Import`, `Quotinator.Engine.Services`,
`Quotinator.Api.Endpoints.Filters`) removed from `QuoteEndpoints.cs`.

## 2. Rename `ApiRoutes` constants

**Status:** ✅ Done

`ApiRoutes.QuotesImport` → `Import` (`/api/v1/import`), `QuotesImportPreview` →
`ImportPreview` (`/api/v1/import/preview`) — matching the existing `ImportConflicts*`
naming style in the same file.

## 3. Fix the security-requirement transformer + register the `Import` tag

**Status:** ✅ Done

New `AdminApiKeyRequiredMarker` singleton (`Endpoints/Filters/`), attached via
`.WithMetadata(AdminApiKeyRequiredMarker.Instance)` alongside every
`.AddEndpointFilter<AdminApiKeyFilter>()` group registration (`AdminEndpoints.cs`'s
`adminGroup`, `ImportEndpoints.cs`'s `adminGroup`). New
`OpenApi/AdminApiKeySecurityTransformer.cs` (`IOpenApiOperationTransformer`) checks
`context.Description.ActionDescriptor.EndpointMetadata` for the marker instead of the
tag name, replacing the inline lambda in `Program.cs`. `document.Tags` gained the
missing `ApiTags.Import` entry.

## 4. `RestApi.razor` + i18n

**Status:** ✅ Done

Renamed `QuotesImport*` keys to `Import*` (`ImportEndpointsHeading/Description`,
`ImportLabel`, `ImportPreviewLabel`) in all three `UI.{en-GB,nl,de}.json` files, updated
heading/description text, and added four new keys/rows for the conflict endpoints
(`ImportConflictsLabel`, `...DecideLabel`, `...UndoLabel`, `...ApplyLabel`) using the
already-existing `ApiRoutes.ImportConflicts*` constants.

## 5. `README.md` / `addon/DOCS.md`

**Status:** ✅ Done

Updated the two moved rows' paths and reordered both tables so all `Import`-tagged rows
(import, preview, then the four conflict rows) sit together, ahead of the admin rows.

## 6. Tests

**Status:** ✅ Done

`ImportEndpointTests.cs`: all `/api/v1/quotes/import`(`/preview`) literals updated to the
new paths (prefix replacement also correctly updated the `/preview` variant). New
`AdminApiKeySecurityTransformerTests.cs` (mirrors
`YearParameterSchemaTransformerTests.cs`'s structure): security requirement set when the
marker metadata is present, absent otherwise (marker missing, or unrelated metadata
present). `QuoteImportServiceTests.cs`'s doc-comment route reference also corrected.

## 7. Docs and changelog

**Status:** ✅ Done

`CLAUDE.md`'s Pre-Push Checklist curl example and `docs/vocabulary.md`'s CSV entry fixed
to the new path. Both moved endpoints exist only in `changelog.en.json`'s `unreleased`
section (never shipped) — the two `added` bullets rewritten in place to the new paths
(lockstep in `nl.json`/`de.json`); `152` added to `unreleased.issues`; one `changed`
bullet added (route move) and one `fixed` bullet added (security-requirement transformer
fix), lockstep across all three locales; `CHANGELOG.md`/`addon/CHANGELOG.md` regenerated
via `scripts/changelog.csx`.

## 8. Plan doc, solution, and overview

**Status:** ✅ Done

This plan doc created; added to `Quotinator.slnx` under
`/docs/milestones/data-import-sources/`; `overview.md`'s `#152` row updated to link this
plan doc.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Build clean | Live | `dotnet build --configuration Release` → 0 Warning(s), 0 Error(s) |
| 2 | ✅ | `/quotes/import` and `/quotes/import/preview` endpoints behave identically at their new `/api/v1/import` and `/api/v1/import/preview` paths (401/422/501/200 cases) | Unit test | `ImportEndpointTests` — all 12 test methods pass against the new paths |
| 3 | ✅ | Conflict-review endpoints (#149) unaffected by the group restructuring | Unit test | `ImportConflictEndpointsTests` — all 13 test methods pass |
| 4 | ✅ | OpenAPI security requirement is set exactly when `AdminApiKeyRequiredMarker` metadata is present, not by tag name | Unit test | `AdminApiKeySecurityTransformerTests.MarkerPresent_SetsApiKeySecurityRequirement`, `...MarkerAbsent_LeavesSecurityNull`, `...OtherMetadataPresent_WithoutMarker_LeavesSecurityNull` |
| 5 | ✅ | i18n key rename kept all three locale files complete | Unit test | `TranslationCompletenessTests.AllLanguageFiles_HaveExactlyTheSameKeysAsBaseline`, `...HaveNoEmptyValues` |
| 6 | ✅ | Changelog JSON stays schema-valid after the edits | Unit test | `ChangelogSchemaTests` — all 8 test methods pass |
| 7 | ✅ | T1 — app starts in VS (IIS Express profile) without error; `/openapi/v1.json` registers the `Import` tag and marks all 5 write operations (`POST /import`, `.../preview`, `.../conflicts/{id}/decide`, `.../undo`, `.../apply`) with the `X-Api-Key` security requirement and the 1 public read (`GET /conflicts`) without it; `POST /api/v1/quotes/import` (old path) no longer accepts `POST` (`405` — falls through to the `/quotes/{id}` pattern, which only defines `GET`, rather than a bare `404`); `/rest-api` page's rendered HTML contains the renamed Import section heading and all six endpoint rows with correct paths | Live | Started via Visual Studio IIS Express profile (`http://localhost:56326`); `GET /api/v1/import/conflicts` → `200`; `POST /api/v1/import` (multipart, no key) → `401`; `POST /api/v1/quotes/import`(`/preview`) → `405`; `/openapi/v1.json` inspected directly for tag/security correctness; `/rest-api` HTML fetched and checked for all six new row strings. `/scalar/v1` was not visually screenshotted (no Chrome extension connected this session) — it renders directly from the verified spec, so no separate risk |
| 8 | ✅ | T2 — Docker smoke test: `POST /api/v1/import` with a multipart file + `X-Api-Key` succeeds; without the key returns `401`; old `/api/v1/quotes/import` no longer works; full decide/undo/apply conflict-review cycle works against the new routes | Live | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; container started clean (fresh-database baseline path, sources auto-refreshed, 788 quotes seeded), zero errors in logs throughout. `GET /health`, `/version`, `/quotes/random`, `/quotes/search` (default and `field=source`) all `200`. `POST /api/v1/quotes/import` → `405` (old path); `POST /api/v1/import` without key → `401`, with key + multipart file → succeeds. Forced a `review`-policy re-import of `quotinator-curated.json` → 2 genuine pending conflicts; ran decide → `status=decided` → undo → `status=pending` (back to 2) → decide again on both → `apply?batchId=<uppercase batchId>` → `200` → `status=resolved` shows both; `GET /quotes/{id}` confirmed the record still reads correctly after apply. One test-script mistake along the way (passing the lowercase `batchId` from the `POST /import` response instead of the uppercase one `GET /conflicts` returns) produced a misleading vacuous `200` on an early `apply` call matching zero conflicts — not a #152 regression; corrected by using the uppercase id per `CLAUDE.md`'s documented convention, after which the full sequence behaved exactly as expected |
