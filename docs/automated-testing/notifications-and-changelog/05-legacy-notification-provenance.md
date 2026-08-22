# The legacy notification gains provenance, and only a real v1.8.3 database gets a 1.8.3 row

**Smoke:** no
**Traces to:** #312

## Preconditions

The migration that backfills legacy notification metadata restored the legacy notification's identity
but left its provenance null. A later migration fills that in and creates the `System_AppVersion` row it
points at — **conditionally**, because a database created fresh by an unreleased build also reaches this
migration and never ran v1.8.3.

**The current build must report a version other than `1.8.3`, or this proves nothing.** With both equal,
the row the migration inserts and the row the app records for itself are the same row, and the two
causes are indistinguishable. Temporarily set `Directory.Build.props`' `<Version>` to the next patch
number, build the image, and **restore the file immediately afterwards**.

## Determinism

- **The version difference is the whole test.** See Preconditions — this is the one setup step that
  cannot be skipped without silently invalidating the result.
- **The seeding wait polls for the announcement**, not a duration: v1.8.3 writes it after seeding ~800
  quotes, so a fixed wait can see zero and read as proof that nothing was written.
- This scenario uses **its own database**, not one shared with a sibling test — the row counts below
  are exact and any prior state breaks them.

## Steps

**Seed a v1.8.3 database, then upgrade it:**

```bash
docker rm -f qprov 2>/dev/null; rm -rf /tmp/qprov; mkdir -p /tmp/qprov/data
MSYS_NO_PATHCONV=1 docker run -d --name qprov -e Quotinator__DataDir=/data \
  -v /tmp/qprov/data:/data -p 8080:8080 ghcr.io/dutchjafo/quotinator:1.8.3
until [ "$(curl -s 'http://localhost:8080/api/v1/notifications?pageSize=0' \
  | grep -c 'Two API operation IDs were renamed')" = "1" ]; do sleep 5; done
docker rm -f qprov

MSYS_NO_PATHCONV=1 docker run -d --name qprov -e Quotinator__DataDir=/data \
  -v /tmp/qprov/data:/data -p 8080:8080 quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
```

**Read the version history:**

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qprov/data:/data alpine \
  sh -c "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 -header /data/quotinatordata.db \
    'SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber;'"
```

**Then repeat the whole thing against a fresh database** — same build, no v1.8.3 stage.

## Expected output

Exactly two rows: `Quotinator.Api | 1.8.3 | 1`, then `Quotinator.Api | <current> | 2`.

**The 1.8.3 row must sort first.** It predates every row this table can hold, and if it sorted last then
"the version that ran last" would answer 1.8.3 — and #81's catch-up would replay releases already
announced.

Joining the notifications back to that table attributes the v1.8.3-era announcement to **1.8.3**, and
anything written during this startup to the **current** version. Provenance records who wrote a row,
not who is running now.

**On a fresh database** the same build produces exactly one row — its own version — and no 1.8.3 row at
all. That guarantee is structural rather than guarded in SQL: an empty database takes the one-step
baseline path and never replays migrations. Worth confirming rather than assuming, which is why it is a
step here.

## Observed effect

Not yet established as a captured record. The ordering is the load-bearing observation — the two rows
existing is weaker evidence than the 1.8.3 row sorting first.

## Cleanup

```bash
docker rm -f qprov
rm -rf /tmp/qprov
```

Confirm `Directory.Build.props` has been restored.
