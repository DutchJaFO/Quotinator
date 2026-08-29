# Upgrading translates the notification a released build already wrote

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #319

## Preconditions

**Two containers over one bind-mounted directory**, the same shape
[`02-notification-metadata-and-provenance.md`](02-notification-metadata-and-provenance.md) uses:
`qt-notif-09-183` runs the published `ghcr.io/dutchjafo/quotinator:1.8.3` tag with **no published
port**, then `qt-notif-09-current` runs the current build over the database it left behind.

**v1.8.3's operation-id-rename announcement is the only notification any released build has
persisted**, which is what makes this testable at all: the upgrade has exactly one row to translate,
and a fresh install has none. A fresh container therefore cannot exercise this path — that is why this
is an Upgraded scenario rather than a step inside
[`08-notification-text-resolves-per-language.md`](08-notification-text-resolves-per-language.md).

**The original English text must survive the upgrade untouched.** Each producer's content hash is taken
over that text, so a backfill that moved or rewrote it would make the notification re-announce itself on
the next start — a failure that only shows up one boot later, which is why step 5 restarts.

## Determinism

- The released container publishes no port and waits on its own log, so nothing races the current
  build for the database file.
- The current build's wait terminates on **either** ready or unhandled exception, so a crash fails the
  test rather than hanging it.
- **Never assert a migration number or schema version.** Assert that the translations are present and
  that the original is unchanged — both survive consolidation, a version number does not.
- The bind directory is an absolute Windows path built from `$PWD`, so nothing translates it into a
  different directory on the way to the container.

## Steps

### 1. Create the released baseline

```powershell
$dataDir = "$PWD\.claude\temp\qt-notif-09-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-notif-09-183 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir
while (-not (docker logs qt-notif-09-183 2>&1 | Select-String -SimpleMatch 'Quotinator ready')) { Start-Sleep 1 }
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-09-183 --bind $dataDir
```

**Expected:** the released image reaches `Quotinator ready`, leaving a v1.8.3 database in `$dataDir`.

### 2. Confirm the announcement is there, and English-only

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) FROM System_Notification WHERE Body LIKE '%GetAllImportBatches%'"
```

**Expected:** `1`.

**On failure:** `0` means the released image did not write the announcement, so there is nothing for the
upgrade to translate and a green result below would mean only that no work was attempted. Stop —
this is the precondition the whole scenario rests on.

### 3. Upgrade to the current build

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-09-current --port 18509 `
  --image quotinator:local --bind $dataDir --no-wait

while (-not (docker logs qt-notif-09-current 2>&1 | Select-String -SimpleMatch 'Quotinator ready', 'Unhandled exception')) { Start-Sleep 1 }
docker logs qt-notif-09-current 2>&1 | Select-String -SimpleMatch 'pending', 'schema updated', 'Quotinator ready', 'Unhandled'
```

**Expected:** logs pending `Data` migrations, then `schema updated`, then `Quotinator ready`.

**Must not log `Unhandled exception`.**

### 4. Read the announcement back in each language

```powershell
foreach ($l in 'en','nl','de') {
  $n = (Invoke-RestMethod "http://localhost:18509/api/v1/notifications?lang=$l").items |
       Where-Object { $_.metadataKind -eq 'announcement' } | Select-Object -First 1
  "{0}: language={1} isTranslated={2} :: {3}" -f $l, $n.language, $n.isTranslated, $n.title
}
```

**Expected:** three lines. `en` reports `language=en isTranslated=False`; `nl` and `de` each report
their own language with `isTranslated=True`, and each prints a **different** title from the English one
and from each other.

**Three identical titles is a failure even if every `language` field looks right** — it means the
backfill wrote no translation rows and each read fell through to the original while the `CASE` still
echoed the requested language. Comparing the titles is the only observation here that distinguishes a
translated row from a labelled fallback.

**On failure:** if `nl` and `de` report `isTranslated=False`, the backfill did not match the
announcement row. Re-run step 2 against the upgraded database — if the row is still there, the
migration's matching predicate is at fault (cause 1), not the read path.

### 5. Confirm the original text did not move, and does not re-announce

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) FROM System_Notification WHERE Body LIKE '%GetAllImportBatches%'"

docker restart qt-notif-09-current | Out-Null
while (-not (docker logs qt-notif-09-current 2>&1 | Select-String -SimpleMatch 'Quotinator ready', 'Unhandled exception')) { Start-Sleep 1 }

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT COUNT(*) FROM System_Notification WHERE Body LIKE '%GetAllImportBatches%'"
```

**Expected:** `1` both times.

**A second row after the restart is the failure this step exists for.** It means the English body no
longer matches what the producer's content hash was taken over, so the dedupe no longer recognises the
stored notification as the same one — the announcement would reappear on every upgrade. The count
before the restart cannot reveal this on its own, which is why the restart is part of the step rather
than a separate scenario.

### 6. Confirm the backfill is idempotent

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$dataDir\quotinatordata.db" `
  --sql "SELECT Language, COUNT(*) FROM System_NotificationTranslation GROUP BY Language"
```

**Expected:** one row per translated language, each with a count of exactly `1`, after a start and a
restart have both run the migration path.

**On failure:** a count above `1` means the backfill appended instead of skipping what it had already
written.

### 7. Tear down

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-09-current --bind $dataDir
```

**Expected:** the container is removed and the command reports no error.
