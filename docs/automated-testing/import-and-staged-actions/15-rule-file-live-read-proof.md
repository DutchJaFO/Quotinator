# The rule lookup reads the file's live content, not a cached decision

**Smoke:** no
**Environment:** Fresh
**Traces to:** #181

## Preconditions

**This test runs its own two containers rather than the profile's**, because it edits a bundled rule
file and rebuilds the image between them — a rebuilt image is exactly what the profile's prebuilt
container cannot supply. Everything else about each run is Fresh: same admin key, same first-boot seed.

**One deliberate departure from the profile: `Quotinator__AutoPurgeBundledImportActions=false`, on both
runs** (confirmed live 2026-08-08, #249). Fresh pins the application's own default, `true`, and under
that default every bundled batch's `Import_Action` rows — including the one whose `MergedFields` this
test queries — are purged immediately after a successful seed, so the row is already gone by the time
you inspect it.

Both of this test's containers publish `8080`, so the profile's `qt-env` must not be running while they
are — `docker rm -f qt-env` first, and re-establish the profile afterwards.

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

### 1. Remove the rule and confirm the conflict returns

Temporarily delete the Auntie Mame rule entirely from `nikhilnamal17-conflict-rules.json`
(`entityId: 088603c0-…`), then:

```bash
docker build -f docker/Dockerfile -t quotinator:local .
docker rm -f qt-rule-removed 2>/dev/null; docker volume rm qt-rule-removed 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-rule-removed -p 8080:8080 -v qt-rule-removed:/data \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=false quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:8080/api/v1/import/actions?status=pending"
```

**Expected:** with the rule removed, that quote's conflict stages `Pending` again, with
`ambiguousFields: ["date"]`.

That proves the mechanism consults the file's content on every seed rather than a cached decision from
an earlier run.

### 2. Restore the rule as `Replace`, rebuild, and seed a second container

Restore the rule and change its `resolution` from `Keep` to `Replace`, then rebuild and run a second
container against the rebuilt image — the first must be removed to free the port:

```bash
docker rm -f qt-rule-removed
docker volume rm qt-rule-replace 2>/dev/null
docker build -f docker/Dockerfile -t quotinator:local .
MSYS_NO_PATHCONV=1 docker run -d --name qt-rule-replace -p 8080:8080 -v qt-rule-replace:/data \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=false quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the health poll returns — the second container has completed its own first-boot seed
against the rebuilt image.

### 3. Read the recorded merge decision from the audit trail

**Stop the container first** — a copy taken while the app is writing can be torn, and a torn copy reads
as "no rows", which is indistinguishable from the assertion failing:

```bash
docker stop -t 15 qt-rule-replace
docker cp qt-rule-replace:/data/quotinatordata.db .claude/temp/inspect-181.db
docker cp qt-rule-replace:/data/quotinatordata.db-wal .claude/temp/inspect-181.db-wal
docker cp qt-rule-replace:/data/quotinatordata.db-shm .claude/temp/inspect-181.db-shm
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db ".claude/temp/inspect-181.db" \
  --sql "SELECT MergedFields FROM Import_Action WHERE EntityId='088603c0-b35a-1b48-977d-ca08489a0cbb' AND ActionType='Modify'"
```

**Expected:** with `resolution` changed to `Replace`, the row for the batch matching NikhilNamal17's own
rule file shows `"date":"2005"` — the incoming value, Replace won — changed from `"date":"1958"` under
the original `Keep`.

**On failure:** no rows at all is not a failed assertion — it is the auto-purge flag not having taken
effect. `Quotinator__AutoPurgeBundledImportActions=false` is required on both runs, and without it this
row is purged straight after the seed. Stop and re-run the container with the flag set.

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
docker rm -f qt-rule-removed qt-rule-replace 2>/dev/null
docker volume rm qt-rule-removed qt-rule-replace 2>/dev/null
rm -f .claude/temp/inspect-181.db .claude/temp/inspect-181.db-wal .claude/temp/inspect-181.db-shm
```

**Revert both rule-file edits.** Confirm `nikhilnamal17-conflict-rules.json` matches `git status` clean
before committing anything.

**Then rebuild the image, and only then move on.** The last build above baked the mutated `Replace`
rule into `quotinator:local`, and reverting the file on the host does not touch the image. Every
sibling test running that tag would otherwise be running a rule file that does not exist in the repo:

```bash
docker build -f docker/Dockerfile -t quotinator:local .
```
