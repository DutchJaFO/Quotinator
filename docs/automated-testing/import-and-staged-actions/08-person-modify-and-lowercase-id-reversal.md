# A Person can be Modified, blocks overwrites when Complete, and reverses by a mixed-case id

**Smoke:** no
**Environment:** Fresh
**Traces to:** #173

## Preconditions

Person was Add-only before this issue and never had a write path for `dateOfBirth`/`dateOfDeath`. This
proves a `Complete` Person blocks a silent overwrite, that a correctable Person can be Modified and
decided end to end, and exercises the lowercase-explicit-id reversal fix found live during #173's own
T2 pass.

**A fixture needs at least one quote** — `POST /import` rejects a file with none.

**Two separate Person fixtures are required.** See Determinism; using one is the mistake this test
already made once.

## Determinism

**`CompletenessGuard.ShouldBlock` (`ImportActionPlanner.cs`, #168) is evaluated against the value a
policy would actually *write*, not the raw incoming value.** Once a row is `Complete`, **every policy
except `skip` blocks a genuine field change, `newest-wins` included.**

Re-running the reversal sequence against the `Complete` Person from the first half stages `Blocked`
again, never a clean apply. This was corrected in this test on 2026-07-31.

**Reversing a `Modify`-only batch never touches `IsDeleted` at all** — only reversing the row's own
`Add` does. So the lowercase-id reversal fix can only be proven against a fresh row that was never
marked `Complete`.

**The second fixture's id is deliberately uppercase (`F0000007-…`), and that is the reproduction
shape.** A `Guid`-typed repository call used to silently force-uppercase before comparing, matching
zero rows against the lowercase-canonicalized stored id — so the row would stay visibly present with
`IsDeleted = 0` while the endpoint reported success. Lowercasing it in the fixture removes the entire
point of the test.

The first fixture's id is deliberately lowercase, as a file-authored explicit id always is.

## Steps

**First fixture — Modify, decide, then confirm Complete blocks:**

```bash
cat > .claude/temp/smoke-173.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1950-01-01","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
```

Confirm via DbInspector:
`SELECT Id, Name, DateOfBirth, DateOfDeath, CompletenessStatus FROM Quotinator_Person WHERE Id = 'f0000005-0000-4000-8000-000000000005'`

**Re-import the same id with a changed `dateOfBirth`, under `review`:**

```bash
cat > .claude/temp/smoke-173-v2.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1951-02-02","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-v2.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

Copy that `batchId`, list its pending action, copy the action `id`, then decide and apply:

```bash
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&batchId=<batchId>&pageSize=0"
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"personDateOfBirth":{"choice":"replace"},"markCompletenessAs":"Complete"}' \
  "http://localhost:8080/api/v1/import/actions/<action id>/decide"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/apply?batchId=<batchId>"
```

**Now a third import with yet another `dateOfBirth` — the `Complete` row must block it:**

```bash
cat > .claude/temp/smoke-173-v3.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1952-03-03","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-v3.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

Copy this third `batchId`, then read what it staged:

```bash
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<third batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

**Second fixture — a fresh Person with an uppercase id:**

```bash
cat > .claude/temp/smoke-173-addonly.json <<'EOF'
{
  "quotes": [{"id":"f0000005-0000-4000-8000-000000000008","quote":"A #173 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person AddOnly","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"F0000007-0000-4000-8000-000000000007","name":"Smoke Test Person AddOnly","dateOfBirth":"1985-05-05","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-addonly.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

Copy the returned `batchId`, then:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
```

Confirm via DbInspector:
`SELECT Id, IsDeleted FROM Quotinator_Person WHERE Id = 'f0000007-0000-4000-8000-000000000007'`

Then re-import the exact same fixture one more time, and **read what it staged** — this is the single
distinction the test exists to draw, and nothing else in the run observes it:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-addonly.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

Copy that import's own `batchId`, then:

```bash
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<final batchId>&pageSize=0" \
  | grep -o '"actionType":"[A-Za-z]*"' | sort | uniq -c
```

## Expected output

Each bullet names its fixture — four imports run here and they differ only by which file they upload.

- **`smoke-173.json`** returns `200` with the Person added, `DateOfBirth` `1950-01-01`.
- **`smoke-173-v2.json`**, under `review`, stages a `Pending` `Modify` with
  `ambiguousFields: ["dateOfBirth"]`. After deciding and applying, `DateOfBirth` reads `1951-02-02` and
  `CompletenessStatus` is `Complete`.
- **`smoke-173-v3.json`**'s status tally reads **`Blocked`, not `Pending`**, and `DateOfBirth` stays
  `1951-02-02` — `1952-03-03` never lands. Reading the tally is the assertion; the import returns a
  success code either way.
- Both reversal calls return `200`, and
  `SELECT Id, IsDeleted FROM Quotinator_Person WHERE Id = 'f0000007-0000-4000-8000-000000000007'` shows `IsDeleted` genuinely
  flipped to `1`.
- The final re-import's action tally shows **`Add`, not `Modify`**, for the Person. `Modify` would mean
  the reversal silently no-op'd and the row was never truly gone — the endpoint reported success in that
  failing case too, so this tally is the only thing that separates them.
- `IsDeleted` is back to `0` afterwards.

## Observed effect

Not yet established as a captured record. The `IsDeleted` flip is the load-bearing observation: the
endpoint reported success in the failing case too, so the HTTP result alone never distinguished them.

## Cleanup

```bash
rm -f .claude/temp/smoke-173*.json
```

Two Person rows (one `Complete`), the filler quotes, the `Smoke Test Film` Source and the staged
batches remain — restore the Fresh profile before the next test.
