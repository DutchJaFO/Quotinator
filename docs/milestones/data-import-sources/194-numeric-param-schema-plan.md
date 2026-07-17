# #194 — Numeric query params published to the OpenAPI spec as string

**Status:** Waiting for release
**GitHub issue:** #194
**Tiers required:** T1, T2
**Depends on:** none

---

## Spec requirements (from the GitHub issue, corrected during planning)

1. Generalise `YearParameterSchemaTransformer` to patch every numeric `string?`-bound query parameter
   to `integer` — covering `page`, `pageSize`, `n`, `limit` — not only the four year params. Rename to
   `NumericParameterSchemaTransformer` to match the widened remit. Keep the existing path scoping.
2. Preserve published nullability (`null|integer` where optional) rather than flattening to bare
   `integer`.
3. Publish the correct `default` for parameters that have a real one — `page` (1), `pageSize` (20),
   `n` (1), `limit` (20) — sourced from a single new `Quotinator.Constants.Api.QueryParamDefaults`
   class shared by the `[DefaultValue(...)]` attribute, the handler's own fallback, and the
   transformer's registry, so the three copies cannot drift independently. **`n` publishes a default
   of 1** — the issue originally said it must not, which was wrong: `GetRandom` already defaults to
   `count = 1` and its own `[Description]` says "Omit for a single random quote", so the default is
   real and merely wasn't machine-readable before this issue.
4. Update `CLAUDE.md`'s "Numeric query parameter binding pattern" section (renamed from "Year
   parameter binding pattern") — its old rule said to register the endpoint *path*, which was
   followed and was still insufficient; the param *name* must be registered too, alongside its
   default (or `null` for none).
5. Keep the existing trailing-slash regression guard green under the rename, and extract it to a
   small shared `ScopedPath.From()` helper now used by both `NumericParameterSchemaTransformer` and
   `EnumParameterSchemaTransformer`.

**Two claims in the original issue body were wrong and are corrected here, not carried forward:**
- `docs/openapi.md` does not exist and has zero references to the transformer — the issue claimed it
  needed updating; there was nothing to update.
- Only `api/v1/quotes`, `api/v1/quotes/random`, and `api/v1/quotes/search` are `string?`-bound today.
  `api/v1/admin/audit` and `api/v1/import/actions` are still `int`-bound — out of scope until #195
  converts them — so this issue does not touch "all five affected paths".

---

## Background — why this issue exists

Found while reviewing #183's own premise (2026-07-16). `CLAUDE.md` requires numeric query params be
declared `string?` and parsed with `int.TryParse`, so an invalid value yields a clean 422 rather than
the framework binder's bare 400 — and to compensate for the resulting `type: string` in the generated
spec, says to add the endpoint path to the year-param schema transformer. The paths were added. But
`YearParameterSchemaTransformer` only patched a parameter whose *name* also appeared in
`YearParamNames`, so every other numeric `string?` param on those same paths was silently missed —
confirmed live: `page` on `/quotes` published `"type": "string"` with no `default`, while the same
param name on the still-`int`-bound `/admin/audit` published `"type": ["integer","string"], "default": 1`
correctly.

Blocks #195, which converts `/admin/audit` and `/import/actions` to `string?` binding — without this
fix their published type would regress from `integer|string` to bare `string`.

---

## Steps

### 1. Red tests

**Status:** ✅ Done.

Added 8 failing tests to the (then still year-named) transformer test file: 4 type-patch assertions
for `page`/`pageSize` on `api/v1/quotes`, `n` on `api/v1/quotes/random`, `limit` on
`api/v1/quotes/search`, and 4 matching default-publishing assertions. Confirmed genuinely red
(8 failed, 11 pre-existing passed) before touching the transformer.

### 2. Add `QueryParamDefaults` and generalise/rename the transformer

**Status:** ✅ Done.

- New `src/Quotinator.Constants/Api/QueryParamDefaults.cs`: `Page = 1`, `PageSize = 20`,
  `SearchLimit = 20`, `RandomCount = 1`.
- `git mv` both the transformer and its test file to `NumericParameterSchemaTransformer(Tests).cs`,
  preserving blame.
- Replaced the flat `YearFilterPaths`/`YearParamNames` pair with a nested
  `IReadOnlyDictionary<string, IReadOnlyDictionary<string, int?>> NumericParamsByPath` (path → param
  name → default-or-null), mirroring `EnumParameterSchemaTransformer`'s registry shape. Sets
  `schema.Type = JsonSchemaType.Integer | JsonSchemaType.Null` and, when a default is registered,
  `schema.Default = JsonValue.Create(value)`.
- Extracted the trailing-slash handling to `ScopedPath.From()` (new file), now used by both
  `NumericParameterSchemaTransformer` and `EnumParameterSchemaTransformer`.

### 3. Wire `QuoteEndpoints.cs` to the shared constants

**Status:** ✅ Done.

All four `[DefaultValue(...)]` attributes and their matching handler fallback variables
(`pageValue`, `pageSizeValue`, `limitValue`, `count`) now reference `QueryParamDefaults.*` instead of
duplicated literals. `n` gained `[DefaultValue(QueryParamDefaults.RandomCount)]`, which it did not
carry before.

### 4. Fix CLAUDE.md's rule and update reference sites

**Status:** ✅ Done.

`CLAUDE.md`'s section renamed "Numeric query parameter binding pattern"; its rule now says to
register **both** the path and the parameter name (with default) in
`NumericParameterSchemaTransformer.NumericParamsByPath`, and to add a shared constant to
`QueryParamDefaults` when the parameter has one. Also updated: two `<see cref>` doc comments
(`EnumParameterSchemaTransformer.cs`, `ImportModelSchemaTransformer.cs`), one test comment
(`EnumParameterSchemaTransformerTests.cs`), the live pointer in
`152-endpoint-grouping-plan.md:93`, and the two historical-claim sites in
`126-validation-status-codes-plan.md:19,50` (annotated as historical, not rewritten).

### 5. Verify

**Status:** ✅ Done — T1 and T2 both confirmed.

Full suite green (9/9 projects, 1261/1261 tests), 0 warnings, 0 errors. T2 confirmed the live spec via
`GET /openapi/v1.json`: `page`/`pageSize`/`n`/`limit` all publish `["null","integer"]` with the
correct `default`; `yearFrom` unaffected (still `["null","integer"]`, still no `default`); invalid
values (`?page=abc`, `?n=abc`, `?limit=abc`) still 422 with the same detail messages as before —
binding behaviour is unchanged, only the published schema.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `page`/`pageSize` on `/quotes` publish as integer | Unit test | `NumericParameterSchemaTransformerTests.Page_OnPaginatedList_PatchedToInteger`, `PageSize_OnPaginatedList_PatchedToInteger` |
| 2 | ✅ | `n` on `/quotes/random` publishes as integer | Unit test | `NumericParameterSchemaTransformerTests.N_OnRandom_PatchedToInteger` |
| 3 | ✅ | `limit` on `/quotes/search` publishes as integer | Unit test | `NumericParameterSchemaTransformerTests.Limit_OnSearch_PatchedToInteger` |
| 4 | ✅ | An optional param keeps `null\|integer`, not bare `integer` | Unit test | `NumericParameterSchemaTransformerTests.OptionalParam_RetainsNullableInteger_NotBareInteger` |
| 5 | ✅ | `page`, `pageSize`, `n`, `limit` publish their correct `default` | Unit test | `NumericParameterSchemaTransformerTests.Page_OnPaginatedList_PublishesDefaultOfOne`, `PageSize_..._PublishesDefaultOfTwenty`, `N_OnRandom_PublishesDefaultOfOne`, `Limit_OnSearch_PublishesDefaultOfTwenty` |
| 6 | ✅ | Year params still patched, and still publish no default, after the rename | Unit test | `NumericParameterSchemaTransformerTests.YearFrom_OnRandom/OnSearch/OnPaginatedList_PatchedToInteger` (+ siblings), `YearFrom_DoesNotPublishADefault` |
| 7 | ✅ | The trailing-slash guard survives the rename, for both a year param and a newly-registered one | Unit test | `NumericParameterSchemaTransformerTests.YearFrom_OnPaginatedListWithTrailingSlash_PatchedToInteger`, `Page_OnPaginatedListWithTrailingSlash_PatchedToInteger` |
| 8 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 9/9 test projects, 1261/1261 passed, 0 warnings, 0 errors |
| 9 | ✅ | T1 — app starts in Visual Studio, Scalar renders the affected params as integer with correct defaults | Live (T1) | Developer confirmed (2026-07-17): `n=r`/`year=fff` → 422, valid `n=1` requests → 200, `page=0`/`pageSize=0` → 422 (pre-existing #195-scoped behaviour, not a #194 regression) |
| 10 | ✅ | T2 — the live spec types every numeric param as integer with correct defaults | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` + `curl -s http://localhost:8080/openapi/v1.json` — confirmed `page`/`pageSize`/`n`/`limit` all publish `"type": ["null","integer"]` with `default` 1/20/1/20; `yearFrom` still `["null","integer"]` with no `default`; `?page=abc`, `?n=abc`, `?limit=abc` all still 422 with the correct detail message; a valid request still returns 200 |

---

## Notes

T1 and T2 are both required: this changes what the published OpenAPI document and the Scalar UI show,
which no unit test observes end to end — the transformer tests drive it in isolation, not through the
real document pipeline.

**Follow-up suggested, not filed as part of this issue:** a document-level bidirectional test — every
registered (path, param) pair exists in the real OpenAPI document, and every `string?`-bound numeric
param on a registered path is itself registered. The isolation tests here can't catch a path string
that silently drifts out of sync with the real route; this is the only thing that would make #194's
class of bug genuinely unrepeatable.
