# Upgrading translates the notification a released build already wrote

**Smoke:** no
**Environment:** Upgraded
**Deterministic:** yes
**Traces to:** #319

## Preconditions

Beyond the profile: the prior side is the **published `ghcr.io/dutchjafo/quotinator:1.8.3` tag**, not
the milestone base image. That version wrote the operation-id-rename announcement in English with no
translation table to put anything else in, which is the state this upgrade has to repair: the base
image already has the repair in it and would prove nothing.

One container name, `qt-notif-09`, publishing `18509`, over one bind directory the document creates and
removes: first the released image, then the current build.

## Determinism

- **The subject is one notification identified by a known cause**: the announcement, selected by
  `metadataKind = announcement`. Nothing counts notifications: how many exist depends on which
  producers ran and what the changelog flags for the running version.
- **The upgrade must be the thing that adds the translations**, so step 2 confirms the released
  database has none before the current build touches it. Without that, a pass cannot distinguish the
  backfill working from the translations having been there all along.
- **The original English text must survive unchanged.** Each producer's content hash is taken over it,
  so a backfill that rewrote it would leave the notification unrecognisable to the dedupe and it would
  re-announce, a failure that appears one boot later, which is why step 5 restarts.
- **Each translated title is compared with the translation file it comes from** (`--equals-json` against
  `UI.nl.json` and `UI.de.json`), so the document holds no copy of the text that could drift from it.
- **Every count is scoped to the announcement, never taken over a whole table.** Other producers write
  their own notifications and their own translations, so an unscoped count answers a different question
  than the one being asked and grows whenever a producer is added.
- **Column names differ across the upgrade.** v1.8.3 stores the text in `Message`; #312's rename makes
  it `Body`. Step 2 uses the first, step 6 the second.
- **The database file is read only with no container running**, so nothing holds it and no `-wal` holds
  part of it.
- **Never assert a migration number or schema version.** Assert the translations are present and the
  original is unchanged; both survive a migration consolidation, a version number does not.
- **The log is read only for exceptions, before each stop.**

## Steps

### 1. Create the released baseline

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-09 --delete C:\repos\Quotinator\.claude\temp\qt-notif-09-out --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-09 --create-directory C:\repos\Quotinator\.claude\temp\qt-notif-09-out
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-09 --port 18509 --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind C:\repos\Quotinator\.claude\temp\qt-notif-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-09 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-09 --bind C:\repos\Quotinator\.claude\temp\qt-notif-09
```

**Expected:** the released image reports healthy, nothing is thrown before the stop, and the container is
removed, leaving its database in the bind directory.

### 2. Confirm the released database has the announcement and no translations

```
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-notif-09\quotinatordata.db --sql "SELECT COUNT(*) AS Announcements FROM System_Notification WHERE Message LIKE '%GetAllImportBatches%'" --json-out C:\repos\Quotinator\.claude\temp\qt-notif-09-out\released-announcements.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-09-out\released-announcements.json --equals 0.Announcements=1
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-notif-09\quotinatordata.db --sql "SELECT COUNT(*) AS TranslationTable FROM sqlite_master WHERE type = 'table' AND name = 'System_NotificationTranslation'" --json-out C:\repos\Quotinator\.claude\temp\qt-notif-09-out\released-translation-table.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-09-out\released-translation-table.json --equals 0.TranslationTable=0
```

**Expected:** both `json` commands exit `0`: one announcement, and no translation table at all in the
released schema.

**The column is `Message` here, not `Body`.** v1.8.3 predates the rename, so a query naming `Body` fails
with `no such column`, which reads as a broken step rather than as the schema difference it is.

**On failure:** `0` announcements means there is nothing for the upgrade to translate, and every check
below would pass by doing no work. A translation table already present means the prior image is not the
released tag this test needs. Stop either way.

### 3. Upgrade to the current build

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-09 --port 18509 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-notif-09
dotnet script scripts/testing/http.csx -- --url http://localhost:18509/api/v1/health --expect 200 --status
```

**Expected:** the current build reports healthy, and `/health` answers `200`.

**On failure:** anything but `200` means the upgrade did not complete, and the remaining steps would read
an unrelated state. Stop.

### 4. Confirm the announcement resolves in every language

```
dotnet script scripts/testing/http.csx -- --url "http://localhost:18509/api/v1/notifications?pageSize=0&lang=en" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-09-out\notifications-en.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-09-out\notifications-en.json --where items:metadataKind=announcement --length-equals items=1 --equals items.0.language=en --equals items.0.isTranslated=false --equals-json items.0.title=src/Quotinator.Api/i18ntext/UI.en-GB.json#NotificationOperationIdRenameTitle
dotnet script scripts/testing/http.csx -- --url "http://localhost:18509/api/v1/notifications?pageSize=0&lang=nl" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-09-out\notifications-nl.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-09-out\notifications-nl.json --where items:metadataKind=announcement --length-equals items=1 --equals items.0.language=nl --equals items.0.isTranslated=true --equals-json items.0.title=src/Quotinator.Api/i18ntext/UI.nl.json#NotificationOperationIdRenameTitle
dotnet script scripts/testing/http.csx -- --url "http://localhost:18509/api/v1/notifications?pageSize=0&lang=de" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-09-out\notifications-de.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-09-out\notifications-de.json --where items:metadataKind=announcement --length-equals items=1 --equals items.0.language=de --equals items.0.isTranslated=true --equals-json items.0.title=src/Quotinator.Api/i18ntext/UI.de.json#NotificationOperationIdRenameTitle
```

**Expected:** every `json` command exits `0`: one announcement in each language; in English it is the
original, untranslated; in Dutch and German it is translated, and its title is exactly that language's
translation file entry.

**On failure:** a Dutch or German title equal to the English, with `isTranslated=true`, means no translation
row was written and each read fell through to the original while still echoing the requested language: the
backfill did not match the announcement row.

### 5. Restart, and confirm the second boot does not announce again

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-09 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-09 --port 18509 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-notif-09
dotnet script scripts/testing/http.csx -- --url "http://localhost:18509/api/v1/notifications?pageSize=0&lang=en" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-notif-09-out\notifications-after-restart.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-09-out\notifications-after-restart.json --where items:metadataKind=announcement --length-equals items=1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-notif-09 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-09 --bind C:\repos\Quotinator\.claude\temp\qt-notif-09
```

**Expected:** nothing thrown before either stop, the restarted build reports healthy, and the `json` command
exits `0`: still exactly one announcement.

**On failure:** a second announcement means the stored English no longer matches what the producer's content
hash covers, so the dedupe stopped recognising it.

### 6. Confirm the English text was kept and each translation written once

```
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-notif-09\quotinatordata.db --sql "SELECT COUNT(*) AS Announcements FROM System_Notification WHERE Body LIKE '%GetAllImportBatches%'" --json-out C:\repos\Quotinator\.claude\temp\qt-notif-09-out\upgraded-announcements.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-09-out\upgraded-announcements.json --equals 0.Announcements=1
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-notif-09\quotinatordata.db --sql "SELECT COUNT(*) AS Translations, COUNT(DISTINCT t.Language) AS Languages FROM System_NotificationTranslation t JOIN System_Notification n ON LOWER(n.Id) = LOWER(t.NotificationId) WHERE n.MetadataKind = 'Announcement'" --json-out C:\repos\Quotinator\.claude\temp\qt-notif-09-out\upgraded-translations.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-notif-09-out\upgraded-translations.json --equals 0.Translations=2 --equals 0.Languages=2
```

**Expected:** both `json` commands exit `0`: the original row still carries the text the released build
wrote, and the announcement has two translations, one per translated language, after two boots.

**The count is scoped to the announcement by joining its notification.** Other producers write
translations of their own (the what's-new notification has a row per language too), so an unscoped count
over the whole table would read as the backfill having duplicated.

**On failure:** `Announcements = 0` means the backfill moved or rewrote the original text. `Translations`
above `Languages` means the backfill appended on the second boot instead of skipping what it had written.

## Observed effect

A user upgrading from v1.8.3 keeps the notification that release wrote, in the words it wrote, and gains
Dutch and German versions of it without the notification reappearing as new. Reading the Notifications
page in Dutch shows the Dutch text; reading it in English shows exactly what was there before the
upgrade. A database that never ran v1.8.3 gains nothing, because the backfill matches no row there.

## Canary

Build the commit before [#319](https://github.com/DutchJaFO/Quotinator/issues/319)'s first code
change, under a distinct tag, and run steps 3 to 6 against it. Translating a notification a released
build already wrote is what #319 added, so step 4's Dutch and German `json` commands fail on
`isTranslated` and `title`: each returns the English text.

**Step 2 is the control and is read before the upgrade**: the released database has the announcement
and no translations. Without it, step 4 succeeding would not establish that the upgrade did anything,
since a database that already had translations would answer the same way.

**Step 6 is a guard against the obvious wrong fix.** Translating the row must not move or rewrite its
English text, so a build that achieved step 4 by replacing the original fails here. Step 5 is the same
shape for a second boot, which must not announce again.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-09 --bind C:\repos\Quotinator\.claude\temp\qt-notif-09
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-notif-09 --delete C:\repos\Quotinator\.claude\temp\qt-notif-09-out
```

**Expected:** the container and both directories are gone.
