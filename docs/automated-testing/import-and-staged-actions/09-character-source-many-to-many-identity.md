# A Character's Source links are per-Source, and survive the many-to-many mechanism change

**Smoke:** no
**Environment:** Fresh
**Traces to:** #179

## Preconditions

Character no longer has a `SourceId` column; a Character's Source links live in
`Quotinator_CharacterSource` instead. **Matching remains per-Source in meaning** — only the mechanism
changed. Reusing a Character across Sources is #174's job, not this one's.

Both halves must run: a brand-new Character on an existing Source creates exactly one new link, and the
same Character *name* under a *different* Source still creates a separate row.

## Determinism

- **The link count is compared before and after**, and must increase by exactly 1. An absolute count
  depends on the whole seeded dataset, so only the delta is asserted — which means the *before* reading
  is load-bearing, not preamble. Without it there is no delta to evaluate and the assertion cannot fail.
- **Every count filters `IsDeleted = 0`.** Soft-deleted links are invisible to the endpoints but still
  present in the table, and would inflate both readings.
- **Both readings depend on starting from a database this test has not run against before.** The second
  half asserts the Character count is exactly `2`; against leftovers it would find them already present,
  the delta would read `0`, and the failure would look like a defect in the mechanism rather than in the
  setup. Creating the container and volume fresh in step 1 is what makes that impossible — it is not a
  caution the reader has to remember.
- **The second half is the one that can silently pass wrongly.** If cross-Source reuse were introduced
  prematurely, the Character count would stay at 1 and only an explicit `= 2` assertion catches it.
- Both Sources used (`Airplane!`, `Monty Python and the Holy Grail`) must already exist from seeding.
- **Each database copy is taken from a stopped container**, so a WAL that has not been checkpointed
  cannot leave the copy missing exactly the rows just written. The `-wal`/`-shm` copies are allowed to
  fail: a cleanly stopped database has neither file.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-09 --port 18609
$base = "http://localhost:18609/api/v1"
$temp = "$PWD\.claude\temp"

function Copy-Database($suffix) {
  docker stop -t 15 qt-import-09 | Out-Null
  docker cp "qt-import-09:/data/quotinatordata.db" "$temp\smoke179-$suffix.db"
  docker cp "qt-import-09:/data/quotinatordata.db-wal" "$temp\smoke179-$suffix.db-wal" 2>$null
  docker cp "qt-import-09:/data/quotinatordata.db-shm" "$temp\smoke179-$suffix.db-shm" 2>$null
  docker start qt-import-09 | Out-Null
  dotnet script scripts/testing/http.csx -- --url "$base/health" --wait-for 200 --status | Out-Null
}
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Record the baseline link count

The assertion below is a delta, and a delta cannot be evaluated from its after-value alone:

```powershell
Copy-Database before
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$temp\smoke179-before.db" `
  --sql "SELECT COUNT(*) AS LinksBefore FROM Quotinator_CharacterSource WHERE IsDeleted = 0"
```

**Expected:** a `LinksBefore` figure — the value the delta below is measured against.

**On failure:** without this reading there is no delta to evaluate and step 4's assertion cannot fail.
Stop.

### 3. Import `smoke-179.json` — a new Character on the existing `Airplane!` Source

```powershell
$first = @'
{"quotes": [{"id":"a0000001-0000-4000-8000-000000000001","quote":"A #179 smoke test line.","originalLanguage":"en","source":"Airplane!","date":"1980","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
'@
[IO.File]::WriteAllText("$temp\smoke-179.json", $first, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\smoke-179.json" --duplicate-resolution newest-wins --expect 200 --status
```

**Expected:** `200`.

### 4. Re-read the database and compare against the baseline

```powershell
Copy-Database after
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$temp\smoke179-after.db" `
  --sql "SELECT COUNT(*) AS LinksAfter FROM Quotinator_CharacterSource WHERE IsDeleted = 0"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$temp\smoke179-after.db" `
  --sql "SELECT c.Name, s.Title FROM Quotinator_Character c JOIN Quotinator_CharacterSource cs ON cs.CharacterId = c.Id AND cs.IsDeleted = 0 JOIN Quotinator_Source s ON s.Id = cs.SourceId AND s.IsDeleted = 0 WHERE c.Name = 'Striker (Smoke Test)' AND c.IsDeleted = 0"
```

**Expected:** `LinksAfter` is exactly one greater than `LinksBefore`, and the join shows one row linking
to `Airplane!`.

### 5. Import `smoke-179b.json` — the same character name, different Source

```powershell
$second = @'
{"quotes": [{"id":"a0000002-0000-4000-8000-000000000002","quote":"A second #179 smoke test line, same character, different source.","originalLanguage":"en","source":"Monty Python and the Holy Grail","date":"1975","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
'@
[IO.File]::WriteAllText("$temp\smoke-179b.json", $second, [Text.UTF8Encoding]::new($false))

dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
  --file "$temp\smoke-179b.json" --duplicate-resolution newest-wins --expect 200 --status
```

**Expected:** `200`.

### 6. Count the Character rows carrying that name

```powershell
Copy-Database second
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$temp\smoke179-second.db" `
  --sql "SELECT COUNT(*) AS Characters FROM Quotinator_Character WHERE Name = 'Striker (Smoke Test)' AND IsDeleted = 0"
```

**Expected:** `Characters` reads **2** — a second, separate Character row, each linked to its own
Source. Per-Source matching genuinely survived the mechanism change rather than being silently reused
across Sources.

## Observed effect

Not yet established as a captured record beyond the database reads.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-09
Get-ChildItem $temp -Filter 'smoke179-*.db*' | Remove-Item -ErrorAction SilentlyContinue
Remove-Item "$temp\smoke-179.json", "$temp\smoke-179b.json" -ErrorAction SilentlyContinue
```
