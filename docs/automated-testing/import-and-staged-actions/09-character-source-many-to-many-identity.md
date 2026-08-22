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
- **This test is not re-runnable against its own leftovers.** The second half asserts the Character
  count is `2`; a rerun against a database where it already ran finds them present, the delta becomes
  `0`, and the failure looks like a defect in the mechanism rather than in the setup. Restore the Fresh
  profile first — see Cleanup.
- **The second half is the one that can silently pass wrongly.** If cross-Source reuse were introduced
  prematurely, the Character count would stay at 1 and only an explicit `= 2` assertion catches it.
- Both Sources used (`Airplane!`, `Monty Python and the Holy Grail`) must already exist from seeding.

## Steps

**Record the baseline link count first.** The assertion below is a delta, and a delta cannot be
evaluated from its after-value alone:

```bash
docker stop -t 15 qt-env
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db .claude/temp/smoke179.db
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db-wal .claude/temp/smoke179.db-wal
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db-shm .claude/temp/smoke179.db-shm
docker start qt-env
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke179.db \
  --sql "SELECT COUNT(*) AS LinksBefore FROM Quotinator_CharacterSource WHERE IsDeleted = 0"
```

```bash
cat > .claude/temp/smoke-179.json <<'EOF'
{"quotes": [{"id":"a0000001-0000-4000-8000-000000000001","quote":"A #179 smoke test line.","originalLanguage":"en","source":"Airplane!","date":"1980","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-179.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

Re-read the database and compare against the baseline:

```bash
docker stop -t 15 qt-env
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db .claude/temp/smoke179-after.db
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db-wal .claude/temp/smoke179-after.db-wal
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db-shm .claude/temp/smoke179-after.db-shm
docker start qt-env
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke179-after.db \
  --sql "SELECT COUNT(*) AS LinksAfter FROM Quotinator_CharacterSource WHERE IsDeleted = 0"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke179-after.db \
  --sql "SELECT c.Name, s.Title FROM Quotinator_Character c JOIN Quotinator_CharacterSource cs ON cs.CharacterId = c.Id AND cs.IsDeleted = 0 JOIN Quotinator_Source s ON s.Id = cs.SourceId AND s.IsDeleted = 0 WHERE c.Name = 'Striker (Smoke Test)' AND c.IsDeleted = 0"
```

**Same character name, different Source:**

```bash
cat > .claude/temp/smoke-179b.json <<'EOF'
{"quotes": [{"id":"a0000002-0000-4000-8000-000000000002","quote":"A second #179 smoke test line, same character, different source.","originalLanguage":"en","source":"Monty Python and the Holy Grail","date":"1975","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-179b.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

```bash
docker stop -t 15 qt-env
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db .claude/temp/smoke179-second.db
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db-wal .claude/temp/smoke179-second.db-wal
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db-shm .claude/temp/smoke179-second.db-shm
docker start qt-env
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke179-second.db \
  --sql "SELECT COUNT(*) AS Characters FROM Quotinator_Character WHERE Name = 'Striker (Smoke Test)' AND IsDeleted = 0"
```

## Expected output

- Both imports return `200`.
- `Quotinator_CharacterSource` increased by exactly 1 after the first, and the join shows one row
  linking to `Airplane!`.
- After the second, `Quotinator_Character WHERE Name = 'Striker (Smoke Test)'` counts **2** — a second,
  separate Character row, each linked to its own Source. Per-Source matching genuinely survived the
  mechanism change rather than being silently reused across Sources.

## Observed effect

Not yet established as a captured record beyond the DbInspector reads.

## Cleanup

```bash
rm -f .claude/temp/smoke-179.json .claude/temp/smoke-179b.json \
      .claude/temp/smoke179.db* .claude/temp/smoke179-after.db* .claude/temp/smoke179-second.db*
```

**Restore the Fresh profile.** Two Characters, two links and two quotes remain, and this test asserts a
Character count of exactly `2` — running it again against its own leftovers reports a failure that is
setup, not mechanism.
