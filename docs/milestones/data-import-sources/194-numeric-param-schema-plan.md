# #194 — Numeric query params published to the OpenAPI spec as string

**Status:** Planning
**GitHub issue:** #194
**Tiers required:** T1, T2
**Depends on:** none

---

## Spec requirements (from the GitHub issue)

1. Generalise `YearParameterSchemaTransformer` to patch every numeric `string?`-bound query parameter
   to `integer` — covering `page`, `pageSize`, `n`, `limit` — not only the four year params. Rename to
   match the widened remit. Keep the existing path-scoping.
2. Preserve published nullability (`null|integer` where optional) rather than flattening to bare
   `integer`.
3. Update `CLAUDE.md`'s "Year parameter binding pattern" — its rule says to register the endpoint
   *path*, which was followed and was still insufficient; the param *name* must be registered too.
4. Keep the existing trailing-slash regression guard green under the rename.

---

## Background — why this issue exists

Found while reviewing #183's own premise (2026-07-16). `CLAUDE.md` requires numeric query params be
declared `string?` and parsed with `int.TryParse`, so an invalid value yields a clean 422 rather than
the framework binder's bare 400 — and to compensate for the resulting `type: string` in the generated
spec, says to add the endpoint path to the year-param schema transformer. The paths were added. But
`YearParameterSchemaTransformer` only patches parameters whose *name* appears in `YearParamNames`, so
every other numeric `string?` param on those same paths is silently missed.

Blocks #195, which converts `/admin/audit` and `/import/actions` to `string?` binding — without this
fix their published type would regress from `integer|string` to bare `string`.

---

## Steps

### 1. Red tests

**Status:** Not started.

Write the failing tests against the current transformer before any change (per this project's
red-before-fix rule): `page`/`pageSize` on `api/v1/quotes`, `n` on `api/v1/quotes/random`, `limit` on
`api/v1/quotes/search`. Confirm each is genuinely red — the existing
`YearParameterSchemaTransformerTests` is the template for driving the transformer in isolation.

### 2. Generalise and rename the transformer

**Status:** Not started.

Widen the param-name set beyond the four year params and rename the class to match its remit. Keep
`YearFilterPaths`' path-scoping approach and its trailing-slash handling intact — that guard exists
because `group.MapGet("/", GetAll)` reports `api/v1/quotes/` with a trailing slash while the other two
paths do not, and missing it silently disabled the transformer for `GET /api/v1/quotes` once already
(see the class's own remarks).

Preserve per-param nullability: the year params publish `null|integer` today and must keep doing so.

### 3. Fix CLAUDE.md's rule

**Status:** Not started.

The "Rules for adding new numeric query parameters" list says to add the endpoint path to the
transformer. That instruction is what was followed, and it was insufficient — the param name must be
registered too. Correct the rule and point it at the renamed transformer.

### 4. Verify

**Status:** Not started.

Full suite green, 0 warnings. T2 confirms the live spec via `GET /openapi/v1.json`.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `page`/`pageSize` on `/quotes` publish as integer | Unit test | `Quotinator.Api.Tests.OpenApi.NumericParameterSchemaTransformerTests.PageAndPageSize_OnQuotes_PatchedToInteger` — starts red |
| 2 | ❌ | `n` on `/quotes/random` publishes as integer | Unit test | `NumericParameterSchemaTransformerTests.N_OnQuotesRandom_PatchedToInteger` — starts red |
| 3 | ❌ | `limit` on `/quotes/search` publishes as integer | Unit test | `NumericParameterSchemaTransformerTests.Limit_OnQuotesSearch_PatchedToInteger` — starts red |
| 4 | ❌ | An optional param keeps `null\|integer`, not bare `integer` | Unit test | `NumericParameterSchemaTransformerTests.OptionalParam_RetainsNullableInteger_NotBareInteger` — starts red |
| 5 | ❌ | Year params still patched after the rename | Unit test | `NumericParameterSchemaTransformerTests.YearParams_StillPatched_AfterRename` — regression |
| 6 | ❌ | The trailing-slash guard survives the rename | Unit test | `NumericParameterSchemaTransformerTests.Type_OnQuotesWithTrailingSlash_PatchedOnItemsSchema` — regression |
| 7 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 8 | ❌ | T1 — app starts in Visual Studio, Scalar renders the affected params as integer | Live (T1) | Developer to confirm at `/scalar/v1` |
| 9 | ❌ | T2 — the live spec types every numeric param as integer | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + `curl -s http://localhost:8080/openapi/v1.json`, inspecting `page`/`pageSize`/`n`/`limit` |

---

## Notes

T1 and T2 are both required: this changes what the published OpenAPI document and the Scalar UI show,
which no unit test observes end to end — the transformer tests drive it in isolation, not through the
real document pipeline.
