# The changelog is served from its own on-disk database, not the JSON fallback

**Smoke:** yes
**Environment:** Fresh
**Traces to:** #309

## Preconditions

**Beyond the profile.** One container of this test's own (`qt-notif-07`), on a bind-mounted directory
rather than the profile's named volume, so the host can see the database files directly. It is restarted
once, in place.

The changelog database is a file beside the main database. What this verifies is that **file-backed
storage is what ships and what serves reads** — a feature, checkable immediately.

## Determinism

**Assert on the positive line, never on the absence of the negative one.** The About page renders
identically whichever source served it, because the JSON fallback does its job — which is exactly why
this defect survived a full T2 pass unnoticed. An absent fallback warning was originally treated as the
decisive signal, but absence proves nothing: until this was fixed, the empty-database fallback logged
nothing at all, so a silently-fallen-back read and a healthy one produced identical output. Only a
positive *"the database answered"* statement distinguishes them.

**The importer and reader counts must match.** They report the same unit deliberately, so
`refreshed N entries` and `served N entries` are directly comparable; a read reporting fewer than the
import wrote means it was served a partial or stale copy. **Compare the two numbers to each other —
neither is predicted**, since the changelog grows with every release.

> **A sixteen-minute wait used to sit here and was removed (developer direction, 2026-08-19.)** It slept
> past the one observed failure at +13 minutes and re-read. The reasoning was that no shorter check
> could see the defect — but the mechanism behind that 13 minutes was never established, because the fix
> removed the dependency rather than explaining the timer. **An interval with no basis in an understood
> mechanism tests nothing**: it is not derived, so a regression failing at 40 minutes would sail past it,
> and a green result buys confidence it has not earned. A test verifies a feature or a reliable
> behaviour, never a guessed delay.
>
> What guards this now is deterministic and instant: the file exists on disk, and
> `ChangelogDatabaseWiringTests.ChangelogDatabase_IsNotAnInMemoryDatabase` /
> `.ChangelogDatabase_IsAFileNamedAlongsideTheMainDatabase` assert the real DI registration is not an
> in-memory connection string. A file does not evaporate; an in-memory database is caught before it
> ships.

## Steps

### 1. Start a container of this test's own on a bind-mounted directory

```powershell
$dataDir = "$PWD\.claude\temp\qt-notif-07-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-notif-07 --port 18507 --bind $dataDir
```

**Expected:** the app reaches healthy, having initialised and imported the changelog during startup.

### 2. Confirm the changelog file exists alongside `quotinatordata.db`

**The file must exist alongside `quotinatordata.db`** — an in-memory database leaves nothing on disk:

```powershell
Get-ChildItem $dataDir -Filter *.db | Select-Object Name, Length
```

**Expected:** both `quotinatordata.db` and `quotinatordata`'s changelog sibling
`quotinatorchangelog.db` are listed, each with a non-zero length. Read from the host directory rather
than from inside the container, because the bind mount is what makes the file's existence on real
storage the thing being observed.

### 3. Confirm the database-backed read path is in use, not the fallback

```powershell
$log = docker logs qt-notif-07 2>&1 | Out-String
$log -split "`n" | Select-String -Pattern 'Changelog - (Init|Import|Read)'
"fallbacks=$(([regex]::Matches($log, 'falling back to the JSON-backed changelog service')).Count)"
```

**Expected:** `[Changelog - Import] refreshed N entries across 3 language(s)` appears, and so does
`[Changelog - Read] served N entries from the database` — the positive statement that the database
itself answered. **The two counts match each other**; the value itself is data. The three languages
are asserted, because that is the shipped set rather than a content count.

`fallbacks=0` — no `falling back to the JSON-backed changelog service` line appears at any point. That
is the weaker half of the assertion and is why the positive line above is read first: an absent warning
is produced identically by a healthy read and by the silent fallback this test exists to catch.

### 4. Confirm a real page request is served from the database

```powershell
$before = ([regex]::Matches((docker logs qt-notif-07 2>&1 | Out-String), 'entries from the database')).Count

$about = dotnet script scripts/testing/http.csx -- --url "http://localhost:18507/about" --expect 200 | Out-String
"changelogEntries=$(([regex]::Matches($about, 'changelog-entry')).Count)"

$after = ([regex]::Matches((docker logs qt-notif-07 2>&1 | Out-String), 'entries from the database')).Count
"served before=$before after=$after increased=$($after -gt $before)"
"fallbacks=$(([regex]::Matches((docker logs qt-notif-07 2>&1 | Out-String), 'falling back to the JSON-backed changelog service')).Count)"
```

**Expected:** `/about` returns `200`, `changelogEntries` is non-zero, `increased=True`, and
`fallbacks=0`. **There is deliberately no REST endpoint here** — changelog content is surfaced only on
the About page (`Components/Pages/About.razor`), so that is what must be read. The count of
`entries from the database` grew as a result of that request, which is what ties the page render to the
database rather than to the fallback.

### 5. Confirm the file survives a restart with its content intact

**The file must survive a restart with its content intact** — it is rebuilt from the bundled JSON at
every startup, so this confirms the rebuild is idempotent rather than duplicating rows:

```powershell
docker restart qt-notif-07
dotnet script scripts/testing/http.csx -- --url "http://localhost:18507/api/v1/health" --wait-for 200 --status

Get-ChildItem $dataDir -Filter quotinatorchangelog.db | Select-Object Name, Length
docker logs qt-notif-07 2>&1 | Select-String -Pattern 'Changelog - (Init|Import)' | Select-Object -Last 4
```

**Expected:** after restart, the file is still present and the import reports the same entry count as
step 3 did — no duplication.

## Observed effect

Well established, and the failure is the instructive part. Thirteen minutes after a clean import of 126
entries, every read failed with `no such table: Changelog_Entry` and fell back to the JSON service
permanently, with no process restart in between — the changelog database was a shared-cache in-memory
instance held open by a keep-alive connection.

**Nothing was user-visible, because the JSON fallback works exactly as designed.** That is why it went
unnoticed, and why this test asserts a positive statement rather than an absent warning.

Neither Reset nor the pre-migration backup touches this file (developer decision, 2026-08-18): its
contents are wholly derived from JSON shipped in the image, so nothing user-authored is ever at risk.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-07 --bind $dataDir
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
```
