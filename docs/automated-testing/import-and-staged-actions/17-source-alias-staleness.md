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

### 1. Create this test's own environment

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-import-17 --port 18617
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Read the pending and stale lists after the fresh seed, before anything else runs

```bash
curl -s http://localhost:18617/api/v1/version
docker logs qt-import-17 2>&1 | grep -c "\[Database - Seed\] .* report: "
docker logs qt-import-17 2>&1 | grep "\[Database - Seed\] .* alias staleness evaluated"
curl -s -H "X-Api-Key: smoketest" "http://localhost:18617/api/v1/admin/audit?table=Import_Action&pageSize=0" | grep -o '"operation":"Purge"' | wc -l
curl -s "http://localhost:18617/api/v1/import/actions?status=pending&pageSize=0" | grep -o '"totalCount":[0-9]*'
curl -s "http://localhost:18617/api/v1/import/actions?status=stale&pageSize=0" | grep -o '"totalCount":[0-9]*'
```

**Expected:** the counts have settled; the report count is non-zero, one line per bundled file, each
rendering `stale=0`; a line states that source-alias staleness was **evaluated** and over how many
aliases; the `Purge` trace count matches the number of bundled batches; and both `status=pending` and
`status=stale` report `totalCount: 0`.

**Each reading rules out a different way of producing those empty lists**, the same way
[`16-conflict-rule-staleness.md`](16-conflict-rule-staleness.md) sets out for conflict rules:

| Reading | Rules out |
|---|---|
| Report lines present, one per file | The seed never planned anything |
| `Purge` traces present | The action rows existed and were removed, leaving empty lists behind |
| Evaluation line present | The mechanism never compared the aliases at all |

**On failure:** a missing evaluation line makes this step inconclusive rather than passing — `stale=0`
in the report and an empty list are both produced equally by a mechanism that ran and found none and by
one that never ran. See the index's *When the expected situation does not occur*, cause 3.

### 3. Reseed and repeat, which is the second of the two paths

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" "http://localhost:18617/api/v1/admin/database/reseed"
docker logs qt-import-17 2>&1 | grep -c "\[Database - Seed\] .* report: "
docker logs qt-import-17 2>&1 | grep -c "\[Database - Seed\] .* alias staleness evaluated"
curl -s "http://localhost:18617/api/v1/import/actions?status=pending&pageSize=0" | grep -o '"totalCount":[0-9]*'
curl -s "http://localhost:18617/api/v1/import/actions?status=stale&pageSize=0" | grep -o '"totalCount":[0-9]*'
```

**Expected:** the reseed returns `200`; both counts have **increased** over step 2's readings, since the
reseed plans every bundled file again; and both `status=pending` and `status=stale` report
`totalCount: 0`.

**Comparing against step 2 rather than asserting a number** is what makes this the second path rather
than a repeat of the first: the reseed's own lines are indistinguishable from first-boot lines except
by there being more of them.

Every real bundled alias's canonical Source either already exists under its exact recorded title, or is
being legitimately created for the first time. None has actually been renamed away — which is why zero
is the correct result here, and why the log line rather than the zero is what proves the mechanism
looked.

**On failure:** as in step 2 — an unobservable evaluation makes both readings meaningless, and the
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

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-17
```
