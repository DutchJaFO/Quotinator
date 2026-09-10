# Discarding a staged batch discards what awaits a decision, keeps its no-ops, and applies nothing

**Smoke:** no
**Environment:** Fresh
**Traces to:** #154, #389

## Preconditions

A staged batch holding both an action awaiting a decision and a plan-time no-op — step 2 produces one by
previewing the shared conflict fixture under `review`.

## Determinism

- **The batch comes from the shared conflict fixture, not from the curated file.** This test used to
  preview `data/sources/quotinator-curated.json`, which a fresh container has already seeded. Since the
  planner began recording content that is already stored as an `Unchanged` no-op (#373), that preview
  stages every action `Applied` and nothing awaiting a decision — measured 2026-09-10 as 37 actions, all
  `Unchanged`, and a discard that answered `422`. `scripts/testing/stage-import-conflict.csx` writes a
  file re-stating a bundled quote's id with different text, the fixture
  [`20-pending-review-alert.md`](20-pending-review-alert.md) uses for the same reason; previewed here, it
  stages that quote as a `Pending` `Modify` and its already-stored source as an `Unchanged` no-op.
- **`review` policy is required here**, unlike
  [`03-batch-id-mode-alias.md`](03-batch-id-mode-alias.md): without it the conflicting text is resolved on
  the spot and nothing awaits a decision.
- **The batch must hold both kinds, and step 3 stops if it does not.** The pending action is what gives
  discard something to mark. The no-op is what separates this build from one that stamps every row
  `Discarded`, and from one that refuses the whole batch as already applied (#389). A batch holding only
  one kind cannot tell those apart.
- **Each action is followed by its own `id` from before to after**, and every listing is scoped to that
  specific `batchId` — so other batches in the database cannot affect the result, and one row cannot pass
  for another.
- **The count matters as much as the status**: "every awaiting action shows `Discarded`" is satisfied
  vacuously by an empty list, so a discard that hard-deleted the rows, or a `batchId` filter matching
  nothing, would otherwise read as a pass.
- **The quote count is compared before and after rather than asserted as a value.** Creation is
  deferred to apply time, so a discarded batch never touched the domain tables at all — and that claim
  needs a domain read to mean anything. Comparing the count keeps it true whatever the dataset holds.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-04 --port 18604
$base = "http://localhost:18604/api/v1"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch by previewing the conflict fixture under `review`

```powershell
$fixture = Join-Path $env:TEMP "qt-import-04-fixture"
dotnet script scripts/testing/stage-import-conflict.csx -- --imports $fixture
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/preview" `
              --file (Join-Path $fixture "conflicting.json") --duplicate-resolution review `
            | ConvertFrom-Json).batchId
$batchId
```

**Expected:** the response carries a `batchId` — the readings below are scoped to it.

The fixture's `manifest.json` plays no part here: the file is posted directly, and its policy comes from
`--duplicate-resolution`.

### 3. Record what the batch staged and what the domain holds, before discarding

```powershell
$before   = @((Invoke-RestMethod "$base/import/actions?batchId=$batchId&pageSize=0").items)
$awaiting = @($before | Where-Object { $_.status -ne 'Applied' })
$noOps    = @($before | Where-Object { $_.status -eq 'Applied' })
$before | ForEach-Object { "$($_.entityType) $($_.actionType) $($_.status)" }
$quotesBefore = (Invoke-RestMethod "$base/version").database.quotes
"actions=$($before.Count) awaiting=$($awaiting.Count) noOps=$($noOps.Count) quotes=$quotesBefore"
```

**Expected:** `awaiting` and `noOps` both at least `1` — measured as `Quote Modify Pending` and
`Source Unchanged Applied` — and the quote count recorded for comparison.

**On failure:** `awaiting=0` means discard has nothing to mark, and `noOps=0` means the batch can no
longer tell a correct discard from one that stamps every row. Either way, stop — this is a staging
problem, not a discard result.

### 4. Discard the batch

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/discard?batchId=$batchId" --expect 204 --status
```

**Expected:** `204`.

### 5. Read both again

```powershell
$after  = @((Invoke-RestMethod "$base/import/actions?batchId=$batchId&pageSize=0").items)
$status = @{}
$after | ForEach-Object { $status[$_.id] = $_.status }
$quotesAfter = (Invoke-RestMethod "$base/version").database.quotes

"actions=$($after.Count) sameCount=$($after.Count -eq $before.Count)"
"awaiting now Discarded = $(@($awaiting | Where-Object { $status[$_.id] -eq 'Discarded' }).Count) of $($awaiting.Count)"
"no-ops still Applied   = $(@($noOps | Where-Object { $status[$_.id] -eq 'Applied' }).Count) of $($noOps.Count)"
"quotes=$quotesAfter unchanged=$($quotesAfter -eq $quotesBefore)"
```

**Expected:** `sameCount=True`; every awaiting action now `Discarded` and every no-op still `Applied`,
each reading `N of N` with the counts from step 3; and `unchanged=True` — nothing was deleted, and the
domain tables were never touched.

A no-op is left as recorded because nothing was ever written for it, so a discard has nothing of it to
undo. Stamping it `Discarded` would record the rejection of a change that never existed.

## Observed effect

Not yet established as a captured record.

## Canary — run red against the build before #389

Per `docs/testing-policy.md`'s *Red first applies to automated tests, not only unit tests*. Run against
the #369 image this issue started from, tagged `quotinator:canary389`, in a container of its own
(`qt-import-04-canary389`, `18605`), 2026-09-10:

| Step | Assertion | Pre-work result |
|---|---|---|
| 3 | `awaiting` and `noOps` both at least `1` | passes — `Quote Modify Pending`, `Source Unchanged Applied`; staging is not what #389 changes |
| 4 | `204` | **fails** — `422`; the no-op made the batch read as already applied |
| 5 | `sameCount=True` | passes — a refused discard deletes nothing |
| 5 | awaiting now `Discarded`, `1 of 1` | **fails** — `0 of 1`; the pending action was left `Pending` |
| 5 | no-ops still `Applied`, `1 of 1` | passes — nothing touched it, on either build |
| 5 | `unchanged=True` | passes — the control; discard never writes a domain row |

The document as it stood before this rewrite could not have served as a canary: previewing the curated
file stages nothing but no-ops, so it failed on the fixed build too. Container and fixture directory
removed afterwards.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-04
Remove-Item -Recurse -Force (Join-Path $env:TEMP "qt-import-04-fixture")
```
