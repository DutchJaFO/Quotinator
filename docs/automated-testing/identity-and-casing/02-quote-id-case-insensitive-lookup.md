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
- **The id comparison is `-ceq`.** `-eq` is case-insensitive in PowerShell, so with it this test would
  report the canonical form as correct whichever casing came back — the one thing it exists to tell
  apart.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-id-02 --port 18202
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Import a fixture whose explicit id is uppercase

```powershell
$fixture = "$PWD\.claude\temp\smoke-210.json"
$json = @'
{"quotes": [{"id":"F0000210-0000-4000-8000-000000000210","quote":"A #210 smoke test quote with an uppercase explicit id.","originalLanguage":"en","source":"Smoke Test Film 210","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]}
'@
[IO.File]::WriteAllText($fixture, $json, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18202/api/v1/import" `
  --file $fixture --duplicate-resolution newest-wins --expect 200
```

**Expected:** `200`.

**On failure:** stop. Neither lookup below can distinguish "the read is case-sensitive" from "the quote
was never stored".

### 3. Fetch the quote by the lowercase form of its id

```powershell
$lower = dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18202/api/v1/quotes/f0000210-0000-4000-8000-000000000210" `
  --expect 200 | ConvertFrom-Json
$lower.id -ceq 'f0000210-0000-4000-8000-000000000210'
```

**Expected:** `200`, and `True` — the response's own `id` field is the canonical **lowercase** form
regardless of the uppercase casing the file supplied.

### 4. Fetch the same quote by the file's own uppercase casing

```powershell
$upper = dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18202/api/v1/quotes/F0000210-0000-4000-8000-000000000210" `
  --expect 200 | ConvertFrom-Json
$upper.id -ceq 'f0000210-0000-4000-8000-000000000210'
$upper.quote -ceq $lower.quote
```

**Expected:** `200`, and `True` twice — the same quote comes back, and its `id` is again rendered in
the canonical lowercase form.

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

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-id-02
Remove-Item $fixture
```
