# StageDirection and SoundCue can be Modified, and a Complete row blocks overwrites

**Smoke:** no
**Environment:** Fresh
**Traces to:** #171, #172

## Preconditions

Both entities were Add-only before these issues. This proves a `Complete` row blocks a silent
overwrite, and that a correctable row can be Modified, decided and reversed end to end.

**A fixture needs at least one quote** — `POST /import` rejects a file with none, even when the quote
is irrelevant to what is being tested.

**Two separate fixtures are required**, and this is the part that is easy to get wrong — see
Determinism.

## Determinism

**`CompletenessGuard.ShouldBlock` is evaluated against the value a policy would actually *write*, not
the raw incoming value.** So once a row is `Complete`, **every policy except `skip` blocks a genuine
field change — `newest-wins` included.**

That makes the reversal half impossible to run against the rows used in the first half: re-running it
there stages `Blocked` again rather than applying cleanly. It needs a **second, brand-new pair** that
was never marked `Complete`. This was a real correction to this test (2026-08-08), not a hypothetical.

Also load-bearing: **a `Modify`-only batch's reversal never touches `IsDeleted`.** Only reversing the
row's own `Add` does, which is why the second fixture must be a fresh add.

The direct-apply path (`newest-wins`, nothing pending) sets `Import_Batch.Status` to `Applied`; the
two-phase decide→apply path used in the first half does **not** — a known pre-existing gap, see
#171/#172's plan docs.

**Each step names the fixture it uses**, because five imports run here and they differ only by which
file they upload.

**Reading the `smoke-171-172-v3.json` tally is the assertion**; the import returns a success code either
way, so the status code alone does not distinguish blocked from staged.

## Steps

### 1. Create this test's own environment

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-import-07 --port 18607
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Import `smoke-171-172.json` — the initial add

```bash
cat > .claude/temp/smoke-171-172.json <<'EOF'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
EOF
curl -s -X POST -H "X-Api-Key: smoketest" -F "file=@.claude/temp/smoke-171-172.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:18607/api/v1/import"
```

Confirm via DbInspector:
`SELECT Id, Text, CompletenessStatus FROM Quotinator_StageDirection WHERE Id = 'f0000002-0000-4000-8000-000000000002'`

**Expected:** `smoke-171-172.json` returns `200` with both rows added — the DbInspector read shows the
StageDirection row present.

**On failure:** without these rows there is nothing for the re-imports below to Modify, and every later
step would be staging fresh adds instead. Stop.

### 3. Re-import `smoke-171-172-v2.json` — the same ids with a changed `text`, under `review`

```bash
cat > .claude/temp/smoke-171-172-v2.json <<'EOF'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out, twice.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder, rolling.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
EOF
batchId=$(curl -s -X POST -H "X-Api-Key: smoketest" -F "file=@.claude/temp/smoke-171-172-v2.json" \
            -F 'settings={"duplicateResolution":{"default":"review"}}' "http://localhost:18607/api/v1/import" \
          | grep -o '"batchId":"[^"]*"' | cut -d'"' -f4)
echo "batchId=$batchId"
```

Capture the two action ids this batch staged:

```bash
stageId=$(curl -s "http://localhost:18607/api/v1/import/actions?status=pending&batchId=$batchId&pageSize=0" \
          | grep -o '"id":"[0-9a-f-]\{36\}","batchId":"[^"]*","actionType":"[A-Za-z]*","entityType":"StageDirection"' \
          | head -1 | cut -d'"' -f4)
soundId=$(curl -s "http://localhost:18607/api/v1/import/actions?status=pending&batchId=$batchId&pageSize=0" \
          | grep -o '"id":"[0-9a-f-]\{36\}","batchId":"[^"]*","actionType":"[A-Za-z]*","entityType":"SoundCue"' \
          | head -1 | cut -d'"' -f4)
echo "stageId=$stageId soundId=$soundId"
```

**Expected:** `smoke-171-172-v2.json`, under `review`, stages a `Pending` `Modify` action for each, with
`ambiguousFields: ["text"]`.

**On failure:** an empty pending listing means the `review` policy did not take effect and nothing was
staged, so the decide and apply below would be operating on an empty batch. Stop.

### 4. Decide every action in the batch, then apply it

The fixture's quote stages an action of its own, and `apply` is all-or-nothing — so the two under test
are decided with their real choices, and anything else in the batch is decided too:

```bash
curl -s -o /dev/null -X POST -H "X-Api-Key: smoketest" -H "Content-Type: application/json" \
  -d '{"stageDirectionText":{"choice":"replace"},"markCompletenessAs":"Complete"}' \
  "http://localhost:18607/api/v1/import/actions/$stageId/decide"
curl -s -o /dev/null -X POST -H "X-Api-Key: smoketest" -H "Content-Type: application/json" \
  -d '{"soundCueText":{"choice":"replace"},"markCompletenessAs":"Complete"}' \
  "http://localhost:18607/api/v1/import/actions/$soundId/decide"

for id in $(curl -s "http://localhost:18607/api/v1/import/actions?status=Pending&batchId=$batchId&pageSize=0" \
            | grep -o '"id":"[0-9a-f-]\{36\}"' | cut -d'"' -f4); do
  curl -s -o /dev/null -X POST -H "X-Api-Key: smoketest" -H "Content-Type: application/json" \
    -d '{"quoteText":{"choice":"keep"}}' \
    "http://localhost:18607/api/v1/import/actions/$id/decide"
done

curl -s "http://localhost:18607/api/v1/import/actions?status=Pending&batchId=$batchId&pageSize=0" \
  | grep -o '"totalCount":[0-9]*'
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18607/api/v1/import/actions/apply?batchId=$batchId"
```

**Expected:** `"totalCount":0` before the apply, then `200`. Both rows then carry the corrected text
and `CompletenessStatus: Complete` — read back by step 8.

**The loop is not redundant with the two explicit decides.** A fixture needs at least one quote for
`POST /import` to accept it, and that quote stages its own `Modify` action; leaving it undecided makes
`apply` return `422` and the rest of the document unreachable. Measured during #339's full run, where
the batch staged three actions and this step named two.

### 5. Re-import `smoke-171-172-v3.json` — a third `text`, still under `review`

The `Complete` rows must block it:

```bash
cat > .claude/temp/smoke-171-172-v3.json <<'EOF'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out, three times.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder, fading.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
EOF
thirdBatchId=$(curl -s -X POST -H "X-Api-Key: smoketest" -F "file=@.claude/temp/smoke-171-172-v3.json" \
                 -F 'settings={"duplicateResolution":{"default":"review"}}' "http://localhost:18607/api/v1/import" \
               | grep -o '"batchId":"[^"]*"' | cut -d'"' -f4)
echo "thirdBatchId=$thirdBatchId"
```

Read what that third batch staged:

```bash
curl -s "http://localhost:18607/api/v1/import/actions?batchId=$thirdBatchId&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
```

**Expected:** `smoke-171-172-v3.json`'s status tally reads **`Blocked`, not `Pending`** — a `Complete`
row can no longer be silently overwritten.

### 6. Import `smoke-171-172-addonly.json` — a fresh pair, for the reversal half

```bash
cat > .claude/temp/smoke-171-172-addonly.json <<'EOF'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000009","quote":"A #171/#172 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000009","text":"Original text before correction.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000009","text":"Original sound before correction.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
EOF
curl -s -X POST -H "X-Api-Key: smoketest" -F "file=@.claude/temp/smoke-171-172-addonly.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:18607/api/v1/import"
```

**Expected:** `smoke-171-172-addonly.json` adds a fresh pair, still `NeedsReview` — never `Complete`,
which is what makes the reversal half runnable at all.

### 7. Single-shot re-import `smoke-171-172-addonly-v2.json` under `newest-wins`, then reverse it

```bash
cat > .claude/temp/smoke-171-172-addonly-v2.json <<'EOF'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000009","quote":"A #171/#172 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000009","text":"Corrected text after correction.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000009","text":"Corrected sound after correction.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
EOF
correctionBatchId=$(curl -s -X POST -H "X-Api-Key: smoketest" -F "file=@.claude/temp/smoke-171-172-addonly-v2.json" \
                      -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:18607/api/v1/import" \
                    | grep -o '"batchId":"[^"]*"' | cut -d'"' -f4)
echo "correctionBatchId=$correctionBatchId"
```

Reverse it, preview first:

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18607/api/v1/import/actions/reverse?batchId=$correctionBatchId&preview=true"
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18607/api/v1/import/actions/reverse?batchId=$correctionBatchId"
```

**Expected:** `smoke-171-172-addonly-v2.json`, under `newest-wins`, applies immediately with nothing
pending. Both reversal calls against its batch return `200`.

### 8. Confirm the pre-correction text is back

```bash
docker stop -t 15 qt-import-07
MSYS_NO_PATHCONV=1 docker cp qt-import-07:/data/quotinatordata.db .claude/temp/smoke-171-172.db
MSYS_NO_PATHCONV=1 docker cp qt-import-07:/data/quotinatordata.db-wal .claude/temp/smoke-171-172.db-wal || true
MSYS_NO_PATHCONV=1 docker cp qt-import-07:/data/quotinatordata.db-shm .claude/temp/smoke-171-172.db-shm || true
docker start qt-import-07
until curl -sf http://localhost:18607/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke-171-172.db \
  --sql "SELECT Id, Text, CompletenessStatus FROM Quotinator_StageDirection WHERE Id IN ('f0000002-0000-4000-8000-000000000002','f0000002-0000-4000-8000-000000000009')"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke-171-172.db \
  --sql "SELECT Id, Text, CompletenessStatus FROM Quotinator_SoundCue WHERE Id IN ('f0000003-0000-4000-8000-000000000003','f0000003-0000-4000-8000-000000000009')"
```

**Expected:** the closing DbInspector reads show the **`…000002`/`…000003` pair** still carrying `v2`'s
corrected text with `CompletenessStatus: Complete` — `v3`'s text never landed — and the **`…000009`
pair** back at `Original text before correction.` / `Original sound before correction.`, the reversal
undone.

## Observed effect

Not yet established as a captured record beyond the DbInspector reads asserted above.

## Cleanup

```bash
rm -f .claude/temp/smoke-171-172*.json .claude/temp/smoke-171-172.db*
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-07
```
