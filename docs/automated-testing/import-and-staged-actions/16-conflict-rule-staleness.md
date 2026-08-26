# A rule whose recorded snapshot no longer matches reality stages Stale, not Decided

**Smoke:** no
**Environment:** Fresh
**Traces to:** #153

## Preconditions

A `ConflictResolutionRule` records an `existingRecord`/`incomingRecord` snapshot. When those no longer
match the current staging run's real field values, the rule is never silently reapplied — the action
stages `Stale`.

Beyond the Fresh profile: **a reseed is required; the profile's own first boot cannot exercise this.**
A brand-new database only ever stages `Add` actions, because nothing exists yet to conflict with.
`POST /admin/database/reseed` re-plans every bundled file against the now-populated database and
genuinely exercises the `Modify`/rule path — the same thing a real redeployment against an
already-seeded volume does. This test issues that reseed itself, in the steps below.

## Determinism

**Wait for the full bundled seed before querying.** A `status=stale` or `status=pending` check against
a container still working through its multi-file seed reads a partially-seeded, misleading state. The
profile's own readiness poll is what gates that.

**The shipped rule file is already corrected**, so a run against current `main` returns an empty list.
To see the "before" state, use `scripts/testing/conflict-rule.csx` to change the rule's recorded
snapshot and rebuild the image — do not treat the empty result as a failure.

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

## Observed effect

**Live-verified 2026-07-26 against a genuine, pre-existing data bug this mechanism caught on its first
real run — not a contrived fixture.**

`nikhilnamal17-conflict-rules.json`'s Zootopia rule (`entityId: 10e3fb48-…`, governing `quoteText`
with `Keep`) had its snapshot recorded with a straight apostrophe (`Life's`), while the real bundled
`NikhilNamal17_popular-movie-quotes.json` entry uses a curly one. A genuine drift between
the rule's recorded assumption and reality, caught by the mechanism rather than by review.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-16
```
