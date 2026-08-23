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

**Expected:** the counts have settled, and the fresh-seed `status=pending` and `status=stale` checks
both return `totalCount: 0`.

### 2. Reseed and repeat, which is the second of the two paths

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" "http://localhost:8080/api/v1/admin/database/reseed"
curl -s "http://localhost:8080/api/v1/import/actions?status=pending&pageSize=0"
curl -s "http://localhost:8080/api/v1/import/actions?status=stale&pageSize=0"
```

**Expected:** the post-reseed `status=pending` and `status=stale` checks also return `totalCount: 0`.

Every real bundled alias's canonical Source either already exists under its exact recorded title, or is
being legitimately created for the first time. None has actually been renamed away.

> **This test cannot currently fail, and that is a known limitation rather than a clean pass.** Both of
> its assertions are that a list is empty. A staleness mechanism that never fires, a reseed that
> silently did nothing, a regressed `status=` filter, and purged `Import_Action` rows all produce that
> same empty list — indistinguishable from the intended result.
>
> **Nothing in the run ever produces a stale alias**, so there is no positive control proving the
> mechanism is alive. Closing this needs a genuine rename — a Source that exists under a title an alias
> no longer points at — constructed by the test itself. The index's *A test that needs a defective input
> must own that input* section describes why the shipped aliases cannot supply it: they were corrected,
> and a test whose failing input was production data stops being able to fail the moment that data is
> fixed.
>
> The same limitation applies to
> [`16-conflict-rule-staleness.md`](16-conflict-rule-staleness.md), for the same reason.

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
