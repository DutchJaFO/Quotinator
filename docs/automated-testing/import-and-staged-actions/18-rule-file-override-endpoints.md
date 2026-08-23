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

Run the **Fresh** profile first.

### 1. Confirm no override is active, and capture the bundled file's rules for comparison

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled" \
  -o /tmp/rules-before.json
grep -o '"isOverrideActive":[a-z]*' /tmp/rules-before.json
grep -o '"entityId":"[^"]*"' /tmp/rules-before.json | sort > /tmp/rule-ids-before.txt
wc -l < /tmp/rule-ids-before.txt
```

**Expected:** `200` with `isOverrideActive:false`, and a non-zero rule count written to
`/tmp/rule-ids-before.txt`. Zero rules there would make the merge assertion vacuous.

**The before-capture is what makes the merge assertion real.** "Still containing every rule the bundled
file already had" cannot be evaluated against a single after-reading — a `generate` that discarded the
bundled rules and returned only its own would need the reader to have memorised the earlier output to
notice.

### 2. Stage a batch to generate from

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=1"
```

**Expected:** at least one pending action is listed. Copy its `id` and the response's own `batchId` —
the next step needs both.

### 3. Decide one action, and generate the rule-file override from it

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"quoteText":{"choice":"keep"}}' \
  "http://localhost:8080/api/v1/import/actions/<id>/decide"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/rules/conflict/generate?fileName=quotinator-curated-conflict-rules.json&origin=Bundled&batchId=<batchId>"
```

**Expected:** `generate` returns `200` with `isOverrideActive: true` and `rulesAdded` at least `1`.

### 4. Re-read the effective rules, and compare them against the before-capture

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled" \
  -o /tmp/rules-after.json
grep -o '"isOverrideActive":[a-z]*' /tmp/rules-after.json
grep -o '"entityId":"[^"]*"' /tmp/rules-after.json | sort > /tmp/rule-ids-after.txt
comm -23 /tmp/rule-ids-before.txt /tmp/rule-ids-after.txt
```

**Expected:** the repeated `GET` returns `isOverrideActive:true`, proving the override took effect for
reads, and **`comm -23` prints nothing.** Every rule id present before is still present after — that is
the merge-preserves-existing-rules guarantee `EffectiveRuleFileResolver` exists for, and the only form
in which it can actually fail. Any id printed is a bundled rule the merge dropped.

### 5. Remove the override, and repeat the `DELETE`

```bash
curl -s -w "\n%{http_code}\n" -X DELETE -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled"
curl -s -w "\n%{http_code}\n" -X DELETE -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/rules/conflict?fileName=quotinator-curated-conflict-rules.json&origin=Bundled"
```

**Expected:** `DELETE` returns `204`; a repeat `DELETE` returns `404`.

### 6. Call the alias-candidate suggestion endpoint — read-only, no key needed

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/rules/alias?fileName=quotinator-curated-source-aliases.json&origin=Bundled"
```

**Expected:** `200` with a well-formed `candidates` array.

**What the alias check verifies is structural**: `200` with a well-formed array, confirming the endpoint
runs cleanly end to end against the full live `Quotinator_Source` table. **Not a candidate count.**

**Stated plainly, because it limits what this proves:** current `main` correctly returns an *empty*
`candidates` array for this query, so the assertion reduces to "the route responds and the field
parses". An implementation with candidate detection removed entirely would pass it. The route is
covered here; the feature is not, and giving it real coverage needs a Source that genuinely has been
renamed — which is the same defective-input problem the index's *A test that needs a defective input
must own that input* section describes.

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

```bash
rm -f /tmp/rules-before.json /tmp/rules-after.json /tmp/rule-ids-before.txt /tmp/rule-ids-after.txt
```

The `DELETE` above removes the override. Confirm the first of the two returned `204` before moving on.

That is not everything this test leaves behind: the `review` import stages a batch against the curated
file and one of its actions is decided but never applied. Restore the Fresh profile before the next
test.
