# A quote resolves by id in either casing, and always renders its id canonically

**Smoke:** no
**Environment:** Fresh
**Traces to:** #210

## Preconditions

Nothing beyond the Fresh profile — the quote under test is supplied by this test's own fixture, not by
the seed.

`Quotes.Id` canonicalizes to lowercase, the same convention every other entity uses
(`EntityIdentity.StableId`, `GuidExtensions.ToCanonicalId`) — this project's single settled id format
after two prior revisions, recorded in ADR 012's revision history.

Before #210's first pass, `GET /quotes/{id}` had **no case-insensitive read-side mitigation at all** —
the one fully-unmitigated gap of this kind found across the whole codebase.

## Determinism

- **The fixture's id is deliberately uppercase** (`F0000210-…`). That is the whole point: capture-time
  canonicalization is only observable if the file supplies a non-canonical form.
- **Both casings are then requested.** Testing only one proves half the behaviour — the lowercase call
  proves canonicalization, the uppercase call proves the case-insensitive read.

## Steps

Run the **Fresh** profile first.

### 1. Import a fixture whose explicit id is uppercase

```bash
cat > .claude/temp/smoke-210.json <<'EOF'
{"quotes": [{"id":"F0000210-0000-4000-8000-000000000210","quote":"A #210 smoke test quote with an uppercase explicit id.","originalLanguage":"en","source":"Smoke Test Film 210","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-210.json" -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

**Expected:** import returns `200`.

**On failure:** stop. Neither lookup below can distinguish "the read is case-sensitive" from "the quote
was never stored".

### 2. Fetch the quote by the lowercase form of its id

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes/f0000210-0000-4000-8000-000000000210"
```

**Expected:** `200` with the quote, and the response's own `id` field is the canonical **lowercase**
form (`f0000210-…`) regardless of the uppercase casing the file supplied.

### 3. Fetch the same quote by the file's own uppercase casing

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/quotes/F0000210-0000-4000-8000-000000000210"
```

**Expected:** `200` with the same quote, its `id` again rendered in the canonical lowercase form.

Taken with the lowercase call, that proves capture-time canonicalization and the case-insensitive read
together; either alone would leave the other unverified.

## Observed effect

Not yet established. The HTTP responses are asserted above; what the container logs while resolving
either casing has not been captured.

## Why the systemic id-case guard has no test of its own

`Quotinator.Data.Diagnostics.SqlIdCaseGuard` scans every SQL query in the codebase for an unwrapped
id-comparison, at unit-test tier: `SqlConstant_PassesIdCaseGuard` and `AssembledQuery_PassesIdCaseGuard`
in both `Quotinator.Core.Tests` and `Quotinator.Data.Tests`, plus `RepositorySqlFactory_PassesIdCaseGuard`
and `AllJoinStrategies_BuildSql_PassesIdCaseGuard` in `Quotinator.Data.Tests`. It is why
`RepositorySql.cs`'s generic `SelectById`/`SoftDelete`/etc. are `LOWER()`-wrapped — see ADR 012's
system-wide lowercase revision for why `LOWER()` and not `UPPER()`.

That guard needs no Docker verification: it is mechanical, it enumerates every query rather than a
maintained list, and a gap in it is a failing unit test.

**It does not follow that the endpoints are covered, and the note this replaced claimed it did.**
Verified 2026-08-22: the guard proves the SQL is wrapped; nothing proves each endpoint wires that
query correctly — route binding, 404-versus-200, and for write paths the update itself. Eleven
endpoints take an `/{id}` route parameter and exactly two are exercised live in both casings: this one,
and Source in
[`05-generic-repository-select-list-wrapping.md`](05-generic-repository-select-list-wrapping.md).

Three of the remainder do not share Source's generic-repository path and so are not covered by proxy
either — Conversation (`Sql.Conversations.SelectForRead`), Captured import file
(`SqliteFileResourceRepository`, its own `Sql.FileResources.SelectById`), and the notification dismiss
endpoint (`Sql.Notifications.UpdateDismissById`, a *write* by id). Closing that is
[#341](https://github.com/DutchJaFO/Quotinator/issues/341); it is not this test's job.

## Cleanup

`rm .claude/temp/smoke-210.json`

The imported quote and its Source remain in the database — restore the Fresh profile before the next
test.
