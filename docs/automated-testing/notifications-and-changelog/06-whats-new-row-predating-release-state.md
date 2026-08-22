# A what's-new row written before the release state existed is backfilled, not re-announced

**Smoke:** no
**Environment:** Fresh + Constrained
**Traces to:** #312

## Preconditions

`WhatsNewMetadataDto.ReleaseState` is a required property, so a row written by an earlier build cannot
be deserialized, cannot be identified, and would re-announce itself. A migration backfills it from the
convention that wrote those rows: a `version` key present meant a tagged release, absent meant the
unreleased section.

**Only databases carrying rows from an unreleased build are affected, so the state has to be
constructed** — a current database will never contain one naturally. The construction is: stand up a
fully-migrated database, insert the old shape, then roll the schema counter back one step so the
backfill re-applies on the next start.

## Determinism

**This works only while the backfill is the newest Data migration.** Rolling back `MAX(Version)`
replays whichever migration is newest — once a later one lands, that is what replays and this scenario
proves nothing.

**The tell is the injected row's `Metadata` coming back unchanged.** If that happens, roll back far
enough to reach the backfill rather than pasting its version number in here — a hardcoded number would
go stale on its own and get "fixed" by editing a digit.

- `DELETE ... WHERE Version = (SELECT MAX(Version) ...)` is deliberately relative, so it stays correct
  after migrations are consolidated.
- The container is **stopped** before the injection and started afterwards; writing to the database file
  underneath a running process is a different scenario.

## Steps

```bash
docker rm -f qws 2>/dev/null; rm -rf /tmp/qws; mkdir -p /tmp/qws/data

# 1. a current, fully-migrated database of this test's own
MSYS_NO_PATHCONV=1 docker run -d --name qws -e Quotinator__DataDir=/data \
  -v /tmp/qws/data:/data -p 8080:8080 quotinator:local
until curl -s http://localhost:8080/api/v1/health | grep -q healthy; do sleep 5; done
docker stop -t 15 qws

# 2. inject a what's-new row in the pre-backfill shape, and undo the newest applied migration
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qws/data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 /data/quotinatordata.db \
   \"INSERT INTO System_Notification (Id, Type, Body, DateCreated, IsDismissed, IsDeleted, Title, Metadata, MetadataKind) \
     VALUES (lower(hex(randomblob(16))), 'Information', 'legacy highlights', '2026-08-16 09:00:00', 0, 0, \
             'What''s new in v1.8.4', '{\\\"version\\\":\\\"1.8.4\\\"}', 'WhatsNew'); \
     DELETE FROM System_SchemaVersion WHERE Version = (SELECT MAX(Version) FROM System_SchemaVersion);\""

# 3. restart so the rolled-back migration replays over the injected row
docker start qws
until docker logs qws 2>&1 | grep -qE "Quotinator ready|Unhandled exception"; do sleep 1; done
```

## Expected output

- Logs `applying … pending "Data" migration(s)` and reaches `Quotinator ready`. **Do not assert which
  version replayed** — deleting `MAX(Version)` rolls back whichever migration is newest, so this stays
  correct after consolidation.
- The injected row's `Metadata` becomes `{"version":"1.8.4","releaseState":"Released"}` — a `version`
  key present meant a tagged release under the convention that wrote those rows.
- A row that already states its own release state is **unchanged**. `json_insert` only adds a key that
  is missing, so replaying the chain cannot rewrite correct data.

## Observed effect

Not yet established as a captured record. The distinguishing observation is the *unchanged* row: it is
what proves the backfill adds rather than overwrites, and it is easy to omit because a test that only
checks the injected row would pass either way.

## Cleanup

```bash
docker rm -f qws
rm -rf /tmp/qws
```
