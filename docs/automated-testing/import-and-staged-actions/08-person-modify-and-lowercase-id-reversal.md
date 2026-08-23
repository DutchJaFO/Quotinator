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

**Each step names its fixture** — four imports run here and they differ only by which file they upload.

**Reading the `smoke-173-v3.json` tally is the assertion**; the import returns a success code either
way.

## Steps

### 1. Create this test's own environment

```bash
docker rm -f qt-import-08 2>/dev/null; docker volume rm qt-import-08-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-import-08 -p 18608:8080 -v qt-import-08-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18608/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Import `smoke-173.json` — the initial add

```bash
cat > .claude/temp/smoke-173.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1950-01-01","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:18608/api/v1/import"
```

Confirm via DbInspector:
`SELECT Id, Name, DateOfBirth, DateOfDeath, CompletenessStatus FROM Quotinator_Person WHERE Id = 'f0000005-0000-4000-8000-000000000005'`

**Expected:** `smoke-173.json` returns `200` with the Person added, `DateOfBirth` `1950-01-01`.

**On failure:** without this row there is nothing for the re-imports below to Modify, and every later
step would be staging a fresh add instead. Stop.

### 3. Re-import `smoke-173-v2.json` — the same id with a changed `dateOfBirth`, under `review`

```bash
cat > .claude/temp/smoke-173-v2.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1951-02-02","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-v2.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' -w "\n%{http_code}\n" "http://localhost:18608/api/v1/import"
```

Copy that `batchId`, list its pending action, and copy the action `id`:

```bash
curl -s "http://localhost:18608/api/v1/import/actions?status=pending&batchId=<batchId>&pageSize=0"
```

**Expected:** `smoke-173-v2.json`, under `review`, stages a `Pending` `Modify` with
`ambiguousFields: ["dateOfBirth"]`.

**On failure:** an empty pending listing means the `review` policy did not take effect and nothing was
staged, so the decide and apply below would be operating on an empty batch. Stop.

### 4. Decide the action and apply the batch

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -H "Content-Type: application/json" \
  -d '{"personDateOfBirth":{"choice":"replace"},"markCompletenessAs":"Complete"}' \
  "http://localhost:18608/api/v1/import/actions/<action id>/decide"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18608/api/v1/import/actions/apply?batchId=<batchId>"
```

**Expected:** after deciding and applying, `DateOfBirth` reads `1951-02-02` and `CompletenessStatus` is
`Complete`.

### 5. Import `smoke-173-v3.json` — a third `dateOfBirth`, and read what it staged

The `Complete` row must block it:

```bash
cat > .claude/temp/smoke-173-v3.json <<'EOF'
{
  "quotes": [{"id":"f0000004-0000-4000-8000-000000000004","quote":"A #173 smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"f0000005-0000-4000-8000-000000000005","name":"Smoke Test Person","dateOfBirth":"1952-03-03","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-v3.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' -w "\n%{http_code}\n" "http://localhost:18608/api/v1/import"
```

Copy this third `batchId`, then read what it staged:

```bash
curl -s "http://localhost:18608/api/v1/import/actions?batchId=<third batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

**Expected:** `smoke-173-v3.json`'s status tally reads **`Blocked`, not `Pending`**, and `DateOfBirth`
stays `1951-02-02` — `1952-03-03` never lands.

### 6. Import `smoke-173-addonly.json` — a fresh Person with an uppercase id

```bash
cat > .claude/temp/smoke-173-addonly.json <<'EOF'
{
  "quotes": [{"id":"f0000005-0000-4000-8000-000000000008","quote":"A #173 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":"Smoke Test Person AddOnly","type":"movie","genres":[],"translations":{}}],
  "people": [{"id":"F0000007-0000-4000-8000-000000000007","name":"Smoke Test Person AddOnly","dateOfBirth":"1985-05-05","dateOfDeath":null}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-addonly.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:18608/api/v1/import"
```

**Expected:** the response carries a `batchId` — the reversal below is scoped to it.

### 7. Reverse the add-only batch, preview first

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18608/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18608/api/v1/import/actions/reverse?batchId=<batchId>"
```

**Expected:** both reversal calls return `200`.

### 8. Confirm the soft-delete flag flipped

Confirm via DbInspector:
`SELECT Id, IsDeleted FROM Quotinator_Person WHERE Id = 'f0000007-0000-4000-8000-000000000007'`

**Expected:** `IsDeleted` genuinely flipped to `1`.

### 9. Re-import `smoke-173-addonly.json` unchanged, and read what it staged

This is the single distinction the test exists to draw, and nothing else in the run observes it:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-addonly.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:18608/api/v1/import"
```

Copy that import's own `batchId`, then:

```bash
curl -s "http://localhost:18608/api/v1/import/actions?batchId=<final batchId>&pageSize=0" \
  | grep -o '"actionType":"[A-Za-z]*"' | sort | uniq -c
```

**Expected:** the final re-import's action tally shows **`Add`, not `Modify`**, for the Person, and
`IsDeleted` is back to `0` afterwards.

**On failure:** `Modify` would mean the reversal silently no-op'd and the row was never truly gone — the
endpoint reported success in that failing case too, so this tally is the only thing that separates them.

## Observed effect

Not yet established as a captured record. The `IsDeleted` flip is the load-bearing observation: the
endpoint reported success in the failing case too, so the HTTP result alone never distinguished them.

## Cleanup

```bash
rm -f .claude/temp/smoke-173*.json
docker rm -f qt-import-08
docker volume rm qt-import-08-data
```
