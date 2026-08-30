# Upgrading translates the notification a released build already wrote

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #319

## Preconditions

Beyond the profile: the prior side is the **published `ghcr.io/dutchjafo/quotinator:1.8.3` tag**, not
the milestone base image. That version wrote the operation-id-rename announcement in English with no
translation table to put anything else in, which is the state this upgrade has to repair — the base
image already has the repair in it and would prove nothing.

Two containers over one bind-mounted directory the document creates and removes: `qt-notif-09-183`
publishes no port and is waited on by its own log, then `qt-notif-09-current` publishes `18509`.

## Determinism

- **The subject is one notification identified by a known cause** — the announcement, selected by
  `metadataKind -eq 'announcement'`. Nothing counts notifications: how many exist depends on which
  producers ran and what the changelog flags for the running version.
- **The upgrade must be the thing that adds the translations**, so step 2 confirms the released
  database has none before the current build touches it. Without that, a pass cannot distinguish the
  backfill working from the translations having been there all along.
- **The original English text must survive unchanged.** Each producer's content hash is taken over it,
  so a backfill that rewrote it would leave the notification unrecognisable to the dedupe and it would
  re-announce — a failure that appears one boot later, which is why step 6 restarts.
- **Every count is scoped to the announcement, never taken over a whole table.** Other producers write
  their own notifications and their own translations, so an unscoped count answers a different question
  than the one being asked and grows whenever a producer is added.
- **Column names differ across the upgrade.** v1.8.3 stores the text in `Message`; #312's rename makes
  it `Body`. Steps before the upgrade use the first, steps after it use the second.
- **Never assert a migration number or schema version.** Assert the translations are present and the
  original is unchanged; both survive a migration consolidation, a version number does not.
- The bind directory is a PowerShell absolute path, so nothing translates it into a different directory
  on the way to the container.
- Waits are on a condition, never a duration: the released container on its own log, the current build
  on a health poll that gives up rather than hanging.

## Steps

### 1. Create the released baseline

```powershell
$dataDir = "$env:TEMP\qt-notif-09-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-09-183 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir
while (-not (docker logs qt-notif-09-183 2>&1 | Select-String -SimpleMatch 'Quotinator ready')) { Start-Sleep 1 }
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-09-183 --bind $dataDir
```

**Expected:** the released image reaches `Quotinator ready` and the container is removed, leaving its
database in `$dataDir`.

### 2. Confirm the released database has the announcement and no translations

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) AS Announcements FROM System_Notification WHERE Message LIKE '%GetAllImportBatches%'"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) AS TranslationTable FROM sqlite_master WHERE type='table' AND name='System_NotificationTranslation'"
```

**Expected:** `Announcements` is `1`, and `TranslationTable` is `0` — the released schema has no
translation table at all.

**The column is `Message` here, not `Body`.** v1.8.3 predates the rename, so a query naming `Body` fails
with `no such column` — which reads as a broken step rather than as the schema difference it is. Steps
after the upgrade use `Body`, because by then the rename has run.

**On failure:** `0` announcements means there is nothing for the upgrade to translate, and every check
below would pass by doing no work. A translation table already present means the prior image is not the
released tag this test needs. Stop either way.

### 3. Upgrade to the current build

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-09-current --port 18509 `
  --image quotinator:local --bind $dataDir
dotnet script scripts/testing/http.csx -- --url "http://localhost:18509/api/v1/health" --wait-for 200 --status
docker logs qt-notif-09-current 2>&1 | Select-String -SimpleMatch 'Unhandled exception'
```

**Expected:** `200`, and the log search returns nothing.

**On failure:** any `Unhandled exception` means the migration threw rather than applying; the database
is left as it was and the remaining steps would report an unrelated state. Stop.

### 4. The announcement resolves in every language

```powershell
$base = "http://localhost:18509/api/v1"
function Get-Announcement($lang) {
  @((Invoke-RestMethod "$base/notifications?pageSize=0&lang=$lang").items |
    Where-Object { $_.metadataKind -eq 'announcement' })[0]
}
$en = Get-Announcement 'en'; $nl = Get-Announcement 'nl'; $de = Get-Announcement 'de'
"english=$($en.language -eq 'en' -and $en.isTranslated -eq $false) " +
"dutch=$($nl.language -eq 'nl' -and $nl.isTranslated -eq $true) " +
"german=$($de.language -eq 'de' -and $de.isTranslated -eq $true) " +
"allDiffer=$(@($en.title, $nl.title, $de.title | Select-Object -Unique).Count -eq 3)"
```

**Expected:** every value `True`.

**On failure:** `allDiffer=False` with the language flags `True` means no translation rows were written
and each read fell through to the original while still echoing the requested language — the backfill did
not match the announcement row.

### 5. The English text was not moved or rewritten

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) AS Announcements FROM System_Notification WHERE Body LIKE '%GetAllImportBatches%'"
```

**Expected:** `1` — the original row, still carrying the text the released build wrote.

### 6. A second boot does not re-announce

```powershell
docker restart qt-notif-09-current | Out-Null
dotnet script scripts/testing/http.csx -- --url "http://localhost:18509/api/v1/health" --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) AS Announcements FROM System_Notification WHERE Body LIKE '%GetAllImportBatches%'"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT t.Language, COUNT(*) AS Cnt FROM System_NotificationTranslation t JOIN System_Notification n ON LOWER(n.Id)=LOWER(t.NotificationId) WHERE n.MetadataKind = 'Announcement' GROUP BY t.Language"
```

**Expected:** `200`, `Announcements` still `1`, and one row per translated language each with `Cnt` = `1`.

**The count is scoped to the announcement by joining its notification.** Other producers write
translations of their own — the what's-new notification has a row per language too — so an unscoped
`GROUP BY Language` over the whole table returns 2 per language on a correct run and reads as the
backfill having duplicated. Measured: it does exactly that.

**On failure:** a second announcement means the stored English no longer matches what the producer's
content hash covers, so the dedupe stopped recognising it. A `Cnt` above `1` means the backfill appended
instead of skipping what it had already written.

## Observed effect

A user upgrading from v1.8.3 keeps the notification that release wrote, in the words it wrote, and gains
Dutch and German versions of it without the notification reappearing as new. Reading the Notifications
page in Dutch shows the Dutch text; reading it in English shows exactly what was there before the
upgrade. A database that never ran v1.8.3 gains nothing, because the backfill matches no row there.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-09-current --bind $dataDir
Remove-Item -Recurse -Force $dataDir
```
