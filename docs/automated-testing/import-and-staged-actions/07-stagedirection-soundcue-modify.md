# StageDirection and SoundCue can be Modified, and a Complete row blocks overwrites

**Smoke:** no
**Environment:** Fresh
**Traces to:** #171, #172

## Preconditions

Both entities were Add-only before these issues. This proves a `Complete` row blocks a silent
overwrite, and that a correctable row can be Modified, decided and reversed end to end.

**A fixture needs at least one quote** — `POST /import` rejects a file with none, even when the quote
is irrelevant to what is being tested.

**Two separate fixtures are required**, and this is the part that is easy to get wrong — see
Determinism.

## Determinism

**`CompletenessGuard.ShouldBlock` is evaluated against the value a policy would actually *write*, not
the raw incoming value.** So once a row is `Complete`, **every policy except `skip` blocks a genuine
field change — `newest-wins` included.**

That makes the reversal half impossible to run against the rows used in the first half: re-running it
there stages `Blocked` again rather than applying cleanly. It needs a **second, brand-new pair** that
was never marked `Complete`. This was a real correction to this test (2026-08-08), not a hypothetical.

Also load-bearing: **a `Modify`-only batch's reversal never touches `IsDeleted`.** Only reversing the
row's own `Add` does, which is why the second fixture must be a fresh add.

The direct-apply path (`newest-wins`, nothing pending) sets `Import_Batch.Status` to `Applied`; the
two-phase decide→apply path used in the first half does **not** — a known pre-existing gap, see
#171/#172's plan docs.

**Each step names the fixture it uses**, because five imports run here and they differ only by which
file they upload.

**Reading the `smoke-171-172-v3.json` tally is the assertion**; the import returns a success code either
way, so the status code alone does not distinguish blocked from staged.

**The staged actions are selected by their `entityType` property**, not by matching the shape of the
serialized response. A response whose field order changed would silently select nothing under the
latter, and the decide calls would then be made against an empty variable.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-07 --port 18607
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18607/api/v1"
$temp = "$PWD\.claude\temp"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Import `smoke-171-172.json` — the initial add

```powershell
$v1 = @'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-171-172.json", $v1, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\smoke-171-172.json" --duplicate-resolution newest-wins --expect 200 | Out-Null

(Invoke-RestMethod "$base/masterdata/stagedirections?pageSize=0").items |
  Where-Object { $_.id -eq 'f0000002-0000-4000-8000-000000000002' } |
  Select-Object id, text, completenessStatus
```

**Expected:** `200`, and the StageDirection row is present reading `A shot rings out.`

**On failure:** without these rows there is nothing for the re-imports below to Modify, and every later
step would be staging fresh adds instead. Stop.

### 3. Re-import `smoke-171-172-v2.json` — the same ids with a changed `text`, under `review`

```powershell
$v2 = @'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out, twice.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder, rolling.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-171-172-v2.json", $v2, [Text.UTF8Encoding]::new($false))

$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file "$temp\smoke-171-172-v2.json" --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$batchId

$staged = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").items
$staged | Select-Object entityType, actionType, ambiguousFields

$stageId = ($staged | Where-Object { $_.entityType -eq 'StageDirection' })[0].id
$soundId = ($staged | Where-Object { $_.entityType -eq 'SoundCue' })[0].id
"stageId=$stageId soundId=$soundId"
```

**Expected:** a `Pending` `Modify` action for each, with `ambiguousFields` of `text`, and both ids
non-empty.

**On failure:** an empty pending listing means the `review` policy did not take effect and nothing was
staged, so the decide and apply below would be operating on an empty batch. Stop.

### 4. Decide every action in the batch, then apply it

The fixture's quote stages an action of its own, and `apply` is all-or-nothing — so the two under test
are decided with their real choices, and anything else in the batch is decided too:

```powershell
Invoke-RestMethod -Method Post -Uri "$base/import/actions/$stageId/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"stageDirectionText":{"choice":"replace"},"markCompletenessAs":"Complete"}' | Out-Null
Invoke-RestMethod -Method Post -Uri "$base/import/actions/$soundId/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"soundCueText":{"choice":"replace"},"markCompletenessAs":"Complete"}' | Out-Null

foreach ($id in (Invoke-RestMethod "$base/import/actions?status=Pending&batchId=$batchId&pageSize=0").items.id) {
  Invoke-RestMethod -Method Post -Uri "$base/import/actions/$id/decide" -Headers $key `
    -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"}}' | Out-Null
}

(Invoke-RestMethod "$base/import/actions?status=Pending&batchId=$batchId&pageSize=0").totalCount
dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$batchId" --expect 200 --status
```

**Expected:** `0` before the apply, then `200`. Both rows then carry the corrected text
and `CompletenessStatus: Complete` — read back by step 8.

**The loop is not redundant with the two explicit decides.** A fixture needs at least one quote for
`POST /import` to accept it, and that quote stages its own `Modify` action; leaving it undecided makes
`apply` return `422` and the rest of the document unreachable. Measured during #339's full run, where
the batch staged three actions and this step named two.

### 5. Re-import `smoke-171-172-v3.json` — a third `text`, still under `review`

The `Complete` rows must block it:

```powershell
$v3 = @'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out, three times.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder, fading.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-171-172-v3.json", $v3, [Text.UTF8Encoding]::new($false))

$thirdBatchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
                   --file "$temp\smoke-171-172-v3.json" --duplicate-resolution review `
                 | ConvertFrom-Json).batchId
$thirdBatchId

$third = (Invoke-RestMethod "$base/import/actions?batchId=$thirdBatchId&pageSize=0").items
$third | Group-Object status | Select-Object Count, Name
"blockedStageAndSound=$(@($third | Where-Object { $_.entityType -in 'StageDirection','SoundCue' -and $_.status -eq 'Blocked' }).Count)"
```

**Expected:** the tally reads **`Blocked`, not `Pending`**, for both entities — `blockedStageAndSound=2`.
A `Complete` row can no longer be silently overwritten.

### 6. Import `smoke-171-172-addonly.json` — a fresh pair, for the reversal half

```powershell
$addOnly = @'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000009","quote":"A #171/#172 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000009","text":"Original text before correction.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000009","text":"Original sound before correction.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-171-172-addonly.json", $addOnly, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\smoke-171-172-addonly.json" --duplicate-resolution newest-wins --expect 200 | Out-Null

(Invoke-RestMethod "$base/masterdata/stagedirections?pageSize=0").items |
  Where-Object { $_.id -eq 'f0000002-0000-4000-8000-000000000009' } |
  Select-Object id, text, completenessStatus
```

**Expected:** a fresh pair added, still `NeedsReview` — never `Complete`, which is what makes the
reversal half runnable at all.

### 7. Single-shot re-import `smoke-171-172-addonly-v2.json` under `newest-wins`, then reverse it

```powershell
$addOnlyV2 = @'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000009","quote":"A #171/#172 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000009","text":"Corrected text after correction.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000009","text":"Corrected sound after correction.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
'@
[IO.File]::WriteAllText("$temp\smoke-171-172-addonly-v2.json", $addOnlyV2, [Text.UTF8Encoding]::new($false))

$correctionBatchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
                        --file "$temp\smoke-171-172-addonly-v2.json" --duplicate-resolution newest-wins --expect 200 `
                      | ConvertFrom-Json).batchId
$correctionBatchId

dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/reverse?batchId=$correctionBatchId&preview=true" --expect 200 --status
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/reverse?batchId=$correctionBatchId" --expect 200 --status
```

**Expected:** the re-import applies immediately with nothing pending, and both reversal calls against
its batch return `200`.

### 8. Confirm the pre-correction text is back

```powershell
docker stop -t 15 qt-import-07
docker cp qt-import-07:/data/quotinatordata.db .claude/temp/smoke-171-172.db
docker cp qt-import-07:/data/quotinatordata.db-wal .claude/temp/smoke-171-172.db-wal 2>$null
docker cp qt-import-07:/data/quotinatordata.db-shm .claude/temp/smoke-171-172.db-shm 2>$null
docker start qt-import-07
dotnet script scripts/testing/http.csx -- --url "$base/health" --wait-for 200 --status

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke-171-172.db `
  --sql "SELECT Id, Text, CompletenessStatus FROM Quotinator_StageDirection WHERE Id IN ('f0000002-0000-4000-8000-000000000002','f0000002-0000-4000-8000-000000000009')"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke-171-172.db `
  --sql "SELECT Id, Text, CompletenessStatus FROM Quotinator_SoundCue WHERE Id IN ('f0000003-0000-4000-8000-000000000003','f0000003-0000-4000-8000-000000000009')"
```

**Expected:** the closing reads show the **`…000002`/`…000003` pair** still carrying `v2`'s
corrected text with `CompletenessStatus: Complete` — `v3`'s text never landed — and the **`…000009`
pair** back at `Original text before correction.` / `Original sound before correction.`, the reversal
undone.

## Observed effect

Not yet established as a captured record beyond the database reads asserted above.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-07
Remove-Item "$temp\smoke-171-172.json", "$temp\smoke-171-172-v2.json", "$temp\smoke-171-172-v3.json", `
            "$temp\smoke-171-172-addonly.json", "$temp\smoke-171-172-addonly-v2.json", `
            "$temp\smoke-171-172.db", "$temp\smoke-171-172.db-wal", "$temp\smoke-171-172.db-shm" `
            -ErrorAction SilentlyContinue
```
