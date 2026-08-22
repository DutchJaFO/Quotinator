# A Character's Source links are per-Source, and survive the many-to-many mechanism change

**Smoke:** no
**Traces to:** #179

## Preconditions

Character no longer has a `SourceId` column; a Character's Source links live in
`Quotinator_CharacterSource` instead. **Matching remains per-Source in meaning** — only the mechanism
changed. Reusing a Character across Sources is #174's job, not this one's.

Both halves must run: a brand-new Character on an existing Source creates exactly one new link, and the
same Character *name* under a *different* Source still creates a separate row.

## Determinism

- **The link count is compared before and after**, and must increase by exactly 1. An absolute count
  depends on the whole seeded dataset.
- **The second half is the one that can silently pass wrongly.** If cross-Source reuse were introduced
  prematurely, the Character count would stay at 1 and only an explicit `= 2` assertion catches it.
- Both Sources used (`Airplane!`, `Monty Python and the Holy Grail`) must already exist from seeding.

## Steps

```bash
cat > .claude/temp/smoke-179.json <<'EOF'
{"quotes": [{"id":"a0000001-0000-4000-8000-000000000001","quote":"A #179 smoke test line.","originalLanguage":"en","source":"Airplane!","date":"1980","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-179.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

Confirm via DbInspector:

```sql
SELECT COUNT(*) FROM Quotinator_CharacterSource;
SELECT c.Name, s.Title FROM Quotinator_Character c
  JOIN Quotinator_CharacterSource cs ON cs.CharacterId = c.Id
  JOIN Quotinator_Source s ON s.Id = cs.SourceId
 WHERE c.Name = 'Striker (Smoke Test)';
```

**Same character name, different Source:**

```bash
cat > .claude/temp/smoke-179b.json <<'EOF'
{"quotes": [{"id":"a0000002-0000-4000-8000-000000000002","quote":"A second #179 smoke test line, same character, different source.","originalLanguage":"en","source":"Monty Python and the Holy Grail","date":"1975","character":"Striker (Smoke Test)","author":null,"type":"movie","genres":[],"translations":{}}]}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-179b.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

```sql
SELECT COUNT(*) FROM Quotinator_Character WHERE Name = 'Striker (Smoke Test)';
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
rm -f .claude/temp/smoke-179.json .claude/temp/smoke-179b.json
```
