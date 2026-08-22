# #222 — Unicode-aware case-insensitive LIKE matching (accented/non-ASCII characters)

**Status:** Waiting for release
**GitHub issue:** #222
**Tiers required:** T1, T2
**Depends on:** Nothing (independent of #227 — the SQL sites touched all reference tables/columns
Sql.cs already parameterises; the fix is additive alongside them, not a rename)

---

## Background

Confirmed against SQLite's own docs ([lang_expr.html](https://sqlite.org/lang_expr.html)) before
writing this plan:

- `LIKE` case-folds ASCII only by default (`'æ' LIKE 'Æ'` → `FALSE`).
- **`LIKE` does not consult `COLLATE` at all.** Only two things change its behaviour: the ICU
  extension, or the `case_sensitive_like` pragma (which only toggles ASCII case-sensitivity, adds no
  Unicode support). This means the issue's Option 2 — "`CreateCollation(...)` or `CreateFunction(...)`"
  — overstates `CreateCollation`; it genuinely cannot fix `LIKE`. Only `CreateFunction`, replacing
  `LIKE` with a custom scalar function, works.
- No prebuilt Ubuntu Noble package for the SQLite ICU extension exists (checked
  `packages.ubuntu.com`) — Option 1 (ICU) still requires building the extension from source for both
  `linux/amd64` and `linux/arm64`, an ongoing multi-arch cost the issue already flagged correctly.

Confirmed against Microsoft Learn (`Microsoft.Data.Sqlite` docs):
- `SqliteConnection.CreateFunction` supports multi-string-argument functions returning `bool`, usable
  directly in a `WHERE` clause, available since 2.1.0 (Quotinator: 10.0.10) — fully viable.
- **Functions are registered per connection, not globally.** `SqliteConnectionFactory.CreateConnection()`
  ([SqliteConnectionFactory.cs](../../../src/Quotinator.Data/Connections/SqliteConnectionFactory.cs))
  returns a fresh, unopened connection on every call — every one needs the function registered after
  `Open()`, or callers hit "no such function." Hooking `SqliteConnection.StateChange` in the factory
  registers it on every open, regardless of call site, with no changes needed anywhere else.

**Actual code scope** (grepped, not assumed): 8 `LIKE` clauses total —
`Sql.SearchField.{Quote,Source,Character,Author,All}` in
[Quotinator.Core/Queries/Sql.cs:161-165](../../../src/Quotinator.Core/Queries/Sql.cs) (used by
`/quotes/search`), plus 3 more written **inline** in `SqliteQuoteService.BuildFilterWhere`
(`SqliteQuoteService.cs:385-387`) for the `character`/`author`/`source` fuzzy filters. All 8 use an
identical `%value%` contains-wrap with no other wildcard usage. The 3 inline ones are a pre-existing
violation of CLAUDE.md's "no inline strings" SQL policy ("every SQL string must live in `Sql.cs`,
including inline in a service") — fixed as a byproduct of this change, not separately.

## Decision

**Add an opt-in, off-by-default feature flag — `Quotinator:UnicodeAwareSearch` (default `false`) —
rather than switching every `LIKE` site unconditionally.** Per direction: this behaviour has not been
exercised against real-world non-ASCII search traffic yet, so it ships disabled until that evidence
exists, matching the existing `Quotinator:AutoUpdateSources` pattern (`builder.Configuration.GetValue`
in `Program.cs`, threaded through a constructor parameter, not a config-driven runtime toggle).

**Mechanism when enabled:** replace `column LIKE @param` with `UNICODE_CONTAINS(column, @param)`, a
custom SQL function registered via `CreateFunction`, implemented as
`haystack.Contains(needle, StringComparison.InvariantCultureIgnoreCase)` in C# (matching this
codebase's existing invariant-culture convention, e.g. `QuoteIdentity.Normalise`'s `ToLowerInvariant()`
— not `OrdinalIgnoreCase`, which folds case but isn't linguistically aware, and not `CurrentCulture`,
which would make behaviour depend on the container's OS locale). The raw (unwrapped) search term is
passed as the parameter — `UNICODE_CONTAINS` does its own containment check, so the `$"%{value}%"`
wrapping used for `LIKE` is dropped for this path.

**Function registration is unconditional, the flag only controls which SQL text is built.**
`SqliteConnectionFactory` (`Quotinator.Data`, domain-agnostic) always registers `UNICODE_CONTAINS` on
every connection open — cheap, harmless, and keeps the factory free of any config dependency.
`SqliteQuoteService` (which does carry the flag) decides whether the SQL it builds calls
`UNICODE_CONTAINS(...)` or `LIKE`.

**Exposed as a full HA add-on option, same as every other config toggle.** Per direction: follow the
project's standard checklist for this (CLAUDE.md's "When adding or renaming an HA add-on config
option") — `unicode_aware_search: false` added to both `addon/config.yaml` and
`addon-beta/config.yaml`'s `options`/`schema`/`env_vars`, with matching entries in each folder's
`translations/{en,nl,de}.yaml`, same option name and description text in both (the option itself
doesn't differ between channels, matching every other option). Defaulting to `false` in the option
value itself is what keeps it off by default — nothing about being a real add-on option contradicts
"opt-in until validated"; `auto_update_sources` and every other toggle already work exactly this way.

---

## 1. Register `UNICODE_CONTAINS` unconditionally in `SqliteConnectionFactory`

**Status:** ✅ Done

Subscribe to `SqliteConnection.StateChange` in `CreateConnection()`; on transition to `Open`, call
`CreateFunction<string?, string?, bool>("UNICODE_CONTAINS", (h, n) => h is not null && n is not null &&
h.Contains(n, StringComparison.InvariantCultureIgnoreCase), isDeterministic: true)`.

## 2. Add the `Quotinator:UnicodeAwareSearch` flag

**Status:** ✅ Done

`Program.cs`: `builder.Configuration.GetValue("Quotinator:UnicodeAwareSearch", false)`, threaded into
`SqliteQuoteService`'s constructor (service-provider factory overload, matching the existing
`connectionFactory` pattern at `Program.cs:407`) as a new required `bool` parameter.

## 3. Convert `Sql.SearchField` to flag-aware factory methods; centralise the 3 inline clauses

**Status:** ✅ Done. `Clause` implemented as a `static readonly Func<...>` field rather than a
private method — a private method with a non-optional parameter would have been picked up by
`SqlQueryGuardTests`' reflection-based drift detector (`EnumerateParameterizedSqlFactoryMethodNames`)
as if it were its own directly-called query fragment needing an `AssembledQueryCases` entry, when it's
really just a template the real factory methods share.

`Sql.SearchField.Quote`/`Source`/`Character`/`Author`/`All` become `static` methods taking
`bool unicodeAware`, returning either the existing `LIKE` clause or the `UNICODE_CONTAINS(...)` form —
per CLAUDE.md's "dynamic queries as static factory methods" rule, since the clause now depends on a
runtime value. Add three new factory methods (`CharacterFilter`/`AuthorFilter`/`SourceFilter`, same
`bool unicodeAware` shape) for the clauses currently inlined in `SqliteQuoteService.BuildFilterWhere`,
moving them into `Sql.cs` in the same change. `SqliteQuoteService.Search` and `BuildFilterWhere` (plus
its overload and all 3 call sites) thread the instance's `_unicodeAwareSearch` field through; drop the
`$"%{value}%"` wrapping on the parameter value when the flag is on (the function does its own
containment check).

## 4. Add `unicode_aware_search` as an HA add-on option (both channels)

**Status:** ✅ Done

Following CLAUDE.md's "When adding or renaming an HA add-on config option" checklist exactly:
1. `addon/config.yaml`: `options.unicode_aware_search: false`, `schema.unicode_aware_search: bool`,
   `env_vars` entry `Quotinator__UnicodeAwareSearch` → `"{{ unicode_aware_search }}"` (same shape as
   `auto_update_sources` immediately above it).
2. `addon/translations/en.yaml` (baseline), `nl.yaml`, `de.yaml`: name + description, matching the
   `auto_update_sources` entry's structure and tone.
3. Mirror steps 1–2 into `addon-beta/config.yaml` and `addon-beta/translations/{en,nl,de}.yaml` —
   identical option, schema, and translation text; only the channel differs.

## 5. Update `SqlQueryGuardTests.AssembledQueryCases`

**Status:** ✅ Done. Not a literal 8×2 explosion — the 14-row `filterCases` matrix is invariant to
the flag except for the `character`/`author`/`source` rows (only they ever produce a
`CharacterFilter`/`AuthorFilter`/`SourceFilter` clause), so only those 3 got a dedicated
`unicodeAware: true` variant instead of doubling all 14. The `SearchField` loop (`Quote`/`Source`/
`Character`/`Author`/`All`) doubled cleanly (5 → 10) since every one of those is flag-sensitive.
`ParameterizedSqlFactoryMethods_MatchDocumentedInventory`'s `documented` set also gained the 8 new
`SearchField.*` method names. Full solution: 2,836 tests, all passing.

## 6. Unit tests — red/green, with the feature both off and on

**Status:** ✅ Done. New file `SqliteQuoteServiceUnicodeSearchTests.cs`, 19 test cases across 5 methods
(refactored to `[DataRow]`-parameterized tests, matching this project's existing convention — see
`QuoteTypeNormalisationTests.cs` — instead of 16 near-identical hand-written methods): canary (1),
function registration/correctness (2), `Search` across 5 field variants × 2 flag states (10 rows in
one method), `GetRandom`'s 3 fuzzy filters × 2 flag states (6 rows in one method). Full
`SqliteQuoteService` test surface (60 tests) still green — no regression from the `BuildFilterWhere`
signature change.

New file `tests/Quotinator.Core.Tests/Services/SqliteQuoteServiceUnicodeSearchTests.cs`, real-SQLite
integration style matching `SqliteQuoteServiceSearchTests.cs`'s existing pattern (temp DB, JSON
fixture). Fixture includes a quote whose `source`/`character`/`author`/`quote` text contains a
genuine accented character (e.g. `"Café de Flore"`, matching the issue's own suggested fixture).

| Group | What it proves |
|---|---|
| Canary (no service involved) | A raw `SqliteConnection` with no `UNICODE_CONTAINS` registered: `'café' LIKE '%CAFÉ%'` returns no rows — locks in the underlying SQLite limitation this issue exists to work around, so this test would fail loudly if SQLite's own default ever changed |
| Feature **off** (default) | `SqliteQuoteService` constructed with `unicodeAwareSearch: false` — searching `"CAFÉ"` does **not** find the `"Café de Flore"` fixture, across `Search(field: quote/source/character/author/all)` and the `character`/`author`/`source` fuzzy filters — proves default production behaviour is unchanged |
| Feature **on** | Same service, `unicodeAwareSearch: true` — searching `"CAFÉ"` **does** find the fixture, across the same set of call paths — proves the fix works when enabled |
| Function registration | Direct test: open a connection via the real factory, `SELECT UNICODE_CONTAINS('café', 'CAFÉ')` returns `1` — isolates the function's own correctness from the service layer |
| Per-connection re-registration | Open a **second** connection from the same factory instance and repeat the direct `SELECT UNICODE_CONTAINS(...)` call — proves the `StateChange` hook fires on every connection, not just the first (the exact gotcha Microsoft's own docs warn about) |

Every "off" test and every "on" test targets the *same* fixture row and the *same* query shape,
differing only in the constructor flag — the literal "see the effect with and without the feature
active" comparison.

## 7. Smoke tests (T2) — the toggle itself, against a real running container

**Status:** ✅ Done — written to `docs/smoke-tests.md` section 28 (now `docs/automated-testing/`,
whose README maps the old section numbers), and actually run live against
`quotinator:local` (not just documented): flag-off container returned `NoResults` for `q=CAFÉ&
field=source`; a fresh flag-on container (`-e Quotinator__UnicodeAwareSearch=true`), same import,
same query, returned `Ok` with the `Café de Flore` fixture — matching the unit tests exactly.

New numbered section in `docs/smoke-tests.md` (`## 28. Unicode-aware search toggle (#222)`; the suite
is now `docs/automated-testing/`, whose README maps the old section numbers), following
its established pattern (fenced `curl` commands + expected-output prose). Unlike the unit tests, this
proves the **container-level** wiring — the env var actually reaches the app and flips real query
behaviour — not the matching logic itself (already covered in step 6). No bundled/curated data
currently contains a case-varying accented string (checked), so the section imports a small one-quote
throwaway fixture via the existing `POST /api/v1/import` mechanism (the same pattern
section 2 already uses), rather than adding anything to `data/sources/`:

1. `docker run` **without** the flag (default): import a one-quote fixture with an accented `source`
   title (e.g. `"Café de Flore"`); `GET /quotes/search?q=CAF%C3%89&field=source` → expect empty
   `items` — default behaviour unchanged.
2. `docker run` **with** `-e Quotinator__UnicodeAwareSearch=true`: same import, same search → expect
   the fixture quote returned.

Requires an app restart between the two states (the flag is read once at startup, not polled) — two
separate `docker run` invocations, matching how this doc already handles other scenarios needing a
fresh container.

## 8. Documentation

**Status:** ✅ Done. `README.md` gained a config-flag paragraph next to the `AutoUpdateSources` one,
plus a note on the `/search`/`/random` filter-parameter paragraph. CLAUDE.md's `LIKE`-exemption note
rewritten to describe the actual resolution (`UNICODE_CONTAINS`, opt-in flag) instead of pointing at
#222 as still-open; also records the `COLLATE`-doesn't-affect-`LIKE` finding for future readers.

- `README.md`'s configuration section: add `Quotinator:UnicodeAwareSearch` / `unicode_aware_search`
  next to `Quotinator:AutoUpdateSources`, stating the default (`false`).
- CLAUDE.md's "String centralisation policy" / LIKE-exemption note (referenced directly by the issue):
  record that the `LIKE`-based fuzzy filters gained an opt-in Unicode-aware alternative, and that the
  `/quotes/search`'s `q`/`field` exemption from case-insensitive-by-default (CLAUDE.md's "GUID/enum/
  id/Name/Title comparisons are case-insensitive by default" section) is now partially addressable via
  this flag, still off by default.
- CLAUDE.md's "When adding or renaming an HA add-on config option" checklist itself needs no edit —
  step 4 above already follows it; nothing about the rule changes.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Raw SQLite `LIKE` is confirmed ASCII-only case-insensitive (canary) | Unit test | `SqliteQuoteServiceUnicodeSearchTests.RawSqliteLike_AccentedCharacters_IsCaseSensitive` |
| 2 | ✅ | `UNICODE_CONTAINS` is registered on every connection from `SqliteConnectionFactory`, including a second connection | Unit test | `SqliteQuoteServiceUnicodeSearchTests.UnicodeContains_RegisteredOnEveryConnection` |
| 3 | ✅ | `UNICODE_CONTAINS('café', 'CAFÉ')` returns true | Unit test | `SqliteQuoteServiceUnicodeSearchTests.UnicodeContains_MatchesAccentedCaseVariant` |
| 4 | ✅ | With the flag off (default), `Search`/fuzzy filters do not match accented case variants | Unit test | `SqliteQuoteServiceUnicodeSearchTests.*_FlagOff_DoesNotMatchAccentedCaseVariant` — 8 tests (5 `Search` field variants + 3 `GetRandom` filters) |
| 5 | ✅ | With the flag on, `Search`/fuzzy filters do match accented case variants | Unit test | `SqliteQuoteServiceUnicodeSearchTests.*_FlagOn_MatchesAccentedCaseVariant` — same 8 call paths |
| 6 | ✅ | Every existing ASCII search/filter behaviour is unchanged with the flag off | Unit test | `SqliteQuoteServiceSearchTests` + `SqliteQuoteServiceConversationTests` — 60 `SqliteQuoteService*` tests total, all passing |
| 7 | ✅ | `SqlQueryGuardTests.AssembledQueryCases` covers both flag states for all 8 sites | Unit test | `SqlQueryGuardTests` full run — full solution 2,836 tests passing |
| 8 | ✅ | `unicode_aware_search` option present, schema'd, and translated identically in both `addon/` and `addon-beta/` | Live | Re-read both `config.yaml`s and all 6 translation files after editing — confirmed matching |
| 9 | ✅ | T1: app starts in VS without error | Live | Confirmed by developer — schema v5 (data v4), 799 quotes/461 sources/... startup stats logged cleanly, no errors |
| 10 | ✅ | T2: toggle proven against a real container in both states | Live | Actually run against `quotinator:local`: flag-off → `NoResults`; fresh flag-on container, same import/query → `Ok` with the fixture returned |
| 11 | ✅ | `README.md`/CLAUDE.md documentation updated | Live | Re-read both after editing — confirmed |
