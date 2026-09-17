# The staged review → decide → apply workflow, end to end

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #45, #149, #152, #154

## Preconditions

The Fresh profile, plus the shared conflict fixture this test writes itself:
`scripts/testing/stage-import-conflict.csx` re-states a bundled quote's id with different text.

`/api/v1/import/actions/*` (#154's unified staging engine) is the live mechanism: every import and seed
run stages through it.

## Determinism

- **The batch comes from the conflict fixture, not from the curated file.** Since the planner began
  recording already-stored content as an `Unchanged` no-op (#373), re-importing
  `data/sources/quotinator-curated.json` against a seeded container stages nothing awaiting a decision
  and answers `200` — measured 2026-09-17, `pending=0`, which leaves this document nothing to decide
  against. [`04-discard.md`](04-discard.md) and [`20-pending-review-alert.md`](20-pending-review-alert.md)
  use the same fixture for the same reason.
- **`review` policy is forced explicitly.** The conflicting text would otherwise be resolved on the spot,
  leaving nothing pending.
- **`status=pending` is deliberately lowercase**, and the `batchId` on the apply call is deliberately
  lowercased too. Both prove the case-insensitive query-filter fix (#154) is still in effect — matching
  the stored casing would pass without testing anything.
- **Step 8 loops over every pending id rather than naming a number.** The fixture stages one pending
  `Quote`, and the batch also carries the already-stored source as a no-op, so the final tally covers
  more actions than were ever pending.
- **`GET /import/actions`'s `items` may be empty or populated; that is not the assertion, the status
  code is.**
- **The pending listing is scoped to this batch's own `batchId`.** Scoping matters, because an unscoped
  listing is satisfied by anything an earlier run left pending.
- **The final status tally is the assertion, not the quote's own field.** The decision used here is
  `{"quoteText":{"choice":"keep"}}`, so a correct apply leaves the quote exactly as it was and the
  domain row cannot distinguish success from an apply that wrote nothing at all.
  `ImportActionStatus.Applied` means the write landed.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-01 --port 18601
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18601/api/v1"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Confirm the staging endpoint is reachable

```powershell
dotnet script scripts/testing/http.csx -- --url "$base/import/actions" --expect 200 --status
```

**Expected:** `200` — the staging endpoint is reachable with no setup.

### 3. Confirm the legacy conflicts machinery is gone

```powershell
dotnet script scripts/testing/http.csx -- --url "$base/import/conflicts" --expect 404 --status
```

**Expected:** `404`. It was removed entirely in #154 Phase B; anything else means the legacy
manual-review machinery has regressed back in.

### 4. Import the conflict fixture under forced `review`

```powershell
$fixture = Join-Path $env:TEMP "qt-import-01-fixture"
dotnet script scripts/testing/stage-import-conflict.csx -- --imports $fixture
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file (Join-Path $fixture "conflicting.json") --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$batchId
```

**Expected:** `202`, **not** `200` — the fixture's quote conflicts with the stored one and is left
`Pending` under `review` — and a non-empty `batchId`, which every step below is scoped to.

**On failure:** a `200` here means nothing was staged, so the rest of this document would be testing an
empty batch. An empty `batchId` means the same thing one step earlier. Stop.

### 5. List this batch's pending actions

Scoped to this batch, and the first action id captured for the steps that follow:

```powershell
$pending = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").items
"pending=$(@($pending).Count)"
$actionId = $pending[0].id
$actionId
```

**Expected:** a non-zero count — exactly the actions the import just created — and a non-empty
`actionId`.

### 6. Decide that action, and confirm it moved

```powershell
'{"quoteText":{"choice":"keep"}}' |
  dotnet script scripts/testing/http.csx -- --method POST `
    --url "$base/import/actions/$actionId/decide" --json-stdin --expect 204 --status

$decided = (Invoke-RestMethod "$base/import/actions?status=Decided&batchId=$batchId&pageSize=0").items
"movedToDecided=$(@($decided | Where-Object { $_.id -eq $actionId }).Count)"
```

The body arrives on stdin rather than as an argument, so the helper can assert the `204` while the JSON
survives intact — see the index's *Every command is PowerShell*. Where a step does not need to assert
the status code, `Invoke-RestMethod -Body` is the shorter form, as step 8 uses.

**Expected:** the decide returns `204`, and `movedToDecided=1` — the `Decided` listing contains that
action.

### 7. Undo the decision

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/$actionId/undo" --expect 204 --status

$backToPending = (Invoke-RestMethod "$base/import/actions?status=Pending&batchId=$batchId&pageSize=0").items
"backToPending=$(@($backToPending | Where-Object { $_.id -eq $actionId }).Count)"
```

**Expected:** `backToPending=1` — the action is back under `status=Pending`.

### 8. Decide every action in the batch

`apply` is all-or-nothing, so the rest have to be decided too — including any the import staged for
other entity types:

```powershell
foreach ($id in (Invoke-RestMethod "$base/import/actions?status=Pending&batchId=$batchId&pageSize=0").items.id) {
  Invoke-RestMethod -Method Post -Uri "$base/import/actions/$id/decide" `
    -Headers $key -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"}}' | Out-Null
}
(Invoke-RestMethod "$base/import/actions?status=Pending&batchId=$batchId&pageSize=0").totalCount
```

**Expected:** `0` — nothing left pending, so `apply` can succeed.

### 9. Apply the batch, with the `batchId` lowercased

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/apply?batchId=$($batchId.ToLowerInvariant())" --expect 200 --status
```

**Expected:** `200`.

**On failure:** **if any action is still pending, `apply` returns `422`** with a `pendingActionIds`
array listing those still undecided. That is the batch-apply-atomicity contract working as designed, not
a bug — step 8's count is what confirms it should not happen here.

### 10. Read the batch's final status tally

```powershell
(Invoke-RestMethod "$base/import/actions?batchId=$batchId&pageSize=0").items |
  Group-Object status | Select-Object Count, Name
```

**Expected:** after a successful apply, one group — `Applied` — holding every action in the batch.

## Cleanup

```powershell
Remove-Item $fixture -Recurse -Force -ErrorAction SilentlyContinue
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-01
```
