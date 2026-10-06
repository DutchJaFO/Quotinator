# A rule whose recorded snapshot no longer matches reality stages Stale, not Decided

**Smoke:** no
**Environment:** Fresh
**Traces to:** #153
**Fully green after:** [#347](https://github.com/DutchJaFO/Quotinator/issues/347) — step 3's
evaluation line does not exist until then, so that reading is inconclusive; every other reading passes

## Preconditions

Each field a `ConflictResolutionRule` governs records its own `recordedIncomingValue`: the value that
field's incoming side held when the rule was authored (ADR 023). When that no longer matches the current
staging run's real incoming value, the rule is never silently reapplied — the action stages `Stale`. A
field with no recorded value at all stages `Stale` too, since it can never be confirmed fresh.

The entry-level `existingRecord`/`incomingRecord` snapshots are documentation only and are not read by
the matching logic — `existingRecord` has never been (#374), and `incomingRecord` stopped being read
when #420 moved the value onto the field it governs.

Beyond the Fresh profile: **a reseed is required; the profile's own first boot cannot exercise this.**
A brand-new database only ever stages `Add` actions, because nothing exists yet to conflict with.
`POST /admin/database/reseed` re-plans every bundled file against the now-populated database and
genuinely exercises the `Modify`/rule path — the same thing a real redeployment against an
already-seeded volume does. This test issues that reseed itself, in the steps below.

## Determinism

**Wait for the full bundled seed before querying.** A `status=stale` or `status=pending` check against
a container still working through its multi-file seed reads a partially-seeded, misleading state. The
profile's own readiness poll is what gates that.

**The shipped rule files are correct, so the bundled corpus alone can only ever show an empty list.**
That is the negative case, and step 3 asserts it with three readings that rule out a false empty. The
positive case is step 4, which makes staleness actually fire, and the document is not a pass without
both: an absence assertion on its own is equally satisfied by a mechanism that is entirely broken.

**Step 4 owns its input and never touches shipped data.** It builds the stale condition out of the
application's own mechanisms, per the suite index's *A test that needs a defective input must own that
input*. Editing a bundled rule file and rebuilding the image reaches the same state and is the
anti-pattern that section names: the mutation bakes into the shared `quotinator:local` tag, so every
sibling test on that tag runs data that is not in the repository.

**The audit trail records `Purged`, not `Purge`.** This document counted the latter until #339's full
run, so it read `0` against 8 real traces and the whole "rules out an empty list" table below was
satisfied by a pattern that could never match.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-16 --port 18616
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18616/api/v1"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Confirm the bundled seed produced content

```powershell
(Invoke-RestMethod "$base/version").database
```

**Expected:** non-zero counts — the container is no longer working through its multi-file seed, so a
reseed re-plans against a populated database rather than an empty one.

### 3. Reseed, then list the stale actions

```powershell
function Get-PurgedTraces {
  $audit = (Invoke-RestMethod "$base/admin/audit?table=Import_Action&pageSize=0" -Headers $key).items
  @($audit | Where-Object { $_.operation -eq 'Purged' }).Count
}
$purgedBefore = Get-PurgedTraces

dotnet script scripts/testing/http.csx -- --method POST --url "$base/admin/database/reseed" --expect 200 --status

$log = docker logs qt-import-16 2>&1 | Out-String
"reportLines=$(([regex]::Matches($log, '\[Database - Seed\].*report: ')).Count)"
$log -split "`n" | Select-String -SimpleMatch 'rule staleness evaluated'

$purgedAfter = Get-PurgedTraces
"purgedTraces=$purgedBefore -> $purgedAfter increased=$($purgedAfter -gt $purgedBefore)"

"stale=$((Invoke-RestMethod "$base/import/actions?status=stale&pageSize=0").totalCount)"
```

**Expected:** the reseed returns `200`; `reportLines` is non-zero, one per bundled
file, each rendering `stale=0`; a line states that rule staleness was **evaluated** and over how many
rules; `increased=True`; and `stale=0`.

**The purge traces are compared before and after, not against the batch count.** The first boot
already purged one set, so after a reseed the total is *two* rounds — measured `4` then `8` on
2026-08-26 against four bundled files. An equality with the seed-batch count holds only on a fresh boot,
which is [`17`](17-source-alias-staleness.md)'s step 2, not this one. What this step needs is that the
reseed's own action rows existed and were removed, and the delta states exactly that.

**Each reading rules out a different way of producing that empty list**, which is why an empty list on
its own establishes nothing:

| Reading | Rules out |
|---|---|
| Report lines present, one per file | The reseed never re-planned anything |
| `Purged` traces increased | The action rows existed and were removed, leaving an empty list behind |
| Evaluation line present | The mechanism never compared the rules at all |

**`stale=0` in the report cannot carry the last one.** It is produced identically by *compared the
shipped rules, none had drifted* and by *never compared anything* — a count of zero is not evidence
that something looked.

**On failure:** `reportLines=0` means the reseed did not re-plan — a setup failure, not a staleness
result; stop. A missing evaluation line means the mechanism's own execution is unobservable, so neither
the report nor the empty list can establish whether it ran. That is the application's gap rather than
this document's — see the index's *When the expected situation does not occur*, cause 3, and
[#347](https://github.com/DutchJaFO/Quotinator/issues/347), which this test remains blocked on.

### 4. Make staleness fire, and confirm it does

Step 3 can only ever show an absence. This step produces the opposite outcome in the same run, built
entirely from the application's own mechanisms, so nothing outside the test changes and the `DELETE`
below clears it.

The condition needs two things at once: a rule whose recorded value no longer matches the bundled
file's incoming value, **and** a live conflict on that same field, since a rule is only judged when it
is consulted, and it is only consulted when the field actually disagrees.

```powershell
$fixture = Join-Path $env:TEMP "qt-import-16-fixture"
$ruleFile = 'quotinator-curated-conflict-rules.json'

# A conflict against a real curated quote, decided, with a rule generated from that snapshot.
dotnet script scripts/testing/stage-import-conflict.csx -- --imports $fixture
$quoteId = (Get-Content (Join-Path $fixture "conflicting.json") -Raw | ConvertFrom-Json).quotes[0].id
$batch1  = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file (Join-Path $fixture "conflicting.json") --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$action1 = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batch1&pageSize=0").items[0].id
Invoke-RestMethod -Method Post -Uri "$base/import/actions/$action1/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"}}' | Out-Null
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/rules/conflict/generate?fileName=$ruleFile&origin=Bundled&batchId=$batch1" --expect 200 | Out-Null

# Move the stored value away from both, so the reseed finds a real conflict on that field.
# Derived from the first fixture so only the text differs; nothing about the quote is restated here.
$second = Get-Content (Join-Path $fixture "conflicting.json") -Raw | ConvertFrom-Json
$second.quotes[0].quote = "A second deliberately different text, staged after the rule was generated."
$second | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixture "second.json") -Encoding utf8
$batch2  = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file (Join-Path $fixture "second.json") --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$action2 = (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batch2&pageSize=0").items[0].id
Invoke-RestMethod -Method Post -Uri "$base/import/actions/$action2/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"quoteText":{"choice":"replace"}}' | Out-Null
dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$batch2" --expect 200 --status | Out-Null

dotnet script scripts/testing/http.csx -- --method POST --url "$base/admin/database/reseed" --expect 200 --status | Out-Null
"stale=$((Invoke-RestMethod "$base/import/actions?status=stale&pageSize=0").totalCount)"
(Invoke-RestMethod "$base/import/actions?status=stale&pageSize=0").items |
  Select-Object entityId, actionType, status
```

**Expected:** `stale=1`, and the row is a `Modify` on `$quoteId` with status `Stale`. The rule recorded
the first fixture's text; the reseed offers the curated file's own text; the two differ, so the rule is
refused rather than reapplied.

**The rule file must be the one that governs the quote.** Each bundled file's rule file governs only
that file's own batch, so a rule generated into a different file's rule file is never consulted and
this step reports `stale=0` for a reason that has nothing to do with staleness. The fixture targets a
curated quote, so the rule file is the curated one.

**On failure:** `stale=0` means either the rule was never consulted (wrong rule file, or the stored
value never diverged so the reseed found no conflict) or staleness is genuinely broken. Check that the
`generate` call returned `rulesAdded=1` and that the quote's stored text is the second fixture's before
reading the zero as a staleness result.

### 5. Remove the override

```powershell
dotnet script scripts/testing/http.csx -- --method DELETE `
  --url "$base/import/rules/conflict?fileName=$ruleFile&origin=Bundled" --expect 204 --status
Remove-Item $fixture -Recurse -Force -ErrorAction SilentlyContinue
```

**Expected:** `204`. The override is registered state, and a test leaves nothing behind that another
did not ask for.

## Observed effect

**Step 4 live-verified 2026-10-06**: `stale=1`, a `Modify` on the curated Airplane! quote
(`588334be-…`) with status `Stale`, after the rule recorded the first fixture's text and the reseed
offered the curated file's own. This is also what establishes that staleness still fires after #420
moved the recorded value onto the field it governs, which step 3's empty list cannot show.

**Step 3 live-verified 2026-07-26 against a genuine, pre-existing data bug this mechanism caught on its
first real run — not a contrived fixture.**

`nikhilnamal17-conflict-rules.json`'s Zootopia rule (`entityId: 10e3fb48-…`, governing `quoteText`
with `Keep`) had its snapshot recorded with a straight apostrophe (`Life's`), while the real bundled
`NikhilNamal17_popular-movie-quotes.json` entry uses a curly one. A genuine drift between
the rule's recorded assumption and reality, caught by the mechanism rather than by review.

## Cleanup

```powershell
Remove-Item (Join-Path $env:TEMP "qt-import-16-fixture") -Recurse -Force -ErrorAction SilentlyContinue
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-16
```
