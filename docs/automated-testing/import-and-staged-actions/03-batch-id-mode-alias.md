# `POST /import?batchId=` applies an already-staged batch without re-uploading

**Smoke:** no
**Environment:** Fresh
**Traces to:** #154

## Preconditions

Nothing beyond the Fresh profile — the batch is staged by the preview call in Steps.

## Determinism

- **`skip` policy is used deliberately**, so the staged batch leaves nothing pending and `apply`
  can return `200` rather than the `422` an undecided action would produce. Using `review` here would
  test the atomicity contract instead of the alias.
- The `batchId` must come from this test's own preview response. A batch id found lying around in the
  database belongs to something else and makes the result meaningless.
- **The action statuses are read before and after, and that is the whole assertion.** `skip` means no
  domain row changes, so the status code alone cannot distinguish a working alias from a handler that
  parses `batchId` and returns `200` without applying anything — which is precisely the regression this
  test names. `ImportActionStatus.Applied` means "this action's write landed on the consumer's own
  tables", so the transition to it is the observable that a dead code path cannot fake.
- **That transition is the assertion, not the status code.** `batchId` mode is a genuine alias for
  `POST /import/actions/apply` only if the actions it names actually moved; a route that returned `200`
  and did nothing leaves them exactly as the first reading found them.
- **`pageSize=0` is required on both readings.** The default page is 20 and the curated file stages more
  than that, so a default-paged listing would compare two truncated samples and could agree while the
  batch was only partly applied.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-03 --port 18603
$base = "http://localhost:18603/api/v1"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch by previewing the curated file under `skip`

```powershell
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/preview" `
              --file data/sources/quotinator-curated.json --duplicate-resolution skip `
            | ConvertFrom-Json).batchId
$batchId
```

**Expected:** the response carries a `batchId` — the readings below are scoped to it.

### 3. Record the staged state before applying

```powershell
$before = (Invoke-RestMethod "$base/import/actions?batchId=$batchId&pageSize=0").items
$before | Group-Object status | Select-Object Count, Name
"total=$(@($before).Count) applied=$(@($before | Where-Object { $_.status -eq 'Applied' }).Count)"
```

**Expected:** the batch's actions are staged and `applied=0`.

### 4. Apply by `batchId`

```powershell
dotnet script scripts/testing/http.csx -- --method POST --url "$base/import?batchId=$batchId" --expect 200 --status
```

**Expected:** `200`.

### 5. Read the same listing again

```powershell
$after = (Invoke-RestMethod "$base/import/actions?batchId=$batchId&pageSize=0").items
$after | Group-Object status | Select-Object Count, Name
"total=$(@($after).Count) sameTotal=$(@($after).Count -eq @($before).Count)"
"notApplied=$(@($after | Where-Object { $_.status -ne 'Applied' }).Count)"
```

**Expected:** `notApplied=0` — every one of them reads `Applied` — and `sameTotal=True`: the number of
actions is unchanged between the two readings, so the alias applied the batch rather than staging a new
one.

## Observed effect

Not yet established as a captured record.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-03
```
