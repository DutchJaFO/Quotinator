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
- **The curated re-import stages more than one action**, so a single `decide` is never enough to apply
  — which is why step 8 loops over every pending id rather than naming a number. The count is a
  property of the bundled file and moves when it changes: two when this was written, 13 when measured
  during #339's full run.
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
response=$(curl -s -w "\n%{http_code}" -X POST -H "X-Api-Key: smoketest" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:18601/api/v1/import")
echo "$response" | tail -1
batchId=$(echo "$response" | grep -o '"batchId":"[^"]*"' | cut -d'"' -f4)
echo "batchId=$batchId"
```

**Expected:** `202`, **not** `200` — the re-imported quotes are genuine duplicates left `Pending` under
`review` — and a non-empty `batchId`, which every step below is scoped to.

**On failure:** a `200` here means the policy did not take effect and nothing was staged, so the rest of
this document would be testing an empty batch. An empty `batchId` means the same thing one step earlier.
Stop.

### 5. List this batch's pending actions

Scoped to this batch, and the first action id captured for the steps that follow:

```bash
curl -s "http://localhost:18601/api/v1/import/actions?status=pending&batchId=$batchId&pageSize=0" \
  | grep -o '"id":"[0-9a-f-]\{36\}"' | wc -l
actionId=$(curl -s "http://localhost:18601/api/v1/import/actions?status=pending&batchId=$batchId&pageSize=0" \
           | grep -o '"id":"[0-9a-f-]\{36\}"' | head -1 | cut -d'"' -f4)
echo "actionId=$actionId"
```

**Expected:** a non-zero count — exactly the actions the import just created — and a non-empty
`actionId`.

### 6. Decide that action, and confirm it moved

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Api-Key: smoketest" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:18601/api/v1/import/actions/$actionId/decide"
curl -s "http://localhost:18601/api/v1/import/actions?status=Decided&batchId=$batchId&pageSize=0" \
  | grep -c "$actionId"
```

**Expected:** the decide returns `204`, and the `Decided` listing contains that action.

### 7. Undo the decision

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18601/api/v1/import/actions/$actionId/undo"
curl -s "http://localhost:18601/api/v1/import/actions?status=Pending&batchId=$batchId&pageSize=0" \
  | grep -c "$actionId"
```

**Expected:** the action is back under `status=Pending` — the listing contains it again.

### 8. Decide every action in the batch

`apply` is all-or-nothing, so the rest have to be decided too — including any the import staged for
other entity types:

```bash
for id in $(curl -s "http://localhost:18601/api/v1/import/actions?status=Pending&batchId=$batchId&pageSize=0" \
            | grep -o '"id":"[0-9a-f-]\{36\}"' | cut -d'"' -f4); do
  curl -s -o /dev/null -X POST -H "X-Api-Key: smoketest" -H "Content-Type: application/json" \
    -d '{"quoteText":{"choice":"keep"}}' \
    "http://localhost:18601/api/v1/import/actions/$id/decide"
done
curl -s "http://localhost:18601/api/v1/import/actions?status=Pending&batchId=$batchId&pageSize=0" \
  | grep -o '"totalCount":[0-9]*'
```

**Expected:** `"totalCount":0` — nothing left pending, so `apply` can succeed.

### 9. Apply the batch, with the `batchId` lowercased

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18601/api/v1/import/actions/apply?batchId=$(echo "$batchId" | tr 'A-Z' 'a-z')"
```

**Expected:** `200`.

**On failure:** **if any action is still pending, `apply` returns `422`** with a `pendingActionIds`
array listing those still undecided. That is the batch-apply-atomicity contract working as designed, not
a bug — step 8's `totalCount` is what confirms it should not happen here.

### 10. Read the batch's final status tally

```bash
curl -s "http://localhost:18601/api/v1/import/actions?batchId=$batchId&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

**Expected:** after a successful apply, every action in the batch reads `Applied`.

## Observed effect

Not yet established as a captured record beyond the status transitions asserted above.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-01
```
