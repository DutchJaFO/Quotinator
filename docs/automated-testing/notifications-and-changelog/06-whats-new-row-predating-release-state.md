# A what's-new row written before the release state existed is backfilled, not re-announced

**Smoke:** no
**Environment:** Fresh + Constrained
**Traces to:** #312

## Preconditions

**Beyond the profile.** One container of this test's own (`qt-notif-06`, on a bind-mounted directory rather
than the profile's named volume, so the file can be edited from a helper container while the app is
stopped), plus a one-shot `--rm alpine` running `sqlite3` to do the editing. The Constrained defect is
**a state, not a flag**: a what's-new row injected in the pre-backfill shape, and the schema counter
rolled back one step so the backfill replays over it.

`WhatsNewMetadataDto.ReleaseState` is a required property, so a row written by an earlier build cannot
be deserialized, cannot be identified, and would re-announce itself. A migration backfills it from the
convention that wrote those rows: a `version` key present meant a tagged release, absent meant the
unreleased section.

**Only databases carrying rows from an unreleased build are affected, so the state has to be
constructed** — a current database will never contain one naturally. That is why the defect is
constructed rather than provoked with a mount flag.

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
- **Do not assert which version replayed** — deleting `MAX(Version)` rolls back whichever migration is
  newest, so this stays correct after consolidation.

## Steps

### 1. Create a current, fully-migrated database of this test's own

```bash
# 1. a current, fully-migrated database of this test's own
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-06 --port 18506 \
  --bind /tmp/qt-notif-06/data
docker stop -t 15 qt-notif-06
```

**Expected:** the app reports healthy — the database is fully migrated — and the container is then
stopped, so the injection below writes to a file no process holds open.

**On failure:** if the app never reports healthy, the database is not at the current schema, and the
rollback below would then be undoing something other than the backfill. Stop.

### 2. Inject a what's-new row in the pre-backfill shape and undo the newest applied migration

```bash
# 2. inject a what's-new row in the pre-backfill shape, and undo the newest applied migration
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qt-notif-06/data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 /data/quotinatordata.db \
   \"INSERT INTO System_Notification (Id, Type, Body, DateCreated, IsDismissed, IsDeleted, Title, Metadata, MetadataKind) \
     VALUES (lower(hex(randomblob(16))), 'Information', 'legacy highlights', '2026-08-16 09:00:00', 0, 0, \
             'What''s new in v1.8.4', '{\\\"version\\\":\\\"1.8.4\\\"}', 'WhatsNew'); \
     DELETE FROM System_SchemaVersion WHERE Version = (SELECT MAX(Version) FROM System_SchemaVersion);\""
```

**Expected:** `sqlite3` completes with no error — the pre-backfill row is present and the newest
schema-version row is gone.

**On failure:** a `sqlite3` error means the constructed state was never reached, so the restart below
replays nothing and any result it produces is meaningless. Stop.

### 3. Restart so the rolled-back migration replays over the injected row

```bash
# 3. restart so the rolled-back migration replays over the injected row
docker start qt-notif-06
until docker logs qt-notif-06 2>&1 | grep -qE "Quotinator ready|Unhandled exception"; do sleep 1; done
```

**Expected:**

- Logs `applying … pending "Data" migration(s)` and reaches `Quotinator ready`.
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
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-06 \
  --bind /tmp/qt-notif-06/data
```

The data directory is a bind mount rather than a named volume, so removing the directory is what
removes its data.
