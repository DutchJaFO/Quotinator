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

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" `
  --sql "SELECT Title, Type, COUNT(*) AS c FROM Quotinator_Source WHERE IsDeleted = 0 GROUP BY LOWER(Title), Type HAVING c > 1"
```

**Expected:** the duplicate query returns **no rows**. Any row is a genuine duplicate Source that
slipped through both the rule and alias mechanisms.

The container is stopped for the copy, which this step did not do before: a copy taken while the app
holds the database open can omit rows the WAL has not yet checkpointed, and a *missing* duplicate reads
as a pass.

## Observed effect

Not yet established as a captured record beyond the empty pending list and the empty duplicate query —
both of which are the observation this test exists for.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-14
Remove-Item .claude/temp/inspect-181.db, .claude/temp/inspect-181.db-wal, `
            .claude/temp/inspect-181.db-shm -ErrorAction SilentlyContinue
```
