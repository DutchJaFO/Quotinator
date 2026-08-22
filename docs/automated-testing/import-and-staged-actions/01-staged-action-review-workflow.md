# The staged review → decide → apply workflow, end to end

**Smoke:** yes
**Traces to:** #45, #149, #152, #154

## Preconditions

A running container with an admin key and a seeded database — the curated file is re-imported against
already-seeded data, which is what makes its quotes genuine duplicates.

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

## Steps

**Confirm the endpoint is reachable, and that the legacy machinery is gone:**

```bash
curl -s "http://localhost:8080/api/v1/import/actions"
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/conflicts"
```

**Import under forced `review`, then list what it staged:**

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending"
```

Copy one pending action's `id` and its `batchId`.

**Decide, undo, decide again, then apply:**

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s "http://localhost:8080/api/v1/import/actions?status=Decided"
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/import/actions/<id>/undo"
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/apply?batchId=<lowercase the batchId here too>"
```

## Expected output

- The first call returns `200` with an empty or existing `items` list — the endpoint is reachable with
  no setup.
- **`/import/conflicts` must return `404`.** It was removed entirely in #154 Phase B; anything else
  means the legacy manual-review machinery has regressed back in.
- The import returns **`202`, not `200`** — the re-imported quotes are genuine duplicates left
  `Pending` under `review`.
- `status=pending` shows exactly the action(s) just created.
- After `decide`, `status=Decided` shows it. After `undo`, it is back under `status=Pending`. After
  deciding again, it is ready to apply.
- **If more than one action is pending, `apply` returns `422`** with a `pendingActionIds` array listing
  those still undecided. That is the batch-apply-atomicity contract working as designed, not a bug.
  Decide each remaining id the same way and re-run `apply` until it returns `200` and the quote's field
  reflects the decision.

## Observed effect

Not yet established as a captured record beyond the status transitions asserted above.

## Cleanup

> **Outstanding.** This currently leaves its applied batch in place, and other documents have been
> written assuming it. That is a dependency on execution order, which the index forbids: each test
> establishes what it needs. Either this test cleans up after itself and the others gain their own
> setup, or it runs against its own container and volume. Recorded as a finding for #339's audit —
> resolving it means writing new setup steps, not moving existing ones.
