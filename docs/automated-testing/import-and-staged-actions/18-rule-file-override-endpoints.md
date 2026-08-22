# Rule-file override endpoints, and the alias-candidate suggestion endpoint

**Smoke:** no
**Environment:** Fresh
**Traces to:** #153

## Preconditions

`GET` / `POST /generate` / `DELETE` under `/api/v1/import/rules/conflict`, plus the read-only
`GET /api/v1/import/rules/alias`.

Beyond the Fresh profile: nothing. No override is registered for any bundled rule file at the start, so
the effective content is the bundled copy — the first `GET` below confirms that rather than assuming
it. The generate step needs a real batch with a decided field to generate from, and this test stages
and decides one itself.

## Determinism

- **The override must be deleted at the end.** It is registered state, and a test leaves nothing behind
  that another did not ask for.
- **The alias-candidate check is structural, not a candidate count** — see Observed effect. Asserting a
  specific number of candidates would fail for a correct reason the moment a data-quality fix lands.

## Steps

**Confirm no override is active:**

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled"
```

**Stage a batch and decide one action to generate from:**

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=1"
```

Copy one pending action's `id` and the response's own `batchId`, then:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/rules/conflict/generate?fileName=quotinator-curated-conflict-rules.json&origin=Bundled&batchId=<batchId>"
```

Re-run the first `GET` from this test, then remove the override — and repeat the `DELETE`:

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled"
curl -s -w "\n%{http_code}\n" -X DELETE -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled"
curl -s -w "\n%{http_code}\n" -X DELETE -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled"
```

Finally, the alias-candidate suggestion endpoint — read-only, no key needed:

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/rules/alias?fileName=quotinator-curated-source-aliases.json&origin=Bundled"
```

## Expected output

- The first `GET` returns `200` with `isOverrideActive: false` and the bundled file's own rules.
- `generate` returns `200` with `isOverrideActive: true`, `rulesAdded` at least `1`, and a `rules`
  array **still containing every rule the bundled file already had** — the merge-preserves-existing-rules
  guarantee `EffectiveRuleFileResolver` exists for.
- The repeated `GET` now returns `isOverrideActive: true`, proving the override took effect for reads.
- `DELETE` returns `204`; a repeat `DELETE` returns `404`.
- The alias endpoint returns `200` with a well-formed `candidates` array.

**What the alias check verifies is structural**: `200` with a well-formed array, confirming the endpoint
runs cleanly end to end against the full live `Quotinator_Source` table. **Not a candidate count.**

## Observed effect

Originally live-verified 2026-07-26 against real bundled data, surfacing 3 genuine near-duplicates the
curated alias file did not cover: `"When Harry Met Sally"` vs `"When Harry Met Sally..."`,
`"Avengers - Age of Ultron"` vs `"Avengers: Age of Ultron"` — the normalizer strips `-` and `:`
identically, correctly catching this — and `"Airplane"` vs `"Airplane!"`, aliased in a different
bundled file's alias list at the time.

**All 3 were added to `nikhilnamal17-source-aliases.json` as a data-quality follow-up (confirmed
2026-08-08)**, so a run against current `main` correctly returns an **empty** `candidates` array for
this query. That is the fix working, not a regression of the endpoint.

If a future bundled-source refresh introduces a genuinely new near-duplicate, it appears here again. A
confirmed one should be filed as a data-quality follow-up per
[`docs/workflow/source-verification.md`](../../workflow/source-verification.md), **not fixed inline as
part of a test run.**

## Cleanup

The `DELETE` above removes the override. Confirm the first of the two returned `204` before moving on.

That is not everything this test leaves behind: the `review` import stages a batch against the curated
file and one of its actions is decided but never applied. Restore the Fresh profile before the next
test.
