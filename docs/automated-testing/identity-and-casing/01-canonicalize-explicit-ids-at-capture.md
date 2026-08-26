# A file-authored explicit id is canonicalized at capture, and its joins survive

**Smoke:** no
**Environment:** Fresh
**Traces to:** #209

## Preconditions

Nothing beyond the Fresh profile — but the profile's own first boot is **under test here**, not merely
setup for the import below, which is why the log check is the first step rather than an afterthought.

The bundled curated file's own Conversations reference StageDirections and SoundCues by id. #209's fix
would have broken those references if left incomplete, so a seed completing with no
`SQLite Error 19: FOREIGN KEY constraint failed` is itself an assertion here.

## Determinism

- **The fixture's ids are UPPERCASE in the file and carry hex letters**, and the masterdata lookup
  below uses the lowercase form in the URL. Both halves are load-bearing: the uppercase file value is
  what capture-time canonicalization has to transform, and the lowercase lookup is the exact scenario
  that originally returned 404.
- **A fixture already in canonical form cannot fail.** Until #339's full run these ids were lowercase
  and near-digit-only (`f6000002-0000-4000-8000-000000000002`, one hex letter in 32), so "canonicalized
  at capture" was indistinguishable from "stored verbatim" — the test passed identically with
  canonicalization removed. Hex letters in mixed case are what give the assertion something to
  transform; see the index's *A count is evidence only if the instrument counts the right thing* for
  the same failure in its counting form.
- **The seed must have run before the import.** A container whose seeding failed would produce a
  passing import and a meaningless FK result.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-id-01 --port 18201
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app
that never became healthy.

### 2. Read the seed's own log before importing anything

```powershell
$log = docker logs qt-id-01 2>&1 | Out-String
([regex]::Matches($log, 'SQLite Error 19')).Count
```

**Expected:** `0` — the container's seed produced no `SQLite Error 19`.

**On failure:** any other number is a failure, and it is the first step for a reason: a non-zero count
means the seed is what broke, so the assertions below would be reporting on a database that was never
built correctly. Stop.

### 3. Import a fixture whose quote and Source both carry file-authored explicit ids

```powershell
$fixture = "$PWD\.claude\temp\smoke-209.json"
$json = @'
{
  "quotes": [{"id":"F6ABCDE1-0000-4000-8000-00000000ABC1","quote":"A #209 smoke test line.","originalLanguage":"en","source":"209 Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "sources": [{"id":"F6ABCDE2-0000-4000-8000-00000000ABC2","title":"209 Smoke Test Film","type":"movie"}]
}
'@
[IO.File]::WriteAllText($fixture, $json, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18201/api/v1/import" `
  --file $fixture --duplicate-resolution newest-wins --expect 200
```

**Expected:** `200`. A `newest-wins` import resolves as it goes, so nothing is staged and the answer is
not the `202` a `review` import gives.

### 4. Look the Source up by the id the file authored

```powershell
$source = dotnet script scripts/testing/http.csx -- `
  --url "http://localhost:18201/api/v1/masterdata/sources/f6abcde2-0000-4000-8000-00000000abc2" `
  --expect 200 | ConvertFrom-Json
$source.id
```

**Expected:** `200`, and the id echoed back **lowercase** — `f6abcde2-0000-4000-8000-00000000abc2`,
canonicalized per ADR 012's system-wide convention, rather than the uppercase form the file carried.

### 5. Fetch the quote and confirm its Source join still resolves

```powershell
(Invoke-RestMethod "http://localhost:18201/api/v1/quotes/f6abcde1-0000-4000-8000-00000000abc1").source
```

**Expected:** `209 Smoke Test Film`, resolved through the Quote→Source join — proving the fix did not
break the join in order to make the masterdata lookup work.

## Observed effect

Partially established: a clean seed emitting no `SQLite Error 19` is an observed effect and is asserted
above. What the container logs during the import itself has not been captured.

**The original failure this replaced:** a file-authored explicit id reached storage in whatever raw
casing the file used, never canonicalized. A `Guid`-typed lookup — which force-uppercases — then
silently failed to find a non-canonically-stored row, even though the same row resolved fine via a
join.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-id-01
Remove-Item $fixture
```
