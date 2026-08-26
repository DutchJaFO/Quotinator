# The Unicode-aware search flag reaches the running app and changes query behaviour

**Smoke:** no
**Environment:** Fresh
**Traces to:** #222

## Preconditions

This proves the **container-level wiring**, not the matching logic — that is already covered by unit
tests. What is under test is whether `Quotinator__UnicodeAwareSearch` (or the HA add-on's
`unicode_aware_search` option) actually reaches the running app and flips real query behaviour.

No bundled or curated data contains a case-varying accented string, so the test imports a small
throwaway fixture rather than relying on shipped content.

Both halves must reach a successful import before their search assertion means anything — a failed
import produces the same empty result as a correctly-unmatched search.

## Determinism

- **Two fresh containers, not one restarted.** A fresh container has no persisted data, so the fixture
  must be imported again in the second half. Reusing a volume would leave the first half's data and
  make the comparison meaningless.
- **Same query, same fixture, one variable.** Only the env var differs between the two halves. That is
  what makes this a with-and-without comparison rather than two unrelated observations.
- The query is percent-encoded (`CAF%C3%89`) — sending the raw accented character depends on shell
  and terminal encoding.
- **The fixture is written as UTF-8 without a BOM.** `Set-Content` defaults to the system ANSI codepage
  in Windows PowerShell 5.1, which corrupts the accented title into something the search can never
  match — and `Out-File -Encoding utf8` adds a BOM the JSON reader has no reason to tolerate. Both
  failures look exactly like the flag not working. Found the hard way while converting this document:
  a `Get-Content -Raw` / `Set-Content` round-trip over this very file turned every `é` into `Ã©`.
- **Assert the fixture's presence, not a total.** The fixture is the only accented case-varying title
  today, but a bundled source acquiring one would make this two items for an entirely correct reason.

## Steps

### 1. Import the fixture into a container with the flag off (default)

The profile's own environment, under this test's own container name:

```powershell
docker build -f docker/Dockerfile -t quotinator:local .
dotnet script scripts/testing/test-env.csx -- create --name qt-api-03-off --port 18103 `
  --env Quotinator__AutoPurgeBundledImportActions=false

$fixture = "$PWD\.claude\temp\smoke-222.json"
$json = @'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"I will always have Café de Flore.","originalLanguage":"en","source":"Café de Flore","date":"1990","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]
}
'@
[IO.File]::WriteAllText($fixture, $json, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18103/api/v1/import" `
  --file $fixture --duplicate-resolution newest-wins --expect 200
```

**Expected:** `200`, and the report names one new `Quote` and one new `Source`. A `review` import
answers `202` instead, because it has staged something to decide; `newest-wins` resolves as it goes and
leaves nothing pending.

**On failure:** stop. A failed import produces the same empty result the next step reads as a pass,
so its "not matched" outcome would be indistinguishable from the behaviour under test.

### 2. Search the accented title in the wrong case, flag off

```powershell
$off = Invoke-RestMethod "http://localhost:18103/api/v1/quotes/search?q=CAF%C3%89&field=source"
"status=$($off.status) matching=$($off.totalMatching)"
```

**Expected:** `status=NoResults` and `matching=0` — the fixture's accented title is not matched,
proving default behaviour is unchanged.

### 3. Import the same fixture into a second container with the flag on

The same environment plus the one variable under test:

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-03-off
dotnet script scripts/testing/test-env.csx -- create --name qt-api-03-on --port 19103 `
  --env Quotinator__AutoPurgeBundledImportActions=false `
  --env Quotinator__UnicodeAwareSearch=true

dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:19103/api/v1/import" `
  --file $fixture --duplicate-resolution newest-wins --expect 200
```

**Expected:** `200`, as in step 1.

**On failure:** stop, for the same reason as step 1 — an unimported fixture cannot be matched, and the
next step would report the flag as having no effect.

### 4. Repeat the same query with the flag on

```powershell
$on = Invoke-RestMethod "http://localhost:19103/api/v1/quotes/search?q=CAF%C3%89&field=source"
"status=$($on.status)"
@($on.items | Where-Object { $_.source -eq "Caf$([char]0xE9) de Flore" }).Count
```

**Expected:** `status=Ok` and `1` — the fixture's own item is present. Asserted by identity rather than
by a total, so a bundled source gaining an accented title later does not turn a correct result into a
failure. The title is spelled `[char]0xE9` rather than as a literal accented character so the
comparison cannot depend on how the console encoded the command — and note that PowerShell 5.1 has no
`` `u{…} `` escape, which would silently compare against the literal text `Cafu{e9}`.

## Observed effect

Not yet established. The behavioural difference is asserted above; what the container logs while
serving each half has not been captured.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-03-off
dotnet script scripts/testing/test-env.csx -- destroy --name qt-api-03-on
Remove-Item $fixture
```
