# An alias is stale only on a genuine rename, never on first creation

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #153

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

**The empty lists are explained by data, never by the audit trail.** This document used to compare the
count of `Purged` audit rows with the count of seed batches. Measured 2026-11-13, that proved nothing:
the seed writes a `Purged` row for every batch whatever it removed (#348). What does carry it is the
batch itself: a seed batch is marked `applied`, and only then purged, when none of its actions is
pending, blocked or stale. On the reseed, the response reports each file's counts directly.

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

$seed = @((Invoke-RestMethod "$base/import/batches?type=seed&pageSize=0").items)
"seedBatches=$($seed.Count) applied=$(@($seed | Where-Object { $_.status -eq 'applied' }).Count) records=$(($seed | Measure-Object -Property recordCount -Sum).Sum)"

"pending=$((Invoke-RestMethod "$base/import/actions?status=pending&pageSize=0").totalCount)"
"stale=$((Invoke-RestMethod "$base/import/actions?status=stale&pageSize=0").totalCount)"
```

**Expected:** the counts have settled; `reportLines` is non-zero, one per bundled file, each
rendering `stale=0`; a line states that source-alias staleness was **evaluated** and over how many
aliases; `applied` equals `seedBatches` and `records` is above `0`; and both `pending` and `stale` read
`0`.

**Each reading rules out a different way of producing those empty lists**, the same way
[`16-conflict-rule-staleness.md`](16-conflict-rule-staleness.md) sets out for conflict rules:

| Reading | Rules out |
|---|---|
| Report lines present, one per file | The seed never planned anything |
| Every seed batch `applied`, with records above `0` | Pending or stale actions were left and then removed, leaving empty lists behind: a batch is marked `applied`, and purged, only when none of its actions is pending, blocked or stale |
| Evaluation line present | The mechanism never compared the aliases at all |

**On failure:** a missing evaluation line fails this step: `stale=0` in the report and an empty list are
both produced equally by a mechanism that ran and found none and by one that never ran. The application
does not yet say whether it ran, which is [#347](https://github.com/DutchJaFO/Quotinator/issues/347)'s to
fix; see the index's *When the expected situation does not occur*, cause 3. Stop: do not run step 3.

### 3. Reseed and repeat, which is the second of the two paths

```powershell
$reseedBody = Join-Path $env:TEMP "qt-import-17-reseed.json"
dotnet script scripts/testing/http.csx -- --method POST --url "$base/admin/database/reseed" --expect 200 --body-out $reseedBody | Out-Null
$reports = @((Get-Content $reseedBody -Raw | ConvertFrom-Json).reports)
$counts  = @($reports | ForEach-Object { $_.entityTypes.PSObject.Properties.Value })
"reportFiles=$($reports.Count) staged=$(($counts | ForEach-Object { $_.pending + $_.stale + $_.modified + $_.blocked } | Measure-Object -Sum).Sum)"
Remove-Item $reseedBody -ErrorAction SilentlyContinue

$after = docker logs qt-import-17 2>&1 | Out-String
$reportLinesAfter = ([regex]::Matches($after, '\[Database - Seed\].*report: ')).Count
$evaluationsAfter = ([regex]::Matches($after, 'alias staleness evaluated')).Count
"reportLines=$reportLinesFresh -> $reportLinesAfter increased=$($reportLinesAfter -gt $reportLinesFresh)"
"aliasEvaluations=$evaluationsAfter"

"pending=$((Invoke-RestMethod "$base/import/actions?status=pending&pageSize=0").totalCount)"
"stale=$((Invoke-RestMethod "$base/import/actions?status=stale&pageSize=0").totalCount)"
```

**Expected:** the reseed returns `200` with one report per bundled file and `staged=0`, so it staged
nothing that a purge could have removed; `increased=True` and `aliasEvaluations` has grown too, since the
reseed plans every bundled file again; and both `pending` and `stale` read `0`.

**Comparing against step 2 rather than asserting a number** is what makes this the second path rather
than a repeat of the first: the reseed's own lines are indistinguishable from first-boot lines except
by there being more of them.

Every real bundled alias's canonical Source either already exists under its exact recorded title, or is
being legitimately created for the first time. None has actually been renamed away, which is why zero
is the correct result here, and why the log line rather than the zero is what proves the mechanism
looked.

**On failure:** as in step 2: an unobservable evaluation makes both readings meaningless, and the
reseed's own status code is what separates "nothing was stale" from "the reseed never ran". Stop: do not
run step 4.

### 4. Rename an alias's canonical Source, and confirm the alias is reported stale

Steps 2 and 3 can only show an absence. This step produces the presence in the same run: it renames the
Source a real bundled alias resolves to, which is exactly the genuine rename the Preconditions define, and
requires the reseed to report the alias stale. `vilaboim-source-aliases.json` maps `Star Wars` onto
`Star Wars: Episode IV - A New Hope`, and `vilaboim_movie-quotes.json` carries a quote whose source is
`Star Wars`, so the reseed consults that alias.

The rename is an import of a Source correction: a `sources[]` entry carrying the Source's own id, which
matches by id so the title becomes correctable (`schemas/source-extended.schema.json`). A seeded Source is
never `Complete`, which is human-set only, so the correction stages `Pending` rather than `Blocked`, and a
Source's title is decided as `sourceTitle`.

**The file carries one quote of its own.** An import with no quotes is refused (`File contained no quotes`),
although the schema allows an empty list. The quote is new and names `Airplane!`, a seeded title unrelated to
the rename: a quote naming the canonical title could resolve to, or recreate, a Source under it and hide the
rename.

```powershell
$fixture   = Join-Path $env:TEMP "qt-import-17-fixture"
$canonical = 'Star Wars: Episode IV - A New Hope'
$renamed   = "$canonical (renamed by import-and-staged-actions/17)"
New-Item -ItemType Directory -Force $fixture | Out-Null

$source = @((Invoke-RestMethod "$base/masterdata/sources?pageSize=0").items |
  Where-Object { $_.title -eq $canonical -and $_.type -eq 'movie' })
"canonicalSources=$($source.Count)"

$correction = @{
  quotes  = @(@{ id = 'a1111117-0000-4000-8000-000000000001'; quote = 'A quote that carries the import-and-staged-actions/17 rename.'; originalLanguage = 'en'; source = 'Airplane!'; date = '1980'; type = 'movie'; genres = @(); translations = @{} })
  sources = @(@{ id = $source[0].id; title = $renamed; type = 'movie' })
}
$correction | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixture "rename.json") -Encoding utf8
$batch = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
            --file (Join-Path $fixture "rename.json") --duplicate-resolution review --expect 202 `
          | ConvertFrom-Json).batchId
$action = @((Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batch&pageSize=0").items |
  Where-Object { $_.entityType -eq 'Source' })
"sourceActions=$($action.Count) ambiguous=$($action[0].ambiguousFields -join ',')"
Invoke-RestMethod -Method Post -Uri "$base/import/actions/$($action[0].id)/decide" -Headers $key `
  -ContentType 'application/json' -Body '{"sourceTitle":{"choice":"replace"}}' | Out-Null
dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/actions/apply?batchId=$batch" --expect 200 --status | Out-Null
"title=$((Invoke-RestMethod "$base/masterdata/sources/$($source[0].id)").title)"

$reseedBody = Join-Path $env:TEMP "qt-import-17-reseed-renamed.json"
dotnet script scripts/testing/http.csx -- --method POST --url "$base/admin/database/reseed" --expect 200 --body-out $reseedBody | Out-Null
$counts = @(@((Get-Content $reseedBody -Raw | ConvertFrom-Json).reports) | ForEach-Object { $_.entityTypes.PSObject.Properties.Value })
"reportedStale=$(($counts | Measure-Object -Property stale -Sum).Sum)"
Remove-Item $reseedBody -ErrorAction SilentlyContinue
"stale=$((Invoke-RestMethod "$base/import/actions?status=stale&pageSize=0").totalCount)"
```

**Expected:** `canonicalSources=1`; `sourceActions=1` with `ambiguous=title`; `title` reads the renamed
title once the batch is applied; and after the reseed, `reportedStale` and `stale` both above `0`. The
alias still names `Star Wars: Episode IV - A New Hope` as canonical, the Source that title hashes to now
carries another, so the alias no longer describes it and the quote it governs stages `Stale` rather than
being linked by an alias that has gone out of date.

**On failure:** `canonicalSources=0` means the seed did not create the alias's canonical Source, so there
is nothing to rename and the step proves nothing. `sourceActions=0` or a `title` that still reads the
canonical one means the rename did not take effect. Either is a setup failure, not a staleness result: stop.
`stale=0` with the rename in place means the staleness check did not fire on a genuine rename, the defect
this document exists to catch.

## Observed effect

**Measured 2026-11-13** against `quotinator:local` built from `5285a211`'s tree, after steps 2 and 3 moved
off the audit trail (#348). Step 2: `5` report lines; `5` seed batches, all `applied`, `795` records;
`pending=0` and `stale=0`. Step 3: the reseed's response carried `5` file reports and `staged=0`; report
lines `5 -> 10`; `pending=0` and `stale=0`. `aliasEvaluations=0` in both: the evaluation line does not
exist yet, the #347 gap, so step 2 failed.

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

**Step 4 is the control.** Steps 2 and 3 both read lists that must be empty, on a fresh seed and after a
reseed, so between them they cover two paths to the same absence. Step 4 renames the Source an alias
resolves to and requires the alias to be reported stale, the presence, as its sibling
[`16-conflict-rule-staleness.md`](16-conflict-rule-staleness.md)'s step 4 does for conflict rules. Without
it, a correct build and a staleness check that never runs would read the same here. Step 4 was added by
#503 and has no recorded canary reading yet.

Pin `Quotinator__AutoPurgeBundledImportActions` for every run, for the same reason step 2's emptiness
needs it.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-17
Remove-Item (Join-Path $env:TEMP "qt-import-17-fixture") -Recurse -Force -ErrorAction SilentlyContinue
```
