# An alias is stale only on a genuine rename, never on first creation

**Smoke:** no
**Environment:** Fresh
**Traces to:** #153

## Preconditions

An alias is stale **only** when the Source its own `canonicalTitle`/`canonicalType` deterministically
hashes to (`EntityIdentity.SourceId`, fixed at creation and never recomputed on a later Modify) already
exists **under a different current title** — a genuine rename since the alias was authored.

The distinction that matters: guiding the *first-ever* creation of a Source under its correct name is
the alias doing its normal job, not staleness.

Beyond the Fresh profile: both a fresh seed and a reseed are checked, because they exercise different
paths. The fresh seed is the profile's own first boot; this test issues the reseed itself, in the steps
below.

## Determinism

**Wait for the bundled seed to finish before checking** — same partial-seed caveat as
[`16-conflict-rule-staleness.md`](16-conflict-rule-staleness.md). Poll `/api/v1/version` until the
counts settle.

**Every existing unit fixture pre-seeded the canonical Source as a real DB row**, which masked both
bugs below. That is why this check is live: the failing condition only exists when the canonical Source
has *not* yet been created.

## Steps

Run the **Fresh** profile first.

### 1. Read the pending and stale lists after the fresh seed, before anything else runs

```bash
curl -s http://localhost:8080/api/v1/version
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=0"
curl -s "http://localhost:8080/api/v1/import/actions?status=stale&pageSize=0"
```

**Expected:** the counts have settled, the log states that source-alias staleness was evaluated and how
many aliases it considered, and both `status=pending` and `status=stale` return `totalCount: 0`
consistent with that.

**On failure:** if no such log line exists, the evaluation is unobservable and this step establishes
nothing either way — an empty list is produced equally by a mechanism that ran and found none and by
one that never ran. See the index's *When the expected situation does not occur*, cause 3.

### 2. Reseed and repeat, which is the second of the two paths

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/admin/database/reseed"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=0"
curl -s "http://localhost:8080/api/v1/import/actions?status=stale&pageSize=0"
```

**Expected:** the reseed returns `200`, the log again states that alias staleness was evaluated, and
both `status=pending` and `status=stale` return `totalCount: 0`.

Every real bundled alias's canonical Source either already exists under its exact recorded title, or is
being legitimately created for the first time. None has actually been renamed away — which is why zero
is the correct result here, and why the log line rather than the zero is what proves the mechanism
looked.

**On failure:** as in step 1 — an unobservable evaluation makes both readings meaningless, and the
reseed's own status code is what separates "nothing was stale" from "the reseed never ran".

## Observed effect

**Two false-positive bugs were found and fixed live via this exact check, neither catchable by unit
tests alone.**

1. The first version checked only "does a Source with this exact title exist right now", which cannot
   distinguish a genuine rename from the alias's own legitimate job of guiding a first-ever creation.
   It flagged 7 real bundled aliases as stale purely because their canonical Source had not been created
   by an earlier file yet.
2. A same-batch fix — checking `sourceIndex`, the batch's own in-memory Add cache — still needed the
   id-based rewrite to fully clear a `SELECT *`-by-title query being unable to distinguish those same
   two cases when nothing had been indexed yet either.

## Cleanup

No files are written, but the reseed leaves the profile's database re-planned against every bundled
file. Restore the Fresh profile before the next test.
