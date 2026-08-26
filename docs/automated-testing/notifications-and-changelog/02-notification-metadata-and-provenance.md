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

Every command here was run for real; the expected values below are observed output, not predictions.

**The database is read with `Quotinator.Tools.DbInspector`, from the host.** That is a change from how
this document read it before, and the reason the old note against it no longer applies: it warned that
.NET resolves `/tmp/x` to `C:\tmp\x` while `docker -v` resolves it to `%TEMP%\x`, so a host-side query
silently read nothing — **which looks exactly like a passing check**. The bind path below is an
absolute Windows path built from `$PWD`, so there is only one directory and nothing translates it.
Measured 2026-08-26: DbInspector reads the live, WAL-mode file the running container is writing to.

What that removes is worth stating, because the alternative was costly: each query used to run in a
throwaway `alpine` container that installed `sqlite3` **over the network** first, and the SQL had to be
nested three quoting levels deep. This document itself recorded that the nesting had already broken the
query outright in both shells.

**The build under test reports a different version than the database it upgrades, and that is the
normal case.** Development carries the milestone's target version with an `-alpha` suffix from
milestone start — see `docs/workflow/checklist.md`'s *Version during development* — so a build
upgrading a v1.8.3 database records its own row alongside the one already there. **Two rows is the
expected answer**, and the second is the running build's own.

**This document expected exactly one row until #339's full run, and that was a symptom rather than a
rule.** While development shared the released version number, `AppVersionTracker.RecordCurrentAsync` —
append-if-new — found a `Quotinator.Api | 1.8.3` row already present and recorded nothing, so the
running build left no trace at all. Worse for this test specifically, **both notifications then
reported the same `WrittenBy`**, which is exactly what provenance must not do: the assertion could not
tell *records who wrote a row* from *echoes whatever is running*.

Measured after the version moved to `1.9.0-alpha`: the #279 announcement resolves to
`Quotinator.Api 1.8.3` and the what's-new row to `Quotinator.Api 1.9.0-alpha`. That contrast is the
point of the test and only exists when the versions differ.

**Compare against `Directory.Build.props`, never against a literal.** The version moves every
milestone; what stays true is that the running build's version appears as the last row and that the
announcement is attributed to the release that shipped it.

[`05-legacy-notification-provenance.md`](05-legacy-notification-provenance.md) needed the two to differ
badly enough that it edited `Directory.Build.props` and rebuilt the image to manufacture it. With the
development version now genuinely distinct, that edit is no longer necessary there either.

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

```powershell
$dataDir = "$PWD\.claude\temp\qt-notif-02-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

docker pull ghcr.io/dutchjafo/quotinator:1.8.3
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-02-183 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir

while (-not (docker logs qt-notif-02-183 2>&1 | Select-String -SimpleMatch 'Quotinator ready')) { Start-Sleep 1 }
docker logs qt-notif-02-183 2>&1 | Select-String -SimpleMatch 'baseline'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-02-183 --bind $dataDir
```

**Expected:** v1.8.3 reports `schema created at baseline`, the released schema this upgrade starts from.

**On failure:** no baseline line, or a container that never reaches `Quotinator ready`, means the
released database was never created — everything below would then be read from an empty or partial
file, which looks exactly like a passing check. Stop.

### 2. Start the current build against that database

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-02-current --port 18502 `
  --image quotinator:local --bind $dataDir

docker logs qt-notif-02-current 2>&1 | Select-String -SimpleMatch 'pending', 'schema updated', 'Quotinator ready'
```

**Expected:** the current build reports `applying … pending "Data" migration(s)` followed by
`schema updated`, and reaches `Quotinator ready`. **No exception.**

`reenter` rather than `create`: this run must find the v1.8.3 database step 1 left behind, and that
distinction is the entire subject of the test.

### 3. Read the stored payload and its provenance

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT n.Title, n.MetadataKind, n.Metadata, v.Application || ' ' || v.Version AS WrittenBy FROM System_Notification n LEFT JOIN System_AppVersion v ON v.Id = n.AppVersionId"
```

**Expected:** `MetadataKind` is `Announcement`, and `Metadata` carries the `announcement` key together
with `releaseState`, the `version` the announcement is *about*, and a `contentHash` — the shape step 7
describes in full. It is **not** the bare `{"announcement":"GetAllImportBatches"}` this step asked for
until #339's full run; that expectation predated the common release fields and contradicted step 7 in
the same document.

**`Metadata` must not contain a `Kind` property.** Found live during #312's own T2 pass: payloads
stored `{"announcement":"…","Kind":0}`, because `[JsonIgnore]` on an abstract base property is not
inherited by the derived override — `System.Text.Json` reads attributes from the most-derived
declaration. The column already records the kind, so a second copy can drift out of step with it. **No
unit test caught this** — round-tripping succeeded either way. Only reading the stored bytes did.

**`WrittenBy` differs between the two rows, and that is the assertion.** The #279 announcement resolves
to `Quotinator.Api 1.8.3` — the release that shipped it — while any row written during this startup
resolves to the running build's own version, `1.9.0-alpha` when measured. The FK genuinely joins rather
than being written null or dangling, *and* provenance records who wrote each row rather than echoing
whoever is running now. With both versions equal those two claims are indistinguishable, which is what
the Preconditions section is about.

### 4. Read the append-only version history

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber"
(Select-Xml -Path Directory.Build.props -XPath '//Version').Node.InnerText

docker restart qt-notif-02-current
dotnet script scripts/testing/http.csx -- --url "http://localhost:18502/api/v1/health" --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) AS RowsAfterRestart FROM System_AppVersion"
```

**Expected:** two rows — `Quotinator.Api | 1.8.3 | 1`, then `Quotinator.Api | <the version printed
from Directory.Build.props> | 2`. The released version sorts first and the running build is
**appended**, never replacing it. `Application` and `Version` are separate columns, never one
concatenated value. `RowsAfterRestart` is still `2`: recording the same application+version twice
appends nothing, or every restart would grow the table.

**Compare the second row against `Directory.Build.props`, not a literal** — which is why the file is
read in the same block. The value moves every milestone and the assertion is the ordering and the
count, not the string.

`SequenceNumber` is the explicit recording order. It exists because `DateCreated` is second-resolution
and cannot separate rows written within the same second, and because SQLite's implicit `rowid` is
reusable once a table's highest row is removed — neither is a trustworthy answer to "which version ran
last".

### 5. Confirm dedupe is structural, not textual

```powershell
$before = (Invoke-RestMethod "http://localhost:18502/api/v1/notifications?pageSize=0").totalCount
docker restart qt-notif-02-current
dotnet script scripts/testing/http.csx -- --url "http://localhost:18502/api/v1/health" --wait-for 200 --status
$after = (Invoke-RestMethod "http://localhost:18502/api/v1/notifications?pageSize=0").totalCount
"before=$before after=$after unchanged=$($before -eq $after)"

([regex]::Matches((docker logs qt-notif-02-current 2>&1 | Out-String), 'applying .* pending')).Count
```

**Expected:** `unchanged=True`. A producer runs on every startup; the history is what stops it writing
twice. **And no repeat migration on a second start** — the `applying … pending` count stays at the one
occurrence step 2 produced, rather than growing with each restart.

### 6. Confirm the old text-matching path is genuinely dead

A row whose `Body` mentions `GetAllImportBatches` but whose `Metadata` is `NULL` is what the pre-#312
suppression would have matched on. `MetadataKind` is left `NULL` too — a row with no metadata is exactly
the shape being tested, so giving it a kind would defeat the point.

**The real announcement row has to be deleted in the same edit, and without that this step cannot
fail.** Measured 2026-08-26: inserting the legacy row while the metadata-carrying announcement is still
present leaves the total unchanged — correctly, because structural dedupe matches the *real* row and
suppresses a second copy whether or not text matching is also alive. The two outcomes are identical, so
the assertion says nothing. Removing the structural row first leaves the legacy text as the only thing
a suppressor could match on, which is the whole question.

Written against a **stopped** container:

```powershell
docker stop -t 15 qt-notif-02-current

dotnet script scripts/testing/execute-sql.csx -- --db "$dataDir\quotinatordata.db" --sql @'
DELETE FROM System_Notification WHERE MetadataKind = 'Announcement';
INSERT INTO System_Notification (Id, Type, Title, Body, Metadata, MetadataKind, ExpiresAt, IsDismissed, DateCreated, IsDeleted) VALUES
  ('a0000312-0000-4000-8000-000000000001','Information','Legacy text-matched row','Two API operation IDs were renamed, including GetAllImportBatches.',NULL,NULL,NULL,0,'2026-01-01 00:00:00',0);
'@

docker start qt-notif-02-current
dotnet script scripts/testing/http.csx -- --url "http://localhost:18502/api/v1/health" --wait-for 200 --status

$rows = (Invoke-RestMethod "http://localhost:18502/api/v1/notifications?pageSize=0").items
"announcementBack=$(@($rows | Where-Object { $_.title -match 'operation IDs were renamed' }).Count)"
"legacyRowStillThere=$(@($rows | Where-Object { $_.title -ceq 'Legacy text-matched row' }).Count)"
```

**Expected:** `announcementBack=1` and `legacyRowStillThere=1` — the announcement is written again
alongside the legacy row, because a body match no longer suppresses anything.

Asserted by identity rather than by a total: the totals are equal before and after here (one row
deleted, one written), so a count could not tell re-announced from never-touched.

That is the whole point of the change: #278 embedded a key in the message text and matched it with
`Contains`, which could not distinguish `WhatsNew:v1.9.1` from `WhatsNew:v1.9.10`. Structured metadata
replaced it, so a row carrying only the old text is no longer recognised as a duplicate.

**On failure:** `announcementBack=0` means text matching is still live somewhere — which is the
regression this step exists to catch, not a setup problem.

### 7. Confirm every payload states its release

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT Title, MetadataKind, Metadata FROM System_Notification"
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

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-02-183 --bind $dataDir
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-02-current --bind $dataDir
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
```

`qt-notif-02-183` is already removed mid-run; it is named again here so a run abandoned partway leaves nothing
behind. The data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
