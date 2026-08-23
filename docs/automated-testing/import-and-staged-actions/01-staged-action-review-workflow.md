# The staged review → decide → apply workflow, end to end

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #45, #149, #152, #154

## Preconditions

Nothing beyond the Fresh profile. What matters about the profile's own first-boot seed here is that the
curated file is re-imported against already-seeded data, which is what makes its quotes genuine
duplicates.

`/api/v1/import/actions/*` (#154's unified staging engine) is the live mechanism: every import and seed
run stages through it.

## Determinism

- **`review` policy is forced explicitly.** The endpoint would otherwise auto-resolve via the default
  policy and produce no pending action at all, leaving nothing to decide against.
- **`status=pending` is deliberately lowercase**, and the `batchId` on the apply call is deliberately
  lowercased too. Both prove the case-insensitive query-filter fix (#154) is still in effect — matching
  the stored casing would pass without testing anything.
- **The curated re-import currently produces two pending actions** (both `Airplane!` quotes), so a
  single `decide` is not enough to apply. That count is a property of the bundled file and will move if
  it changes; treat it as "decide every remaining id", not as an expected number.
- `ambiguousFields` is populated only where fields genuinely differ — re-importing the same file
  unmodified usually means they do not.
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

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-import-01 --port 18601
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Confirm the staging endpoint is reachable

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:18601/api/v1/import/actions"
```

**Expected:** `200` — the staging endpoint is reachable with no setup.

### 3. Confirm the legacy conflicts machinery is gone

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:18601/api/v1/import/conflicts"
```

**Expected:** `404`. It was removed entirely in #154 Phase B; anything else means the legacy
manual-review machinery has regressed back in.

### 4. Import the curated file under forced `review`

```bash
curl -s -X POST -H "X-Api-Key: smoketest" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:18601/api/v1/import"
```

**Expected:** `202`, **not** `200` — the re-imported quotes are genuine duplicates left `Pending` under
`review`.

**On failure:** a `200` here means the policy did not take effect and nothing was staged, so the rest of
this document would be testing an empty batch. Stop.

### 5. List this batch's pending actions

Copy the response's `batchId`, then list **only this batch's** pending actions and copy one action `id`:

```bash
curl -s "http://localhost:18601/api/v1/import/actions?status=pending&batchId=<batchId>&pageSize=0"
```

**Expected:** exactly the action(s) the import just created.

### 6. Decide that action, and confirm it moved

```bash
curl -s -X POST -H "X-Api-Key: smoketest" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:18601/api/v1/import/actions/<id>/decide"
curl -s "http://localhost:18601/api/v1/import/actions?status=Decided&batchId=<batchId>&pageSize=0"
```

**Expected:** after `decide`, `status=Decided` shows it.

### 7. Undo the decision

```bash
curl -s -X POST -H "X-Api-Key: smoketest" "http://localhost:18601/api/v1/import/actions/<id>/undo"
```

**Expected:** the action is back under `status=Pending`.

### 8. Decide it again

```bash
curl -s -X POST -H "X-Api-Key: smoketest" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:18601/api/v1/import/actions/<id>/decide"
```

**Expected:** it is ready to apply.

### 9. Apply the batch, with the `batchId` lowercased

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18601/api/v1/import/actions/apply?batchId=<lowercase the batchId here too>"
```

**Expected:** `200`.

**On failure:** **if more than one action is pending, `apply` returns `422`** with a `pendingActionIds`
array listing those still undecided. That is the batch-apply-atomicity contract working as designed, not
a bug. Decide each remaining id the same way and re-run `apply` until it returns `200`.

### 10. Read the batch's final status tally

```bash
curl -s "http://localhost:18601/api/v1/import/actions?batchId=<batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

**Expected:** after a successful apply, every action in the batch reads `Applied`.

## Observed effect

Not yet established as a captured record beyond the status transitions asserted above.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-01
```
