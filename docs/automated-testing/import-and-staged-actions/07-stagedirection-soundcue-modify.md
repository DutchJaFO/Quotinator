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

## Steps

**First fixture — Modify, decide, then confirm a Complete row blocks:**

```bash
cat > .claude/temp/smoke-171-172.json <<'EOF'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000001","quote":"Smoke test filler quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000002","text":"A shot rings out.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000003","text":"Distant thunder.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-171-172.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
```

Confirm via DbInspector:
`SELECT Id, Text, CompletenessStatus FROM Quotinator_StageDirection WHERE Id = 'f0000002-0000-4000-8000-000000000002'`

Then re-import the same ids with a changed `text` under
`{"duplicateResolution":{"default":"review"}}`, decide each with
`{"stageDirectionText":{"choice":"replace"},"markCompletenessAs":"Complete"}` /
`{"soundCueText":{"choice":"replace"},"markCompletenessAs":"Complete"}`, and
`POST /import/actions/apply?batchId=…`.

Then re-import the same ids **again** with another changed `text` under `review`.

**No command — the changed-text fixtures are not defined, and neither is how the action `id`s and
`batchId` for the `decide`/`apply` calls are obtained.**

**Second fixture — a fresh pair, for the reversal half:**

```bash
cat > .claude/temp/smoke-171-172-addonly.json <<'EOF'
{
  "quotes": [{"id":"f0000001-0000-4000-8000-000000000009","quote":"A #171/#172 add-only smoke test quote.","originalLanguage":"en","source":"Smoke Test Film","date":"2026","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],
  "stageDirections": [{"id":"f0000002-0000-4000-8000-000000000009","text":"Original text before correction.","imageUrl":null,"translations":{}}],
  "soundCues": [{"id":"f0000003-0000-4000-8000-000000000009","text":"Original sound before correction.","soundFileUrl":null,"imageUrl":null,"translations":{}}]
}
EOF
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@.claude/temp/smoke-171-172-addonly.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' "http://localhost:8080/api/v1/import"
```

Then single-shot re-import a changed `text` for both ids under `newest-wins`, confirm the write via
DbInspector, and `POST /import/actions/reverse?batchId=…` — `preview=true` first, then for real.

**No command — the changed-text fixture for this half is not defined either, and the `batchId` the
reversal uses is never captured from the import above.**

## Expected output

- The first import returns `200` with both rows added.
- The `review` re-import stages a `Pending` `Modify` action for each, with `ambiguousFields: ["text"]`.
- After deciding and applying: the corrected text and `CompletenessStatus: Complete`.
- The third import stages **`Blocked`, not `Pending`**, and the on-disk text is unchanged — a
  `Complete` row can no longer be silently overwritten.
- The second fixture adds both rows fresh, still `NeedsReview`.
- Its `newest-wins` re-import applies immediately with nothing pending.
- Both reversal calls return `200`, and DbInspector confirms the pre-correction text is restored.

## Observed effect

Not yet established as a captured record beyond the DbInspector reads asserted above.

## Cleanup

```bash
rm -f .claude/temp/smoke-171-172.json .claude/temp/smoke-171-172-addonly.json
```
