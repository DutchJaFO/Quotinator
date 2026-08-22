# The rule lookup reads the file's live content, not a cached decision

**Smoke:** no
**Environment:** Fresh
**Traces to:** #181

## Preconditions

**Both container runs need `-e Quotinator__AutoPurgeBundledImportActions=false`** (confirmed live
2026-08-08, #249). Without it, every bundled batch's `Import_Action` rows — including the one whose
`MergedFields` this test queries — are purged immediately after a successful seed, and the row is
already gone by the time you inspect it.

**This test mutates a bundled rule file temporarily.** Both edits must be reverted before committing;
they exist to prove the mechanism, not to change data.

## Determinism

- **The image must be rebuilt after each rule-file edit.** Bundled files ship inside the image, so
  editing on the host without rebuilding tests nothing.
- **The auto-purge flag is not optional.** Omitting it makes the final query return no rows at all,
  which reads exactly like a failed assertion rather than a missing precondition.
- **A second `MergedFields` row may legitimately appear**, for vilaboim's own cross-file duplicate of
  the same quote id, resolved by its own unmodified rule. Each bundled file's rule file governs only
  that file's batch, so the second row is expected and unaffected.

## Steps

**Remove the rule and confirm the conflict returns.** Temporarily delete the Auntie Mame rule entirely
from `nikhilnamal17-conflict-rules.json` (`entityId: 088603c0-…`), then:

```bash
docker build -f docker/Dockerfile -t quotinator:local .
docker run --rm -p 8080:8080 -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=false quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:8080/api/v1/import/actions?status=pending"
```

**Restore the rule, change its `resolution` from `Keep` to `Replace`, rebuild and reseed**, then check
the audit trail:

```bash
docker cp <container>:/app/data/quotinatordata.db .claude/temp/inspect-181.db
docker cp <container>:/app/data/quotinatordata.db-wal .claude/temp/inspect-181.db-wal
docker cp <container>:/app/data/quotinatordata.db-shm .claude/temp/inspect-181.db-shm
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" \
  --sql "SELECT MergedFields FROM Import_Action WHERE EntityId='088603c0-b35a-1b48-977d-ca08489a0cbb' AND ActionType='Modify'"
```

## Expected output

- With the rule removed, that quote's conflict stages `Pending` again, with `ambiguousFields: ["date"]`.
  That proves the mechanism consults the file's content on every seed rather than a cached decision from
  an earlier run.
- With `resolution` changed to `Replace`, the row for the batch matching NikhilNamal17's own rule file
  shows `"date":"2005"` — the incoming value, Replace won — changed from `"date":"1958"` under the
  original `Keep`.

**`GET /quotes/{id}` will not show the change**, and that is correct rather than a failure. `date` is
Source-derived, read via JOIN from `Quotinator_Source.Date`, and the Source was already fixed at the
film's correct year by whichever occurrence was seen first. **A per-quote rule only ever affects that
Quote's own `MergedFields` audit trail, never a Source-owned field's real stored value** — the same
limitation #181's own Step 10 addendum documents.

## Observed effect

Live-verified 2026-07-25. The `MergedFields` value is the load-bearing observation: the endpoint
response is unchanged either way, so only the audit trail distinguishes a rule that applied from one
that did not.

## Cleanup

```bash
rm -f .claude/temp/inspect-181.db .claude/temp/inspect-181.db-wal .claude/temp/inspect-181.db-shm
```

**Revert both rule-file edits.** Confirm `nikhilnamal17-conflict-rules.json` matches `git status` clean
before committing anything.
