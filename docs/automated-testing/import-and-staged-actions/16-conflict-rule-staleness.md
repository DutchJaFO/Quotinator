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

Run the **Fresh** profile first.

### 1. Wait for the bundled seed to finish

```bash
curl -s http://localhost:8080/api/v1/version
```

**Expected:** the counts have stopped changing — the container is no longer working through its
multi-file seed.

### 2. Reseed, then list the stale actions

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/admin/database/reseed"
curl -s "http://localhost:8080/api/v1/import/actions?status=stale&pageSize=0"
```

**Expected:** against current `main`, `status=stale` returns an empty list — the drift has been fixed.

With the apostrophe mismatch reintroduced, `status=stale` returns the Zootopia entity. **That is the
only way this step produces a non-empty result** — the shipped rule file is already corrected, so a run
against current `main` returns an empty list. To see the "before" state, `git stash` or check out the
pre-fix rule file and rebuild the image — do not treat the empty result as a failure.

## Observed effect

**Live-verified 2026-07-26 against a genuine, pre-existing data bug this mechanism caught on its first
real run — not a contrived fixture.**

`nikhilnamal17-conflict-rules.json`'s Zootopia rule (`entityId: 10e3fb48-…`, governing `quoteText`
with `Keep`) had its snapshot recorded with a straight apostrophe (`Life's`), while the real bundled
`NikhilNamal17_popular-movie-quotes.json` entry uses a curly one (`Life’s`). A genuine drift between
the rule's recorded assumption and reality, caught by the mechanism rather than by review.

## Cleanup

No files are written, but the reseed leaves the profile's database re-planned against every bundled
file. Restore the Fresh profile before the next test.
