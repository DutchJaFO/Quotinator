# A fresh seed resolves every bundled file with nothing left pending

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #181
**Fully green after:** [#400](https://github.com/DutchJaFO/Quotinator/issues/400) — step 5 cannot run
until then: it calls a `--convert` entry point that does not exist

## Preconditions

Every bundled file runs under `review` policy with its own `ruleFile`/`sourceAliasFile`.

A `ConflictResolutionRule` auto-resolves a genuinely ambiguous field on an already-seen entity id
(Modify path only). A `SourceAliasRule` corrects a misspelled or inconsistent raw `(title, type)` to
the already-canonical Source **before** Source resolution runs — so it applies to both a first-seen Add
and a re-seen Modify, and prevents a duplicate Source row being created for the wrong spelling in the
first place.

**A `ConflictResolutionRule` alone cannot do that**: it only ever corrects what a Quote's own field
*displays*, never which Source row it links to.

Nothing beyond the Fresh profile. The seed this test inspects is the profile's own first boot.

## Determinism

- **This is the zero-failures assertion for the bundled dataset.** Nothing staged awaiting review is
  the fact; the number of quotes seeded is not asserted, only that content exists.
- **Copy the `-wal` and `-shm` sidecars** with the `.db` — see
  [`10-source-date-from-resolving-quote.md`](10-source-date-from-resolving-quote.md) for why a bare
  copy can silently omit committed data. Their copies are allowed to fail, because a cleanly stopped
  database has already checkpointed and removed them.
- The duplicate-Source query groups on `LOWER(Title)`, so a case-only difference counts as a duplicate.
  That is the point — the alias mechanism exists to prevent exactly that.
- **The values `/version` reports are data, not an expectation** — what matters is that seeding produced
  content, which is why every count is checked for being non-zero rather than against a figure.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-14 --port 18614
$base = "http://localhost:18614/api/v1"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Read what the first boot seeded

```powershell
$database = (Invoke-RestMethod "$base/version").database
$database
"zeroCounts=$(@($database.PSObject.Properties | Where-Object { $_.Name -ne 'schemaVersion' -and $_.Value -eq 0 }).Name -join ', ')"
```

**Expected:** every entity count is non-zero, so `zeroCounts` is empty. Named rather than counted, so a
newly-added entity type that seeds nothing reads as its own name rather than shifting a total.

### 3. Confirm nothing is left staged awaiting review

```powershell
$pending = dotnet script scripts/testing/http.csx -- --url "$base/import/actions?status=pending&pageSize=0" --expect 200 | ConvertFrom-Json
"pending=$($pending.totalCount)"
```

**Expected:** `200` and `pending=0`. No file is left staged awaiting review.

**On failure:** if anything is left pending, `docker logs` shows
`"<file>" left staged awaiting review — batch "<id>", N action(s) pending a decision`. Inspect via
`GET /import/actions?batchId=<id>` to see which entity or field lacks a rule or alias.

### 4. Cross-check for duplicate Sources

```powershell
docker stop -t 15 qt-import-14
docker cp qt-import-14:/data/quotinatordata.db .claude/temp/inspect-181.db
docker cp qt-import-14:/data/quotinatordata.db-wal .claude/temp/inspect-181.db-wal 2>$null
docker cp qt-import-14:/data/quotinatordata.db-shm .claude/temp/inspect-181.db-shm 2>$null
docker start qt-import-14
dotnet script scripts/testing/http.csx -- --url "$base/health" --wait-for 200 --status

# A: exact duplicates — the same work stored twice under the same date.
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" `
  --sql "SELECT Title, Type, Date, COUNT(*) AS c FROM Quotinator_Source WHERE IsDeleted = 0 GROUP BY LOWER(Title), Type, COALESCE(Date,'') HAVING c > 1"

# B: casing-only duplicates — the alias mechanism's own job, and never legitimate.
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" `
  --sql "SELECT LOWER(Title) AS t, Type, COUNT(DISTINCT Title) AS spellings FROM Quotinator_Source WHERE IsDeleted = 0 GROUP BY LOWER(Title), Type HAVING spellings > 1"

# C: every date variant is one somebody declared. An undeclared second date is the ambiguous case.
$declared = @{}
Get-ChildItem data/sources/*.json | ForEach-Object {
  $doc = Get-Content $_.FullName -Raw | ConvertFrom-Json
  foreach ($s in @($doc.sources)) {
    if ($s.title) { $declared["$($s.title.ToLower())|$($s.type.ToLower())|$($s.date)"] = $true }
  }
}

$variantRows = dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" `
  --sql "SELECT Title, Type, Date FROM Quotinator_Source WHERE IsDeleted = 0 AND LOWER(Title) IN (SELECT LOWER(Title) FROM Quotinator_Source WHERE IsDeleted = 0 GROUP BY LOWER(Title), Type HAVING COUNT(*) > 1)"

$undeclared = @($variantRows | Select-Object -Skip 1 | Where-Object { $_ -match '\S' } | ForEach-Object {
  $cols = ($_ -split '\s{2,}') | Where-Object { $_ -match '\S' }
  if ($cols.Count -ge 3 -and -not $declared["$($cols[0].Trim().ToLower())|$($cols[1].Trim().ToLower())|$($cols[2].Trim())"]) { $_.Trim() }
})
"undeclared date variants = $($undeclared.Count)"
$undeclared
```

**Expected:** **A**, **B** and **C** all return **no rows**. `undeclared date variants = 0`.

**C asserts; it does not list.** A title carrying two dates is ambiguous only while nobody has said
which reading applies, and there are exactly two ways to say it — a dated `SourceAliasRule` when one of
the dates is wrong, or a pair of `sources[]` declarations when the title really does name two works.
Once either is in place the variant is *permitted*, and C passes. Until then it fails, which is the
point: a wrong date must not reach the database unremarked just because nothing crashed.

**It was a listing until 2026-09-08, and that was the defect.** A row a human is asked to eyeball is a
promise rather than a verification — `process.md` refuses exactly that shape — so the check passed while
nine wrong dates sat in the database. Making it assert is what forced them to be resolved.

**A** is a true duplicate: nothing legitimises the same title, type *and* date stored twice.
**B** is an alias failure: two spellings of one title that `sourceAliasFile` should have merged onto a
canonical form. Neither is confounded by dates, which is what makes them assertable.

**Rewritten 2026-09-08, because the previous query could not pass.** It grouped on
`(LOWER(Title), Type)` alone and expected no rows — an assertion that
[#374](https://github.com/DutchJaFO/Quotinator/issues/374) had already made unsatisfiable:
`ImportActionPlanner`'s `ResolveSourceAsync` deliberately gives a second-or-later variant with a
different date its own Source row, *"to avoid colliding with the first"*. The proof it was the query
and not the data: **The Lion King** (1994 / 2019) is two genuinely distinct films, and the seed log
*warns about it by name* asking for exactly the confirmation **C** now collects — so the test failed on
behaviour the application announces as expected.

Measured on a fresh container the same day: the old query returned **11 rows**, of which **0** were
true duplicates (**A** empty), **2** were casing failures (**B**: `Back to the future` beside
`Back to the Future`, `The Silence of the lambs` beside `The Silence of the Lambs`) and the rest were
date variants. Eleven rows of mixed signal, where two of them were the real finding.

**Those two were then resolved, and B now returns no rows** (re-measured 2026-09-08 against a rebuilt
image). Not by changing how the importer behaves — by **declaring the canonical spelling**, two new
entries in `nikhilnamal17-source-aliases.json`.

**A code fix was written first and reverted, and the reason it was wrong is the rule this check now
enforces.** `ResolveSourceAsync` was made to adopt the stored title's casing when creating a variant.
That cleared B, but by hiding the disagreement: whichever spelling was stored first would become
canonical, so a first-seen `the mOvie Title` would silently absorb every later, correct
`The Movie Title` with nothing left to notice. Developer rule, 2026-09-08: **casing duplicates need to
be explicitly permitted; they must not be hidden.** The importer therefore keeps the incoming spelling
(`ResolveSourceAsync_DateVariantOfDifferentlyCasedTitle_KeepsBothSpellingsVisible`), both spellings
reach the database, this check reports them, and a reviewed `SourceAliasRule` is what resolves it
(`ResolveSourceAsync_DifferentlyCasedTitle_WithAliasDeclared_ResolvesToTheCanonicalSpelling`).

That is also why the matching itself was never at fault: with agreeing dates a case-only difference
already resolves to the existing row, proved by
`ResolveSourceAsync_QuoteWithDifferentlyCasedTitle_SameDate_ReusesTheExistingSource`. Only a
legitimately-created date variant could carry a second spelling into the database.

**C still lists 11 rows and is expected to**, because a wrong date is a data question, not a code one.
`Back to the Future` (`1985,1958`) and `The Silence of the Lambs` (`1991,1998`) are one film each with
one wrong date — now under a single spelling, visible in C rather than misreported by B.

The container is stopped for the copy, which this step did not do before: a copy taken while the app
holds the database open can omit rows the WAL has not yet checkpointed, and a *missing* duplicate reads
as a pass.

### 5. Confirm each bundled file still matches what its own converter produces

**This is the step that makes this document the suite's external-data sentinel**, and the only place in
the project allowed to go red because the outside world changed rather than because this project broke.
Developer rule, 2026-09-08: *tests should not rely on bundled data to stay green, with the exception of
the feature smoke test that exists purely to be aware of changes in the external data that may affect
our rules.* Unit tests therefore use fixtures; this step watches the real thing.

```powershell
$manifest = Get-Content data/sources/manifest.json -Raw | ConvertFrom-Json
foreach ($entry in $manifest.sources | Where-Object { $_.converter -and $_.github }) {
  $raw = "scripts/cache/$($entry.file)"
  if (-not (Test-Path $raw)) { "$($entry.file): no cached raw — skipped"; continue }

  $out = Join-Path $env:TEMP "regen-$($entry.file)"
  dotnet run --project src/Quotinator.Api -- --convert $raw $out --converter $entry.converter 2>$null

  $regen = (Get-Content $out -Raw | ConvertFrom-Json)
  $live  = (Get-Content "data/sources/$($entry.file)" -Raw | ConvertFrom-Json)
  $liveByKey = @{}; foreach ($q in $live.quotes ?? $live) { $liveByKey["$($q.quote) $($q.source)"] = $q.date }

  $drift = foreach ($q in ($regen.quotes ?? $regen)) {
    $k = "$($q.quote) $($q.source)"
    if ($liveByKey.ContainsKey($k) -and $liveByKey[$k] -ne $q.date) {
      "  $($q.source): checked-in=$($liveByKey[$k]) upstream=$($q.date)"
    }
  }
  "$($entry.file): $(@($drift).Count) diverging date(s)"
  $drift
}
```

**Expected:** `0 diverging date(s)` for every file.

**A divergence is not a converter bug — it means a correction was put somewhere that does not survive.**
`Quotinator:AutoUpdateSources` defaults to `true`, so a running container re-downloads the raw upstream
file and re-runs the converter over it at startup, overwriting the checked-in copy. Anything hand-edited
into `data/sources/` is discarded at runtime while still reading as fixed in the repository. The
supported mechanism is a `ConflictResolutionRule` in that file's own `ruleFile`, which survives
regeneration.

**Measured 2026-09-08 — five divergences in `NikhilNamal17_popular-movie-quotes.json`,** and in three
of them the checked-in value is the *correct* year while upstream's is wrong, so the live container
seeds worse data than the repository appears to hold:

| Quote / Source | Checked in | Seeded |
|---|---|---|
| "Do, or do not…" — Empire Strikes Back | `null` | 1890 |
| "Life is a banquet…" — Auntie Mame | 2005 | 1958 |
| "Even the smallest person…" — LOTR Fellowship | 2002 | 2001 |
| "Following's not really my style." — The Avengers | 2019 | 2012 |
| "I have nothing to prove to you" — Captain Marvel | 2019 | 2013 |

**Written as a unit test first, and that was the wrong place.** It lived in
`BasicJsonArrayConverterTests` for one commit; pinned to bundled and upstream-derived data, it would go
red whenever the outside world moved rather than when this project regressed. The converter's own
behaviour stays covered there by fixtures.

### 6. Confirm every no-op resolution is one somebody has accounted for

**This is the step that makes a missing rule visible.** Since [#377](https://github.com/DutchJaFO/Quotinator/issues/377)
an import action whose resolution settles on the values already stored is classified
`ResolvedToExisting` rather than `Modify` — so for the first time these rows are a countable
population instead of being hidden inside the modified count. Each one is either a rule somebody should
declare, or legitimately nothing; the two answers are both present in the bundled corpus today and
neither is currently written down anywhere.

**This step needs its own environment, with the batch auto-purge turned off.** The smoke profile sets
`Quotinator__AutoPurgeBundledImportActions=true`, so a cleanly-applied batch's `Import_Action` rows are
deleted the moment it applies — which is correct behaviour and exactly what makes these rows invisible
to `GET /import/actions` in the shared environment. Found by running this step as first written against
the shared container and getting an empty list.

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-14-noop --port 18653 `
  --env Quotinator__AutoPurgeBundledImportActions=false
$noopBase = "http://localhost:18653/api/v1"
dotnet script scripts/testing/http.csx -- --url "$noopBase/admin/database/reseed" --method POST --header "X-Api-Key: smoketest" --expect 200 | Out-Null

$actions = dotnet script scripts/testing/http.csx -- --url "$noopBase/import/actions?pageSize=0" --expect 200 | ConvertFrom-Json
$noOps   = @($actions.items | Where-Object { $_.actionType -eq 'ResolvedToExisting' })

"resolvedToExisting = $($noOps.Count)"
$noOps | Group-Object entityType | ForEach-Object { "  $($_.Name) = $($_.Count)" }

# Every no-op must fall into one of the two shapes we understand. Anything else is a new one, and the
# question this step exists to ask is whether it wants a rule nobody has written yet.
#
#   1. A rule already covers this entity — an AlreadyApplied ConflictResolutionRule. Still doing work
#      (it is what stops the incoming file re-imposing the wrong value), so permanent and not
#      retirable, per #374.
#   2. The incoming side simply does not carry the field, and the stored value legitimately wins —
#      vilaboim's raw format has no year where NikhilNamal17's does. No rule is wanted or needed.
$declaredRuleIds = @{}
Get-ChildItem data/sources/*conflict-rules.json | ForEach-Object {
  (Get-Content $_.FullName -Raw | ConvertFrom-Json).rules | ForEach-Object { $declaredRuleIds[$_.entityId.ToLower()] = $true }
}

$isUnexplained = {
  param($action)
  if ($declaredRuleIds[$action.entityId.ToLower()]) { return $false }   # shape 1
  $differing = @($action.existingFields.PSObject.Properties | Where-Object {
    "$($action.incomingFields.($_.Name))" -ne "$($_.Value)" })
  # shape 2: every difference is a field the incoming side left empty
  @($differing | Where-Object { "$($action.incomingFields.($_.Name))" -ne "" }).Count -gt 0
}

$unexplained = @($noOps | Where-Object { & $isUnexplained $_ })
"unexplained no-ops = $($unexplained.Count)"
$unexplained | Select-Object -First 10 | ForEach-Object { "  $($_.entityType) $($_.entityId)" }

# Control: a row whose incoming side genuinely disagrees, with no rule declared for it.
$control = [pscustomobject]@{ entityId = 'control'
  existingFields = [pscustomobject]@{ date = '1972' }; incomingFields = [pscustomobject]@{ date = '1980' } }
"control flagged = $(& $isUnexplained $control)"

dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-14-noop
```

**Expected:** `resolvedToExisting` is **non-zero** and names real entity types,
`unexplained no-ops = 0`, and `control flagged = True`.

**The control is what makes the `= 0` mean anything.** The endpoint returns `existingFields` and
`incomingFields` as objects. A predicate that reads a property the response does not carry finds no
differences on any row and reports `0` whatever the data holds — which is how this step was first
written. Only a row the same predicate *does* flag separates a real pass from that.

**Both halves matter and neither substitutes for the other.** The `= 0` assertion alone is satisfied by
a build that produces no actions at all — including one where the classification broke the import
outright — which is why the count being non-zero is asserted first. This is
`docs/testing-policy.md`'s "every test proves the positive result as well as the negative", applied to
a document rather than a unit test.

**It asserts; it does not list** — the same correction step 4C needed on 2026-09-08, and for the same
reason. A row a human is asked to eyeball is a promise rather than a verification, and this document's
own history records nine wrong dates sitting in the database while a listing passed.

**On failure:** a new unexplained no-op after a source refresh means the outside world moved in a way
our rules do not yet cover — read the row's `existingValue`/`incomingValue` and decide whether it wants
a `ConflictResolutionRule`, a `SourceAliasRule`, or nothing at all. Deciding "nothing at all" is a
legitimate outcome; leaving it undecided is not.

## Observed effect

Not yet established as a captured record beyond the empty pending list, the two duplicate queries and
the drift listing — which are the observations this test exists for.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-14
Remove-Item .claude/temp/inspect-181.db, .claude/temp/inspect-181.db-wal, `
            .claude/temp/inspect-181.db-shm -ErrorAction SilentlyContinue
```
