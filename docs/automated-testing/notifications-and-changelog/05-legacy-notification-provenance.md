# The legacy notification gains provenance, and only a real v1.8.3 database gets a 1.8.3 row

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #312

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.3` tag**, and the current build must be rebuilt from an edited
`Directory.Build.props` (see below) rather than used as-is. One container name (`qprov`) is reused
across the two runs against one bind-mounted directory, plus a one-shot `--rm alpine` running `sqlite3`
to read the result. The whole thing is then repeated a second time with no v1.8.3 stage.

The migration that backfills legacy notification metadata restored the legacy notification's identity
but left its provenance null. A later migration fills that in and creates the `System_AppVersion` row it
points at — **conditionally**, because a database created fresh by an unreleased build also reaches this
migration and never ran v1.8.3.

**The current build must report a version other than `1.8.3`, or this proves nothing.** With both equal,
the row the migration inserts and the row the app records for itself are the same row, and the two
causes are indistinguishable. Temporarily set `Directory.Build.props`' `<Version>` to the next patch
number, build the image, and **restore the file immediately afterwards**.

That requirement is the direct opposite of
[`02-notification-metadata-and-provenance.md`](02-notification-metadata-and-provenance.md), whose
version-history expectation reads `Quotinator.Api | 1.8.3` for the current build. Both are left exactly
as they stand — the discrepancy is tracked separately, not resolved here.

## Determinism

- **The version difference is the whole test.** See Preconditions — this is the one setup step that
  cannot be skipped without silently invalidating the result.
- **The seeding wait polls for the announcement**, not a duration: v1.8.3 writes it after seeding ~800
  quotes, so a fixed wait can see zero and read as proof that nothing was written.
- This scenario uses **its own database**, not one shared with a sibling test — the row counts below
  are exact and any prior state breaks them.

## Steps

### 1. Seed a v1.8.3 database and wait for its announcement

```bash
docker rm -f qprov 2>/dev/null; rm -rf /tmp/qprov; mkdir -p /tmp/qprov/data
MSYS_NO_PATHCONV=1 docker run -d --name qprov -e Quotinator__DataDir=/data \
  -v /tmp/qprov/data:/data -p 8080:8080 ghcr.io/dutchjafo/quotinator:1.8.3
until [ "$(curl -s 'http://localhost:8080/api/v1/notifications?pageSize=0' \
  | grep -c 'Two API operation IDs were renamed')" = "1" ]; do sleep 5; done
docker rm -f qprov
```

**Expected:** the poll terminates — v1.8.3's announcement exists, so seeding has finished and the
database is in the state the upgrade is about.

**On failure:** a poll that never terminates means seeding did not complete and the announcement was
never written. Upgrading a database in that state proves nothing about provenance (see Determinism).
Stop.

### 2. Upgrade to the current build against the same database

```bash
MSYS_NO_PATHCONV=1 docker run -d --name qprov -e Quotinator__DataDir=/data \
  -v /tmp/qprov/data:/data -p 8080:8080 quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** the current build starts against the upgraded database and reports healthy.

### 3. Read the version history

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qprov/data:/data alpine \
  sh -c "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 -header /data/quotinatordata.db \
    'SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber;'"
```

**Expected:** exactly two rows: `Quotinator.Api | 1.8.3 | 1`, then `Quotinator.Api | <current> | 2`.

**The 1.8.3 row must sort first.** It predates every row this table can hold, and if it sorted last then
"the version that ran last" would answer 1.8.3 — and #81's catch-up would replay releases already
announced.

Joining the notifications back to that table attributes the v1.8.3-era announcement to **1.8.3**, and
anything written during this startup to the **current** version. Provenance records who wrote a row,
not who is running now.

### 4. Repeat the whole thing against a fresh database

Same build, no v1.8.3 stage. It needs its **own container name and its own directory**, distinct from
`qprov` and `/tmp/qprov` — run against the database the first half already upgraded, it would find the
1.8.3 row that half created and prove nothing:

```bash
docker rm -f qprov-fresh 2>/dev/null; rm -rf /tmp/qprov-fresh; mkdir -p /tmp/qprov-fresh/data
MSYS_NO_PATHCONV=1 docker run -d --name qprov-fresh -e Quotinator__DataDir=/data \
  -v /tmp/qprov-fresh/data:/data -p 8080:8080 quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qprov-fresh/data:/data alpine \
  sh -c "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 -header /data/quotinatordata.db \
    'SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber;'"
```

**The first container must be removed before this one starts** — both publish 8080.

**Expected:** exactly one row, the current build's own version, and **no 1.8.3 row at all**.

That guarantee is structural rather than guarded in SQL: an empty database takes the one-step baseline
path and never replays migrations, so the conditional insert never runs. Worth confirming rather than
assuming, which is why it is a step here.

**On failure:** a 1.8.3 row on a database that never ran 1.8.3 means the insert is unconditional — the
exact defect this half exists to catch, and invisible from the upgrade path alone, which produces that
row legitimately.

## Observed effect

Not yet established as a captured record. The ordering is the load-bearing observation — the two rows
existing is weaker evidence than the 1.8.3 row sorting first.

## Cleanup

```bash
docker rm -f qprov qprov-fresh 2>/dev/null
rm -rf /tmp/qprov /tmp/qprov-fresh
```

Both containers and both bind-mounted directories are this test's own — it creates no named volume, and
restoring the profile clears nothing it made.

**Two things this leaves behind that a profile restore does not fix.** `Directory.Build.props` must be
confirmed restored to its real `<Version>`, and `quotinator:local` must be rebuilt from the restored
file — every other test in the suite runs against that tag and would otherwise be testing a version
number this test invented.
