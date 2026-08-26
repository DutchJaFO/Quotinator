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
- **The before/after rule ids are compared as sets of objects**, not as sorted text files. The
  comparison is "is every id that was there before still there", which a set difference states
  directly.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-18 --port 18618
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18618/api/v1"
$ruleFile = 'nikhilnamal17-conflict-rules.json'
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Confirm no override is active, and capture the bundled file's rules for comparison

```powershell
$before = dotnet script scripts/testing/http.csx -- `
  --url "$base/import/rules/conflict?fileName=$ruleFile&origin=Bundled" --expect 200 | ConvertFrom-Json

$idsBefore = @($before.rules.entityId)
"isOverrideActive=$($before.isOverrideActive) rules=$($idsBefore.Count)"
```

**Expected:** `200` with `isOverrideActive=False`, and a non-zero rule count — 13 at the time of
writing, but the assertion is "not zero", not the figure.

**The file has to be one that ships with rules, which is why it is `nikhilnamal17-conflict-rules.json`.**
This document named `quotinator-curated-conflict-rules.json` until #339's full run, and that file ships
`"rules": []` — so the before-capture was empty and step 5's "every rule present before is still present
after" could not fail. The document already warned that zero rules would make the assertion vacuous, and
then named the one bundled file that has zero. Counts as shipped today: nikhilnamal17 13, vilaboim 36,
series-universe 1, curated 0.

**On failure:** a zero count means whichever file is named here has no rules, and step 5 then proves
nothing regardless of what the merge does. Stop and pick a file that has some.

**The before-capture is what makes the merge assertion real.** "Still containing every rule the bundled
file already had" cannot be evaluated against a single after-reading — a `generate` that discarded the
bundled rules and returned only its own would need the reader to have memorised the earlier output to
notice.

### 3. Stage a batch to generate from

```powershell
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file data/sources/quotinator-curated.json --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$actionId = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").items[0].id
"batchId=$batchId actionId=$actionId"
```

**Expected:** both values non-empty — at least one pending action was staged, and the next step needs
both.

### 4. Decide one action, and generate the rule-file override from it

```powershell
Invoke-RestMethod -Method Post -Uri "$base/import/actions/$actionId/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"}}' | Out-Null

$generated = dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/rules/conflict/generate?fileName=$ruleFile&origin=Bundled&batchId=$batchId" `
  --expect 200 | ConvertFrom-Json
"isOverrideActive=$($generated.isOverrideActive) rulesAdded=$($generated.rulesAdded)"
```

**Expected:** `generate` returns `200` with `isOverrideActive=True` and `rulesAdded` at least `1`.

### 5. Re-read the effective rules, and compare them against the before-capture

```powershell
$after = dotnet script scripts/testing/http.csx -- `
  --url "$base/import/rules/conflict?fileName=$ruleFile&origin=Bundled" --expect 200 | ConvertFrom-Json

$idsAfter = @($after.rules.entityId)
"isOverrideActive=$($after.isOverrideActive) rules=$($idsAfter.Count)"

$dropped = @($idsBefore | Where-Object { $_ -notin $idsAfter })
"dropped=$($dropped.Count) $($dropped -join ' ')"
```

**Expected:** the repeated `GET` returns `isOverrideActive=True`, proving the override took effect for
reads, and **`dropped=0`.** Every rule id present before is still present after — that is
the merge-preserves-existing-rules guarantee `EffectiveRuleFileResolver` exists for, and the only form
in which it can actually fail. Any id listed is a bundled rule the merge dropped.

### 6. Remove the override, and repeat the `DELETE`

```powershell
dotnet script scripts/testing/http.csx -- --method DELETE `
  --url "$base/import/rules/conflict?fileName=$ruleFile&origin=Bundled" --expect 204 --status
dotnet script scripts/testing/http.csx -- --method DELETE `
  --url "$base/import/rules/conflict?fileName=$ruleFile&origin=Bundled" --expect 404 --status
```

**Expected:** `DELETE` returns `204`; a repeat `DELETE` returns `404`.

### 7. Call the alias-candidate suggestion endpoint — read-only, no key needed

```powershell
$alias = dotnet script scripts/testing/http.csx -- `
  --url "$base/import/rules/alias?fileName=quotinator-curated-source-aliases.json&origin=Bundled" `
  --no-key --expect 200 | ConvertFrom-Json
"hasCandidates=$($alias.PSObject.Properties.Name -contains 'candidates') candidates=$(@($alias.candidates).Count)"
```

**Expected:** `200`, `hasCandidates=True`, and a well-formed `candidates` array. Called with `--no-key`
deliberately: this endpoint is read-only and requires none, and sending one would leave that untested.

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

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-18
```

The `DELETE` above removes the override. Confirm the first of the two returned `204` before moving on.
