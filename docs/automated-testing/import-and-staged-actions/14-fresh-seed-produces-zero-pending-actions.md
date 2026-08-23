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
  copy can silently omit committed data.
- The duplicate-Source query groups on `LOWER(Title)`, so a case-only difference counts as a duplicate.
  That is the point — the alias mechanism exists to prevent exactly that.
- **The values `/version` reports are data, not an expectation** — what matters is that seeding produced
  content.

## Steps

Run the **Fresh** profile first.

### 1. Read what the first boot seeded

```bash
curl -s http://localhost:8080/api/v1/version
```

**Expected:** `/version` reports a non-zero quote count and non-zero counts for every bundled entity
type.

### 2. Confirm nothing is left staged awaiting review

```bash
curl -s -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import/actions?status=pending"
```

**Expected:** `200` with an **empty** `items` array. No file is left staged awaiting review.

**On failure:** if anything is left pending, `docker logs` shows
`"<file>" left staged awaiting review — batch "<id>", N action(s) pending a decision`. Inspect via
`GET /import/actions?batchId=<id>` to see which entity or field lacks a rule or alias.

### 3. Cross-check for duplicate Sources

```bash
docker cp qt-env:/data/quotinatordata.db .claude/temp/inspect-181.db
docker cp qt-env:/data/quotinatordata.db-wal .claude/temp/inspect-181.db-wal
docker cp qt-env:/data/quotinatordata.db-shm .claude/temp/inspect-181.db-shm
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" \
  --sql "SELECT Title, Type, COUNT(*) AS c FROM Quotinator_Source WHERE IsDeleted = 0 GROUP BY LOWER(Title), Type HAVING c > 1"
```

**Expected:** the duplicate query returns **no rows**. Any row is a genuine duplicate Source that
slipped through both the rule and alias mechanisms.

## Observed effect

Not yet established as a captured record beyond the empty pending list and the empty duplicate query —
both of which are the observation this test exists for.

## Cleanup

```bash
rm -f .claude/temp/inspect-181.db .claude/temp/inspect-181.db-wal .claude/temp/inspect-181.db-shm
```
