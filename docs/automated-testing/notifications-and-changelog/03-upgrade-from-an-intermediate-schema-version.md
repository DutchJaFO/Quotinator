# Upgrading from an intermediate schema version, not just the last release

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #312

## Preconditions

**Beyond the profile.** The Upgraded prior state is not an image at all but a **hand-built
intermediate**: the published `ghcr.io/dutchjafo/quotinator:1.8.3` tag creates the released baseline,
then one migration is applied by hand on top of it. Three containers of this test's own share one
bind-mounted directory — `q183` (the released image, **no published port**), a one-shot `--rm alpine`
running `sqlite3` to promote the schema, and `qv4` (the current build, publishing 8080).

**This exists because upgrading from the last release alone missed a startup-killing bug.** The
released-database path starts from v1.8.3, where `System_AppVersion` does not exist at all — so a
pre-migration read of it hits the missing-table path and returns null. A database at data **v4 or v5**
is a different state entirely: the table exists but the columns a later migration adds do not. That
state crashed startup with `no such column: Application`, and only a T1 run on a real dev database
exposed it.

**Whenever a migration adds a column to a table that startup reads before migrating, verify the
intermediate state as well as the released one.** ADR 009 mandates the last *released* schema; that is
a floor, not a ceiling, and unreleased intermediate versions exist on every developer machine.

The intermediate state is built by hand, from the released image plus the migrations in between.

## Determinism

**This is the one place version numbers are unavoidable.** The scenario's whole subject is a database
sitting *between* two schema versions, so it has to name which one — unlike every other test here,
where a number would be an incidental assertion. The numbers below **describe the state being
constructed, not an expected outcome**, and need re-deriving whenever migrations are consolidated.

The tell that they have gone stale: the current build logs no pending migrations at all, or fails with
a "table already exists" error instead of starting. Both mean the hand-built state no longer sits where
this scenario needs it.

- The released container publishes no port and waits on its own log.
- The current build's wait terminates on **either** outcome — ready or unhandled exception — so a
  genuine crash fails the test rather than hanging it.
- **As elsewhere, assert that replay completed — never the migration count or the version numbers.**

**Read from a container, not the host.** `-v /tmp/…:/data` resolves inside the Docker VM while
`dotnet run` executes on Windows against a different `/tmp`, so a host-side query silently finds
nothing. `Quotinator.Tools.DbInspector` is additionally `Mode=ReadOnly` by design and cannot perform
step 2's writes either — **do not "fix" the tool to allow writes.**

## Steps

### 1. Create the released baseline

```bash
# 1. released baseline — the schema version the last published image creates
docker rm -f q183 qv4 2>/dev/null
rm -rf /tmp/qv4
mkdir -p /tmp/qv4/data
MSYS_NO_PATHCONV=1 docker run -d --name q183 -e Quotinator__DataDir=/data \
  -v /tmp/qv4/data:/data ghcr.io/dutchjafo/quotinator:1.8.3
until docker logs q183 2>&1 | grep -q "Quotinator ready"; do sleep 1; done
docker rm -f q183
```

**Expected:** the released image reaches `Quotinator ready`, leaving a v1.8.3 database in
`/tmp/qv4/data`.

**On failure:** if it never reaches `Quotinator ready`, there is no released baseline to promote — the
hand-applied SQL below would run against an empty or absent file and the current build would then be
starting from a state nobody constructed. Stop.

### 2. Hand-apply the migration that first creates `System_AppVersion`

```bash
# 2. hand-apply the migration that first creates System_AppVersion — one step past the baseline —
#    plus a row the later column-adding migration must not destroy
cat > /tmp/qv4/data/promote.sql <<'SQL'
CREATE TABLE IF NOT EXISTS System_AppVersion (
    Id TEXT NOT NULL PRIMARY KEY, Version TEXT NOT NULL, DateCreated TEXT NOT NULL,
    DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0);
INSERT INTO System_AppVersion (Id, Version, DateCreated)
VALUES (lower(hex(randomblob(16))), '1.8.4', '2026-08-15 20:00:00');
INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (4, '2026-08-15 20:00:00');
SQL
docker run --rm -v /tmp/qv4/data:/data alpine \
  sh -c "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 /data/quotinatordata.db < /data/promote.sql"
rm /tmp/qv4/data/promote.sql
```

**Expected:** `sqlite3` completes with no error, leaving the database at the intermediate state — the
table present, the pre-existing `1.8.4` row written, and the schema counter promoted.

**On failure:** a `sqlite3` error means the intermediate state was never built, and the current build
would then be upgrading the plain released baseline instead — which is the path
[`02-notification-metadata-and-provenance.md`](02-notification-metadata-and-provenance.md) already
covers, not this one. Stop.

### 3. Start the current build against that intermediate state

```bash
# 3. current build against that state
MSYS_NO_PATHCONV=1 docker run -d --name qv4 -e Quotinator__DataDir=/data \
  -v /tmp/qv4/data:/data -p 8080:8080 quotinator:local
until docker logs qv4 2>&1 | grep -qE "Quotinator ready|Unhandled exception"; do sleep 1; done
docker logs qv4 2>&1 | grep -E "no such column|Unhandled|pending|schema updated|Quotinator ready"
```

**Expected:** logs `applying … pending "Data" migration(s)`, then `schema updated`, and reaches
`Quotinator ready`.

**Must not log `no such column` or `Unhandled exception`.** Before the fix this terminated the process
during startup, *after* the changelog database had already initialised — so a partial, healthy-looking
log prefix is not evidence of a successful start. Check for `Quotinator ready` explicitly.

**On failure:** no pending migrations at all, or a "table already exists" error, means the hand-built
state no longer sits where this scenario needs it (see Determinism) — the numbers need re-deriving, not
the build investigating. Stop.

### 4. Read the version history

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qv4/data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 -header /data/quotinatordata.db \
   'SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber;'"
```

**Expected:** exactly two rows: `NULL | 1.8.4 | 1` then `Quotinator.Api | <current> | 2`. The
pre-existing row survives with its `Application` still `NULL`, and the current version is **appended**
rather than replacing it.

## Observed effect

Well established for the failure: `no such column: Application`, terminating startup after the
changelog database had already initialised — which is what made the log look healthy up to the point it
died.

## Cleanup

```bash
docker rm -f q183 qv4 2>/dev/null
rm -rf /tmp/qv4
```

`q183` is already removed mid-run; it is named again here so a run abandoned partway leaves nothing
behind. Both containers and the bind-mounted directory are this test's own — it creates no named
volume, and restoring the profile clears nothing it made.
