# A Person can be Modified, blocks overwrites when Complete, and reverses by a mixed-case id

**Smoke:** no
**Environment:** Fresh
**Traces to:** #173

## Preconditions

Person was Add-only before this issue and never had a write path for `dateOfBirth`/`dateOfDeath`. This
proves a `Complete` Person blocks a silent overwrite, that a correctable Person can be Modified and
decided end to end, and exercises the lowercase-explicit-id reversal fix found live during #173's own
T2 pass.

**A fixture needs at least one quote** — `POST /import` rejects a file with none.

**Two separate Person fixtures are required.** See Determinism; using one is the mistake this test
already made once.

## Determinism

**`CompletenessGuard.ShouldBlock` (`ImportActionPlanner.cs`, #168) is evaluated against the value a
policy would actually *write*, not the raw incoming value.** Once a row is `Complete`, **every policy
except `skip` blocks a genuine field change, `newest-wins` included.**

Re-running the reversal sequence against the `Complete` Person from the first half stages `Blocked`
again, never a clean apply. This was corrected in this test on 2026-07-31.

**Reversing a `Modify`-only batch never touches `IsDeleted` at all** — only reversing the row's own
`Add` does. So the lowercase-id reversal fix can only be proven against a fresh row that was never
marked `Complete`.

**The second fixture's id is deliberately uppercase (`F0000007-…`), and that is the reproduction
shape.** A `Guid`-typed repository call used to silently force-uppercase before comparing, matching
zero rows against the lowercase-canonicalized stored id — so the row would stay visibly present with
`IsDeleted = 0` while the endpoint reported success. Lowercasing it in the fixture removes the entire
point of the test.

The first fixture's id is deliberately lowercase, as a file-authored explicit id always is.

**Each step names its fixture** — four imports run here and they differ only by which file they upload.

**Reading the `smoke-173-v3.json` tally is the assertion**; the import returns a success code either
way.

**The Person action is selected by its `entityType` property**, not by matching the shape of the
serialized response — a field-order change would silently select nothing, and the decide call would
then be made against an empty variable.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-08 --port 18608
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18608/api/v1"
$temp = "$PWD\.claude\temp"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Import `smoke-173.json` — the initial add

```powershell
$v1 = @'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1950-01-01","dateOfDeath":null}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-173.json", $v1, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\smoke-173.json" --duplicate-resolution newest-wins --expect 200 | Out-Null

Invoke-RestMethod "$base/masterdata/people/f0000005-0000-4000-8000-000000000005" |
  Select-Object id, name, dateOfBirth, completenessStatus
```

**Expected:** `200` with the Person added, `dateOfBirth` `1950-01-01`.

**On failure:** without this row there is nothing for the re-imports below to Modify, and every later
step would be staging a fresh add instead. Stop.

### 3. Re-import `smoke-173-v2.json` — the same id with a changed `dateOfBirth`, under `review`

```powershell
$v2 = @'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1951-02-02","dateOfDeath":null}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-173-v2.json", $v2, [Text.UTF8Encoding]::new($false))

$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file "$temp\smoke-173-v2.json" --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$batchId

$staged = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").items
$staged | Select-Object entityType, actionType, ambiguousFields
$personId = ($staged | Where-Object { $_.entityType -eq 'Person' })[0].id
"personId=$personId"
```

**Expected:** a `Pending` `Modify` for the Person with `ambiguousFields` of `dateOfBirth`, and a
non-empty `personId`.

**On failure:** an empty pending listing means the `review` policy did not take effect and nothing was
staged, so the decide and apply below would be operating on an empty batch. Stop.

### 4. Decide the action and apply the batch

The fixture's quote stages an action of its own and `apply` is all-or-nothing, so everything else in
the batch is decided too:

```powershell
Invoke-RestMethod -Method Post -Uri "$base/import/actions/$personId/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"personDateOfBirth":{"choice":"replace"},"markCompletenessAs":"Complete"}' | Out-Null

foreach ($id in (Invoke-RestMethod "$base/import/actions?status=Pending&batchId=$batchId&pageSize=0").items.id) {
  Invoke-RestMethod -Method Post -Uri "$base/import/actions/$id/decide" -Headers $key `
    -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"}}' | Out-Null
}

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$batchId" --expect 200 --status
Invoke-RestMethod "$base/masterdata/people/f0000005-0000-4000-8000-000000000005" |
  Select-Object id, dateOfBirth, completenessStatus
```

**Expected:** the apply returns `200`, and the Person reads back `dateOfBirth` `1951-02-02` with
`completenessStatus` `Complete`.

**The loop is why this applies at all.** `POST /import` rejects a file with no quotes, so every fixture
here carries one and it stages its own `Modify`; leaving it undecided makes `apply` return `422` and
the rest of the document unreachable. Measured during #339's full run, where this batch staged two
actions and the step named one.

### 5. Import `smoke-173-v3.json` — a third `dateOfBirth`, and read what it staged

The `Complete` row must block it:

```powershell
$v3 = @'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1952-03-03","dateOfDeath":null}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-173-v3.json", $v3, [Text.UTF8Encoding]::new($false))

$thirdBatchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
                   --file "$temp\smoke-173-v3.json" --duplicate-resolution review `
                 | ConvertFrom-Json).batchId

$third = (Invoke-RestMethod "$base/import/actions?batchId=$thirdBatchId&pageSize=0").items
$third | Group-Object status | Select-Object Count, Name
"blockedPerson=$(@($third | Where-Object { $_.entityType -eq 'Person' -and $_.status -eq 'Blocked' }).Count)"
(Invoke-RestMethod "$base/masterdata/people/f0000005-0000-4000-8000-000000000005").dateOfBirth
```

**Expected:** `blockedPerson=1` — **`Blocked`, not `Pending`** — and `dateOfBirth` still
`1951-02-02`: `1952-03-03` never lands.

### 6. Import `smoke-173-addonly.json` — a fresh Person with an uppercase id

```powershell
$addOnly = @'
{
  "quotes": [{"id":"f0000005-0000-4000-8000-000000000008","quote":"A #173 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person AddOnly","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"F0000007-0000-4000-8000-000000000007","name":"Smoke Test Person AddOnly","dateOfBirth":"1985-05-05","dateOfDeath":null}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-173-addonly.json", $addOnly, [Text.UTF8Encoding]::new($false))

$addonlyBatchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
                     --file "$temp\smoke-173-addonly.json" --duplicate-resolution newest-wins --expect 200 `
                   | ConvertFrom-Json).batchId
$addonlyBatchId
```

**Expected:** a non-empty `addonlyBatchId` — the reversal below is scoped to it.

### 7. Reverse the add-only batch, preview first

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/reverse?batchId=$addonlyBatchId&preview=true" --expect 200 --status
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/reverse?batchId=$addonlyBatchId" --expect 200 --status
```

**Expected:** both reversal calls return `200`.

### 8. Confirm the soft-delete flag flipped

A soft-deleted row is invisible to every read endpoint, so the API answers this without a database
copy — the lowercase form of the fixture's uppercase id, which is the casing under test:

```powershell
dotnet script scripts/testing/http.csx -- `
  --url "$base/masterdata/people/f0000007-0000-4000-8000-000000000007" --expect 404 --status
```

**Expected:** `404` — the row is genuinely gone from every read path, so `IsDeleted` flipped to `1`.

**A `200` here is the defect this test exists to catch.** The reversal reported success in the failing
case too: a `Guid`-typed lookup force-uppercased before comparing, matched no lowercase-canonicalized
row, and left the Person visibly present. Step 9's re-import is what confirms the row was *truly* gone
rather than merely hidden.

### 9. Re-import `smoke-173-addonly.json` unchanged, and read what it staged

This is the single distinction the test exists to draw, and nothing else in the run observes it:

```powershell
$finalBatchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
                   --file "$temp\smoke-173-addonly.json" --duplicate-resolution newest-wins --expect 200 `
                 | ConvertFrom-Json).batchId

$final = (Invoke-RestMethod "$base/import/actions?batchId=$finalBatchId&pageSize=0").items
$final | Group-Object actionType | Select-Object Count, Name
"personAdd=$(@($final | Where-Object { $_.entityType -eq 'Person' -and $_.actionType -eq 'Add' }).Count)"

dotnet script scripts/testing/http.csx -- `
  --url "$base/masterdata/people/f0000007-0000-4000-8000-000000000007" --expect 200 --status
```

**Expected:** `personAdd=1` — **`Add`, not `Modify`** — and the Person resolves `200` again, so
`IsDeleted` is back to `0`. Step 8's `404` against the same id is what makes that `200` mean something.

**On failure:** `Modify` would mean the reversal silently no-op'd and the row was never truly gone — the
endpoint reported success in that failing case too, so this tally is the only thing that separates them.

## Observed effect

Not yet established as a captured record. The `IsDeleted` flip is the load-bearing observation: the
endpoint reported success in the failing case too, so the HTTP result alone never distinguished them.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-08
Remove-Item "$temp\smoke-173.json", "$temp\smoke-173-v2.json", "$temp\smoke-173-v3.json", `
            "$temp\smoke-173-addonly.json" -ErrorAction SilentlyContinue
```
