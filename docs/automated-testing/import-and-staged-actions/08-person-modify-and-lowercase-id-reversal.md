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

Then re-import the same id with a changed `dateOfBirth` under `review`, decide with
`{"personDateOfBirth":{"choice":"replace"},"markCompletenessAs":"Complete"}`, apply, and re-import once
more with another changed `dateOfBirth` under `review`.

**No command — the two changed-`dateOfBirth` fixtures are not defined, and neither is how the action
`id` and `batchId` for the `decide`/`apply` calls are obtained.**

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

Then re-import the exact same fixture one more time:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-173-addonly.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' -w "\n%{http_code}\n" "http://localhost:8080/api/v1/import"
```

## Expected output

- The first import returns `200` with the Person added.
- The `review` re-import stages a `Pending` `Modify` action with `ambiguousFields: ["dateOfBirth"]`.
- After deciding and applying: the corrected `DateOfBirth` and `CompletenessStatus: Complete`.
- The third import stages **`Blocked`, not `Pending`**, and the on-disk value is unchanged.
- Both reversal calls return `200`, and
  `SELECT Id, IsDeleted FROM Quotinator_Person WHERE Id = 'f0000007-0000-4000-8000-000000000007'` shows `IsDeleted` genuinely
  flipped to `1`.
- The final re-import stages as a **fresh `Add`, not `Modify`** — `Modify` would mean the reversal
  silently no-op'd and the row was never truly gone — and `IsDeleted` is back to `0` afterwards.

## Observed effect

Not yet established as a captured record. The `IsDeleted` flip is the load-bearing observation: the
endpoint reported success in the failing case too, so the HTTP result alone never distinguished them.

## Cleanup

```bash
rm -f .claude/temp/smoke-173.json .claude/temp/smoke-173-addonly.json
```
