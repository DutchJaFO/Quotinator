# An alias is stale only on a genuine rename, never on first creation

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #153
**Fully green after:** [#347](https://github.com/DutchJaFO/Quotinator/issues/347): the evaluation
line in steps 2 and 3 does not exist until then, so those readings are inconclusive; every other
reading passes

## Preconditions

An alias is stale **only** when the Source its own `canonicalTitle`/`canonicalType` deterministically
hashes to (`EntityIdentity.SourceId`, fixed at creation and never recomputed on a later Modify) already
exists **under a different current title**, a genuine rename since the alias was authored.

The distinction that matters: guiding the *first-ever* creation of a Source under its correct name is
the alias doing its normal job, not staleness.

Beyond the Fresh profile: both a fresh seed and a reseed are checked, because they exercise different
paths. The fresh seed is the profile's own first boot; this test issues the reseed itself, in the steps
below.

## Determinism

**Wait for the bundled seed to finish before checking**, the same partial-seed caveat as
[`16-conflict-rule-staleness.md`](16-conflict-rule-staleness.md). The profile's own readiness poll is
what gates that.

**Every existing unit fixture pre-seeded the canonical Source as a real DB row**, which masked both
bugs below. That is why this check is live: the failing condition only exists when the canonical Source
has *not* yet been created.

**The audit trail records `Purged`, not `Purge`.** This document counted the latter until #339's full
run, so it read `0` against 4 real traces and the "rules out an empty list" table below was satisfied
by a pattern that could never match.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-17 --port 18617
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18617/api/v1"
```

**Expected:** the app reports healthy, so the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Read the pending and stale lists after the fresh seed, before anything else runs

```powershell
(Invoke-RestMethod "$base/version").database

$log = docker logs qt-import-17 2>&1 | Out-String
$reportLinesFresh = ([regex]::Matches($log, '\[Database - Seed\].*report: ')).Count
"reportLines=$reportLinesFresh"
$log -split "`n" | Select-String -SimpleMatch 'alias staleness evaluated'

$audit = (Invoke-RestMethod "$base/admin/audit?table=Import_Action&pageSize=0" -Headers $key).items
"purgedTraces=$(@($audit | Where-Object { $_.operation -eq 'Purged' }).Count)"
"seedBatches=$((Invoke-RestMethod "$base/import/batches?type=seed").totalCount)"

"pending=$((Invoke-RestMethod "$base/import/actions?status=pending&pageSize=0").totalCount)"
"stale=$((Invoke-RestMethod "$base/import/actions?status=stale&pageSize=0").totalCount)"
```

**Expected:** the counts have settled; `reportLines` is non-zero, one per bundled file, each
rendering `stale=0`; a line states that source-alias staleness was **evaluated** and over how many
aliases; `purgedTraces` matches `seedBatches`; and both `pending` and `stale` read `0`.

**Each reading rules out a different way of producing those empty lists**, the same way
[`16-conflict-rule-staleness.md`](16-conflict-rule-staleness.md) sets out for conflict rules:

| Reading | Rules out |
|---|---|
| Report lines present, one per file | The seed never planned anything |
| `Purged` traces present | The action rows existed and were removed, leaving empty lists behind |
| Evaluation line present | The mechanism never compared the aliases at all |

**On failure:** a missing evaluation line makes this step inconclusive rather than passing: `stale=0`
in the report and an empty list are both produced equally by a mechanism that ran and found none and by
one that never ran. See the index's *When the expected situation does not occur*, cause 3, and
[#347](https://github.com/DutchJaFO/Quotinator/issues/347), which this test remains blocked on.

### 3. Reseed and repeat, which is the second of the two paths

```powershell
dotnet script scripts/testing/http.csx -- --method POST --url "$base/admin/database/reseed" --expect 200 --status

$after = docker logs qt-import-17 2>&1 | Out-String
$reportLinesAfter = ([regex]::Matches($after, '\[Database - Seed\].*report: ')).Count
$evaluationsAfter = ([regex]::Matches($after, 'alias staleness evaluated')).Count
"reportLines=$reportLinesFresh -> $reportLinesAfter increased=$($reportLinesAfter -gt $reportLinesFresh)"
"aliasEvaluations=$evaluationsAfter"

"pending=$((Invoke-RestMethod "$base/import/actions?status=pending&pageSize=0").totalCount)"
"stale=$((Invoke-RestMethod "$base/import/actions?status=stale&pageSize=0").totalCount)"
```

**Expected:** the reseed returns `200`; `increased=True` and `aliasEvaluations` has grown too, since the
reseed plans every bundled file again; and both `pending` and `stale` read `0`.

**Comparing against step 2 rather than asserting a number** is what makes this the second path rather
than a repeat of the first: the reseed's own lines are indistinguishable from first-boot lines except
by there being more of them.

Every real bundled alias's canonical Source either already exists under its exact recorded title, or is
being legitimately created for the first time. None has actually been renamed away, which is why zero
is the correct result here, and why the log line rather than the zero is what proves the mechanism
looked.

**On failure:** as in step 2: an unobservable evaluation makes both readings meaningless, and the
reseed's own status code is what separates "nothing was stale" from "the reseed never ran".

## Observed effect

**Two false-positive bugs were found and fixed live via this exact check, neither catchable by unit
tests alone.**

1. The first version checked only "does a Source with this exact title exist right now", which cannot
   distinguish a genuine rename from the alias's own legitimate job of guiding a first-ever creation.
   It flagged 7 real bundled aliases as stale purely because their canonical Source had not been created
   by an earlier file yet.
2. A same-batch fix, checking `sourceIndex`, the batch's own in-memory Add cache, still needed the
   id-based rewrite to fully clear a `SELECT *`-by-title query being unable to distinguish those same
   two cases when nothing had been indexed yet either.

## Canary

Build the commit before [#153](https://github.com/DutchJaFO/Quotinator/issues/153)'s first code
change, under a distinct tag, and run steps 2 and 3 against it. An alias is stale only on a genuine
rename and never on first creation, which is the distinction #153 drew, so that build reports
staleness on creation too.

**This document needs a step that makes an alias genuinely stale, and does not have one.** Steps 2 and
3 both read lists that must be empty, on a fresh seed and after a reseed, so between them they cover
two paths to the same absence and no path to the presence. Its sibling
[`16-conflict-rule-staleness.md`](16-conflict-rule-staleness.md) gained exactly that case, and until
this one does, a correct build and a staleness check that never runs read the same here.

So the canary is the rename itself: rename the Source the alias resolves to, reseed, and require the
alias to be reported stale. Until that step exists the document is run and reported as it stands,
which is what the `Fully green after:` field means by inconclusive.

Pin `Quotinator__AutoPurgeBundledImportActions` for every run, for the same reason step 2's emptiness
needs it.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-17
```
