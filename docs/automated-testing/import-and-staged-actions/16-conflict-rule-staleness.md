# A rule whose recorded snapshot no longer matches reality stages Stale, not Decided

**Smoke:** no
**Environment:** Fresh
**Traces to:** #153

## Preconditions

A `ConflictResolutionRule` records an `existingRecord`/`incomingRecord` snapshot. When those no longer
match the current staging run's real field values, the rule is never silently reapplied — the action
stages `Stale`.

Beyond the Fresh profile: **a reseed is required; the profile's own first boot cannot exercise this.**
A brand-new database only ever stages `Add` actions, because nothing exists yet to conflict with.
`POST /admin/database/reseed` re-plans every bundled file against the now-populated database and
genuinely exercises the `Modify`/rule path — the same thing a real redeployment against an
already-seeded volume does. This test issues that reseed itself, in the steps below.

## Determinism

**Wait for the full bundled seed before querying.** A `status=stale` or `status=pending` check against
a container still working through its multi-file seed reads a partially-seeded, misleading state.
Poll `/api/v1/version` until the counts stop changing rather than checking immediately.

**The shipped rule file is already corrected**, so a run against current `main` returns an empty list.
To see the "before" state, `git stash` or check out the pre-fix rule file and rebuild the image — do
not treat the empty result as a failure.

## Steps

### 1. Create this test's own environment

```bash
docker rm -f qt-import-16 2>/dev/null; docker volume rm qt-import-16-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-import-16 -p 18616:8080 -v qt-import-16-data:/data \
  -e Quotinator__DataDir=/data \
  -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true \
  quotinator:local
until curl -sf http://localhost:18616/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Wait for the bundled seed to finish

```bash
curl -s http://localhost:18616/api/v1/version
```

**Expected:** the counts have stopped changing — the container is no longer working through its
multi-file seed.

### 3. Reseed, then list the stale actions

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" "http://localhost:18616/api/v1/admin/database/reseed"
docker logs qt-import-16 2>&1 | grep -c "\[Database - Seed\] .* report: "
docker logs qt-import-16 2>&1 | grep "\[Database - Seed\] .* rule staleness evaluated"
curl -s -H "X-Api-Key: <your admin key>" "http://localhost:18616/api/v1/admin/audit?table=Import_Action&pageSize=0" | grep -o '"operation":"Purge"' | wc -l
curl -s "http://localhost:18616/api/v1/import/actions?status=stale&pageSize=0" | grep -o '"totalCount":[0-9]*'
```

**Expected:** the reseed returns `200`; the per-file report count is non-zero, one line per bundled
file, each rendering `stale=0`; a line states that rule staleness was **evaluated** and over how many
rules; the `Purge` trace count matches the number of bundled batches; and `status=stale` reports
`totalCount: 0`.

**Each reading rules out a different way of producing that empty list**, which is why an empty list on
its own establishes nothing:

| Reading | Rules out |
|---|---|
| Report lines present, one per file | The reseed never re-planned anything |
| `Purge` traces present | The action rows existed and were removed, leaving an empty list behind |
| Evaluation line present | The mechanism never compared the rules at all |

**`stale=0` in the report cannot carry the last one.** It is produced identically by *compared the
shipped rules, none had drifted* and by *never compared anything* — a count of zero is not evidence
that something looked.

**On failure:** no report lines means the reseed did not re-plan — a setup failure, not a staleness
result; stop. A missing evaluation line means the mechanism's own execution is unobservable, so neither
the report nor the empty list can establish whether it ran. That is the application's gap rather than
this document's — see the index's *When the expected situation does not occur*, cause 3.

## Observed effect

**Live-verified 2026-07-26 against a genuine, pre-existing data bug this mechanism caught on its first
real run — not a contrived fixture.**

`nikhilnamal17-conflict-rules.json`'s Zootopia rule (`entityId: 10e3fb48-…`, governing `quoteText`
with `Keep`) had its snapshot recorded with a straight apostrophe (`Life's`), while the real bundled
`NikhilNamal17_popular-movie-quotes.json` entry uses a curly one (`Life’s`). A genuine drift between
the rule's recorded assumption and reality, caught by the mechanism rather than by review.

## Cleanup

```bash
docker rm -f qt-import-16
docker volume rm qt-import-16-data
```
