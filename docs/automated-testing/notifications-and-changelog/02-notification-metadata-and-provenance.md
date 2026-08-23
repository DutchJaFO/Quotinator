# Notification metadata, provenance, and the released-database migration path

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #312

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.3` tag** — the migration path must be exercised against a database
created by the last published release, never an accumulated dev database, which is this test's ADR 009
check as much as its own. Two app containers of this test's own share one bind-mounted directory:
`qt-notif-02-183` (the released image, **no published port**) and `qt-notif-02-current` (the current build,
publishing `18502`).
A third kind of container appears repeatedly — a one-shot `--rm alpine` with `sqlite3` installed, used
only to read the file.

Every command here was run for real; the expected values below are observed output, not predictions.

**Read the database from a container, not from the host.** `-v /tmp/…:/data` resolves inside the Docker
VM, while `dotnet run` executes on Windows where `/tmp` is a different directory entirely — a host-side
`DbInspector` call against that path finds an empty or non-existent file and reports nothing, **which
reads exactly like a passing check**. Every query below therefore runs in the same container filesystem
the app wrote to.

**The current build's own version overlaps 1.8.3 here.** The version-history expectation below reads
`Quotinator.Api | 1.8.3 | 1 | 1` as the only row, while
[`05-legacy-notification-provenance.md`](05-legacy-notification-provenance.md) requires the current
build to report something *other* than 1.8.3 for its own scenario to mean anything. Both are left
exactly as they stand — the discrepancy is tracked separately, not resolved here.

## Determinism

- **The v1.8.3 container publishes no port**, so neither HTTP poll applies — it waits on its own log
  instead. The current build publishes `18502` and polls health.
- **Do not assert how many migrations ran or which versions were involved.** Those numbers move every
  milestone and are consolidated before release. What is verified is that replay from a genuinely
  released database completes.
- **Do not transcribe the content hash as a literal.** It is recomputed whenever the announcement's
  wording changes, so a pasted expected value goes stale the first time anyone edits it. Read the row
  and confirm the fields are present and self-consistent.
- The dedupe check compares `totalCount` **before and after a restart** rather than against a fixed
  number — see the note below for why that does not breach the no-counts rule.

**Why comparing a total is legitimate here.** This is the one place a *total* is the right thing to
read, and it does not breach the no-counts rule: nothing here expects a particular number, only that
the number does not change across a restart. Comparing the total rather than one notification is
deliberately stronger — it catches *any* producer duplicating itself, including one added after this
was written. **Do not replace it with a specific expected count, and do not narrow it to a single
notification.**

## Steps

### 1. Seed a genuine v1.8.3 database

```bash
docker pull ghcr.io/dutchjafo/quotinator:1.8.3
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-02-183 \
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind /tmp/qt-notif-02/data --no-wait
until docker logs qt-notif-02-183 2>&1 | grep -q "Quotinator ready"; do sleep 1; done
docker logs qt-notif-02-183 2>&1 | grep baseline
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-02-183
```

**Expected:** v1.8.3 reports `schema created at baseline`, the released schema this upgrade starts from.

**On failure:** no baseline line, or a container that never reaches `Quotinator ready`, means the
released database was never created — everything below would then be read from an empty or partial
file, which looks exactly like a passing check. Stop.

### 2. Start the current build against that database

```bash
MSYS_NO_PATHCONV=1 docker run -d --name qt-notif-02-current -e Quotinator__DataDir=/data \
  -v /tmp/qt-notif-02/data:/data -p 18502:8080 quotinator:local
until curl -sf http://localhost:18502/api/v1/health > /dev/null; do sleep 1; done
docker logs qt-notif-02-current 2>&1 | grep -E "pending|schema updated"
```

**Expected:** the current build reports `applying … pending "Data" migration(s)` followed by
`schema updated`, and reaches `Quotinator ready`. **No exception.**

### 3. Read the stored payload and its provenance

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qt-notif-02/data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 -header /data/quotinatordata.db \
   'SELECT n.Title, n.MetadataKind, n.Metadata, v.Application || \" \" || v.Version AS WrittenBy FROM System_Notification n LEFT JOIN System_AppVersion v ON v.Id = n.AppVersionId;'"
```

**Expected:** `MetadataKind` is `Announcement`, and `Metadata` is exactly
`{"announcement":"GetAllImportBatches"}`.

**`Metadata` must not contain a `Kind` property.** Found live during #312's own T2 pass: payloads
stored `{"announcement":"…","Kind":0}`, because `[JsonIgnore]` on an abstract base property is not
inherited by the derived override — `System.Text.Json` reads attributes from the most-derived
declaration. The column already records the kind, so a second copy can drift out of step with it. **No
unit test caught this** — round-tripping succeeded either way. Only reading the stored bytes did.

`WrittenBy` resolves to `Quotinator.Api 1.8.3` — the `AppVersionId` FK actually joins, rather than
being written null or dangling.

### 4. Read the append-only version history

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qt-notif-02/data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 -header /data/quotinatordata.db \
   'SELECT Application, Version, SequenceNumber, COUNT(*) OVER () AS TotalRows FROM System_AppVersion;'"
```

**Expected:** exactly one row: `Quotinator.Api | 1.8.3 | 1 | 1`. `Application` and `Version` are
separate columns, never one concatenated value. After `docker restart qt-notif-02-current`, still exactly one row:
recording the same application+version twice appends nothing, or every restart would grow the table.

`SequenceNumber` is the explicit recording order. It exists because `DateCreated` is second-resolution
and cannot separate rows written within the same second, and because SQLite's implicit `rowid` is
reusable once a table's highest row is removed — neither is a trustworthy answer to "which version ran
last".

### 5. Confirm dedupe is structural, not textual

```bash
curl -s "http://localhost:18502/api/v1/notifications?pageSize=0" | grep -o '"totalCount":[0-9]*'
docker restart qt-notif-02-current
until curl -sf http://localhost:18502/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:18502/api/v1/notifications?pageSize=0" | grep -o '"totalCount":[0-9]*'
```

**Expected:** `totalCount` is identical before and after the restart. A producer runs on every startup;
the history is what stops it writing twice. **And no repeat on a second start** — `docker restart qt-notif-02-current`
must not log `applying … pending` again.

### 6. Confirm the old text-matching path is genuinely dead

A row whose `Body` mentions `GetAllImportBatches` but whose `Metadata` is `NULL` is what the pre-#312
suppression would have matched on. `MetadataKind` is left `NULL` too — a row with no metadata is exactly
the shape being tested, so giving it a kind would defeat the point. Written against a **stopped**
container:

```bash
docker stop -t 15 qt-notif-02-current
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qt-notif-02/data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 /data/quotinatordata.db \
   \"INSERT INTO System_Notification (Id, Type, Title, Body, Metadata, MetadataKind, ExpiresAt, IsDismissed, DateCreated, IsDeleted) VALUES
     ('a0000312-0000-4000-8000-000000000001','Information','Legacy text-matched row','Two API operation IDs were renamed, including GetAllImportBatches.',NULL,NULL,NULL,0,'2026-01-01 00:00:00',0);\""
docker start qt-notif-02-current
until curl -sf http://localhost:18502/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:18502/api/v1/notifications?pageSize=0" | grep -o '"totalCount":[0-9]*'
docker restart qt-notif-02-current
until curl -sf http://localhost:18502/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:18502/api/v1/notifications?pageSize=0" | grep -o '"totalCount":[0-9]*'
```

**Expected:** `totalCount` **increases** across this restart — the opposite of step 5. The announcement
is written again, because a body match no longer suppresses anything.

That is the whole point of the change: #278 embedded a key in the message text and matched it with
`Contains`, which could not distinguish `WhatsNew:v1.9.1` from `WhatsNew:v1.9.10`. Structured metadata
replaced it, so a row carrying only the old text is no longer recognised as a duplicate.

**On failure:** if `totalCount` stays the same, text matching is still live somewhere — which is the
regression this step exists to catch, not a setup problem.

### 7. Confirm every payload states its release

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v /tmp/qt-notif-02/data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null 2>&1; sqlite3 -header /data/quotinatordata.db \
   'SELECT Title, MetadataKind, Metadata FROM System_Notification;'"
```

**Expected:** the #279 announcement's payload carries its `announcement` key plus `releaseState`, the
`version` the announcement is *about* (v1.8.3 shipped the renames — not the version running now, which
the row's own `AppVersionId` records), and a `contentHash`. Confirm those are present and
self-consistent rather than matching a transcribed literal.

No payload may contain a null-valued property. An unset value is omitted, so a reader never has to
decide what an explicit `null` was supposed to mean.

A notification about no release at all — a schema-version overshoot — reads
`"releaseState":"NotApplicable"` and carries no version. Borrowing the running version would make the
same unresolved overshoot re-announce itself on every upgrade.

## Observed effect

Well established — the stored rows above *are* the observed effect, and the `Kind`-property defect was
found by reading them rather than by any assertion.

> Editing the announcement's wording in `Program.cs` deliberately re-announces it to everyone, because
> the producer's hash then stops matching the migration-frozen one. That is what a content hash is for
> — but it means a wording tweak is a user-visible change, not a cosmetic one.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-02-183
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-02-current \
  --bind /tmp/qt-notif-02/data
```

`qt-notif-02-183` is already removed mid-run; it is named again here so a run abandoned partway leaves nothing
behind. The data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
