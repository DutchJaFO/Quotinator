# A fresh seed resolves every bundled file with nothing left pending

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #181

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

# C: date variants — legitimate, but listed so a wrong date is visible rather than silent.
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" `
  --sql "SELECT Title, Type, GROUP_CONCAT(Date) AS dates FROM Quotinator_Source WHERE IsDeleted = 0 GROUP BY LOWER(Title), Type HAVING COUNT(*) > 1"
```

**Expected:** **A** and **B** return **no rows**. **C** is not an assertion — it is a listing, and every
row in it needs a human to confirm the dates name genuinely distinct works.

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

**Those two were then fixed, and B now returns no rows** (re-measured 2026-09-08 against a rebuilt
image). The cause was not the alias mechanism: a quote naming an existing title in different casing
resolves to the existing row correctly *when the dates agree*, which
`ResolveSourceAsync_QuoteWithDifferentlyCasedTitle_SameDate_ReusesTheExistingSource` proves. It is only
when the date also differs — so a variant is legitimately created — that the raw spelling reached the
database and stood beside the canonical one. `ResolveSourceAsync` now adopts the stored title's own
spelling when creating a variant, asserted by
`ResolveSourceAsync_DateVariantOfDifferentlyCasedTitle_StoresTheCanonicalCasing`.

**C still lists 11 rows and is expected to**, because a wrong date is a data question, not a code one.
`Back to the future` (1958) and `The Silence of the Lambs` (1998) are one film each with one wrong
date, and they now present under a single spelling — visible in C rather than misreported by B.

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

## Observed effect

Not yet established as a captured record beyond the empty pending list, the two duplicate queries and
the drift listing — which are the observations this test exists for.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-14
Remove-Item .claude/temp/inspect-181.db, .claude/temp/inspect-181.db-wal, `
            .claude/temp/inspect-181.db-shm -ErrorAction SilentlyContinue
```
