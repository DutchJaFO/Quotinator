# Backups continue from the reserve, with a warning, until the ceiling refuses one

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #348

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-backup-08`, publishing `18388`, on a bind
directory, so the host can write and remove a sparse filler file inside the `backups/` folder the
container is measuring.

The pressure is established in two distinct bands, and every assertion below depends on which one is in
force:

- **The reserve**, between the operating quota and the ceiling. A backup here is still taken, and the
  warning is raised.
- **Past the ceiling**, where a backup is refused.

Step 3 confirms the reserve was actually reached rather than inferring it from the filler having been
written, and step 9 does the same for the ceiling.

## Determinism

- **`MaxBackupStorageGb` stays at its default 1 GB**, so the filler is sized from that. A smaller
  ceiling cannot be configured, since the setting is whole gigabytes.
- **`BackupQuotaPercent` stays at its default 90**, so the reserve is the band from 90% to 100% of the
  ceiling. Filling to 92% (`987842478` bytes) lands inside it: above the quota, below the ceiling.
- **A backup must fit in what is left below the ceiling, or step 4 would be measuring a refusal rather
  than a backup.** This is not assumed: step 2 takes a backup and saves what it actually cost, and step 3
  checks that `remainingAgainstCeilingBytes` still exceeds it (`--greater-than-json`). Both figures come
  from the run, so neither is a prediction about the database's size.
- **One filler file at a time, sparse.** A sparse file allocates without writing the bytes, so a step is
  fast and does not depend on the host's write throughput.
- **The notification list has no filter, so each reading narrows it with `--where`.** A warning is
  counted by `metadataKind` and with `isDismissed=false`, since the endpoint returns the full history and
  a resolved warning is still listed. Every count of `0` has a reading of the same filter, elsewhere in
  this document, that finds a row.
- **The warning is one row however often its condition is checked**, so every count below is of open
  rows, never of checks performed.
- **The log is read before each stop.**

## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-08 --delete C:\repos\Quotinator\.claude\temp\qt-backup-08-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-08\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-08-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-08 --port 18388 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-08
```

**Expected:** the environment reports healthy.

### 2. Positive control: below the quota, a backup is taken and nothing is warned about

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18388/api/v1/admin/backups/status --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\status-fresh.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\status-fresh.json --equals canBackUp=true --equals storage.reserveInUse=false --equals storage.usedBytes=0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/admin/backups/create --expect 201 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\created-first.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\created-first.json --not-empty name --greater-than sizeBytes=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-fresh.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-fresh.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=0
```

**Expected:** every command exits `0`: the folder is empty and a backup can be taken, one is taken at a
non-zero cost, and no quota warning is open.

**This is the positive control for the whole document.** Everything below asserts that a warning is
raised, and a build that raised it unconditionally would satisfy all of it without this step. Step 4's
reading of the same filter finds the row, which is what makes this `0` evidence.

**On failure:** a warning already open here, or a refused create, means the environment is not clean and
nothing below is measuring the reserve.

### 3. Fill into the reserve, and confirm that is the band reached

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --sparse C:\repos\Quotinator\.claude\temp\qt-backup-08\backups\filler.db 987842478 --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-08\backups\filler.db 987842478
dotnet script scripts/testing/http.csx -- --url http://localhost:18388/api/v1/admin/backups/status --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\status-reserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\status-reserve.json --equals storage.reserveInUse=true --equals canBackUp=true --greater-than-json storage.remainingAgainstCeilingBytes=C:\repos\Quotinator\.claude\temp\qt-backup-08-out\created-first.json#sizeBytes
```

**Expected:** every command exits `0`: the quota has been passed and the ceiling has not, and what is left
below the ceiling exceeds what step 2's backup cost.

**On failure:** `canBackUp=false` has two causes, and the room below the ceiling tells them apart. With
too little room the fill overshot the ceiling, so steps 4 to 8 would be measuring refusals rather than the
reserve. With room to spare the build is refusing at the *quota*: the pre-#348 model, where the reserve
could not be used without the caller opting in. Either way, stop: the band below is not the one being
measured.

### 4. A backup is still taken from the reserve, and raises the warning

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/admin/backups/create --expect 201 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\created-reserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\created-reserve.json --not-empty name --greater-than sizeBytes=0
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-reserve.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-reserve.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=1 --equals items.0.type=warning --absent items.0.resolution --contains "items.0.body=The backups folder holds" --contains "items.0.body=past its operating quota of" --contains "items.0.body=from the reserve below the ceiling of 1.00 GB"
```

**Expected:** every command exits `0`: a backup is created with a non-zero size, and exactly one open
`warning`, carrying no `resolution`, whose body names three sizes: what the folder holds, the quota it
has passed, and the ceiling it has not.

**Open is asserted on two fields, not one.** `--where items:isDismissed=false` only chooses which rows
are counted; `--absent items.0.resolution` asserts the row has not been resolved. A resolved row carries
the field, as step 7 reads (`underquota`), so its absence here is a reading and not a field the API never
sends. The field is omitted rather than sent as `null`, which is why `--absent` and not `--equals ...=null`.

**Both halves matter and neither implies the other.** A build that refused the backup would fail the
first `json`; a build that took it silently would fail the second.

**On failure:** a `409` on the create is the pre-#348 model, where the quota refused rather than warned.

### 5. The warning survives a restart, and the startup check does not duplicate it

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-08 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-08 --port 18388 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-08
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-restarted.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-restarted.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=1 --absent items.0.resolution
```

**Expected:** nothing thrown before the stop, and exactly one open warning after the restart.

**Two failures are distinguishable here, which is why the count is reported rather than a boolean.** `0`
means the startup check did not run or did not raise; `2` means it raised a second warning for a
condition already open, which is what an identity built from the bytes in use would do, since the folder
grew by a backup in step 4.

### 6. A Reset from the reserve is not refused, and what it offers carries the caution

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-reset.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=1 --absent items.0.resolution
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-reset.json --where items:isDismissed=false --where items:availableActions.0 --present items.0 --equals items.0.backupCaution=true --contains items.0.availableActions=backupthenreseed
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-reset.json --where items:isDismissed=false --where items:availableActions.0 --where items:backupCaution=false --length-equals items=0
```

**Expected:** every command exits `0`: the reset answers `200`, one quota warning is open, at least one
open row offers an action, the first of them carries the caution and offers `backupthenreseed`, and no
open row offering an action lacks the caution.

**The warning count is the interesting one.** A Reset replaces the whole database, this row included, so a `1`
here is the post-reset condition check having raised it again rather than the original row surviving.

**The last command's `0` is evidence because the one before it reads the same filter, less the caution,
and finds a row.**

**On failure:** a `409` means the reserve is refusing a Reset, which is the model #348 replaced.

### 6b. The page shows the caution beside the option that takes the backup

```
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18388/notifications --out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\reserve-caution.png --height 1100 --script-file scripts/testing/page-scripts/reserve-caution.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\reserve-caution.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\reserve-caution.json --greater-than innerHeight=0 --equals cautioned=1 --contains "buttons=Back up, then reseed"
```

**Expected:** the `json` command exits `0`: one row carries the caution, and its buttons include *Back up,
then reseed*, so the caution is rendered beside the choice it qualifies rather than only carried over
REST. `reserve-caution.png` is the artefact to look at.

**On failure:** `cautioned=0` with step 6 passing means the response and the page disagree, which is the
specific drift `NotificationActionOptions.TakesABackup` exists to prevent.

### 7. Removing a backup through the application clears the warning, with no restart

```
dotnet script scripts/testing/http.csx -- --method DELETE --url http://localhost:18388/api/v1/admin/backups/filler.db --expect 204 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18388/api/v1/admin/backups/status --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\status-deleted.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\status-deleted.json --equals storage.reserveInUse=false
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-deleted.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-deleted.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-deleted.json --where items:metadataKind=backupquotareached --where items:isDismissed=true --present items.0 --equals items.0.resolution=underquota
```

**Expected:** every command exits `0`: the filler is deleted through the API, the reserve is no longer in
use, no quota warning is open, and the newest resolved one is resolved as `underquota`.

**The container was not restarted between the deletion and the read**, which is the point: the deletion
itself re-checked the condition.

### 8. A removal the application never saw is picked up on request, still with no restart

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --sparse C:\repos\Quotinator\.claude\temp\qt-backup-08\backups\filler.db 987842478
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/notifications/refresh --no-key --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\refresh-raised.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\refresh-raised.json --where checks:kind=backupquotareached --equals checks.0.outcome=raised
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-raised.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-raised.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=1 --absent items.0.resolution
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-08\backups\filler.db
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/notifications/refresh --no-key --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\refresh-cleared.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\refresh-cleared.json --where checks:kind=backupquotareached --equals checks.0.outcome=cleared
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-cleared.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-cleared.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=0
```

**Expected:** every command exits `0`: the first refresh reports `raised` and one warning is open, the
second reports `cleared` and none is.

**Neither refresh carries an admin key** (`--no-key`), which is deliberate: the endpoint is open to every
user.

**This is the case a restart would otherwise be needed for.** The file was written and removed on the
host, so no application path observed either; nothing but the refresh could have noticed.

**On failure:** an `unchanged` on the first refresh means the fill did not reach the quota; on the second,
it means the check is reading a cached figure rather than the folder.

### 9. Past the ceiling, a backup is refused

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --sparse C:\repos\Quotinator\.claude\temp\qt-backup-08\backups\filler.db 1073741824
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/admin/backups/create --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\create-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\create-refused.json --equals backupObstacle=BudgetExceeded
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/notifications/refresh --no-key --expect 200 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-ceiling.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-ceiling.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-ceiling.json --where items:metadataKind=backupmaxexceeded --where items:isDismissed=false --length-equals items=1 --equals items.0.type=error --absent items.0.resolution --contains "items.0.body=The backups folder holds" --contains "items.0.body=at or above its maximum of 1.00 GB"
```

**Expected:** every command exits `0`: the create is refused `409` with `BudgetExceeded`, no quota warning
is open, and exactly one open `error` names what the folder holds and the maximum it has passed.

**Three separate facts, and none implies the others.** The refusal is the attempt being stopped. The
quota warning's `0` is the reserve warning gone, since it says backups are still being taken and that is
false here. The error's `1` is the band reporting: exceeding the maximum always reports, whether or not
anything attempted a backup.

**On failure:** a `201` on the create means nothing enforces the ceiling and the reserve has become
unbounded. An open quota warning means the warning is kept outside the band it describes. No error means
the state that stops every backup is showing the operator nothing.

### 10. The remedy the warning names actually works

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-08\backups\filler.db
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/notifications/refresh --no-key --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18388/api/v1/admin/backups/status --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\status-remedied.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\status-remedied.json --equals canBackUp=true
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18388/api/v1/admin/backups/create --expect 201 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\created-final.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\created-final.json --not-empty name
dotnet script scripts/testing/http.csx -- --url "http://localhost:18388/api/v1/notifications?pageSize=0" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-remedied.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-remedied.json --where items:metadataKind=backupmaxexceeded --where items:isDismissed=false --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-08-out\notifications-remedied.json --where items:metadataKind=backupquotareached --where items:isDismissed=false --length-equals items=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-08 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: a backup can be taken again and one is, neither the error nor the
quota warning is open, and nothing was thrown.

**This closes the loop the warning promises.** Its body tells the operator to delete older backups or
raise the quota, and says the notice clears itself once the folder is back under. This step performs the
first of those and checks both halves: the notices are gone and a backup is possible again. The error's
`0` is evidence because step 9 read the same filter and found it.

## Observed effect

**Measured 2026-11-08** on `feature/notification-system` (1.9.0-alpha), as written, every command
exiting `0`:

| Step | Observed |
|---|---|
| 2 | `canBackUp = true`, `reserveInUse = false`, `usedBytes = 0`; a backup of `4718592` bytes; `0` open quota warnings |
| 3 | `reserveInUse = true` and `canBackUp = true`, `81180754` bytes left below the ceiling |
| 4 | a second backup of `4718592` bytes; `1` open `warning`, `resolution` absent, body *The backups folder holds 951.1 MB, past its operating quota of 921.6 MB. Backups are still being taken, from the reserve below the ceiling of 1.00 GB, …* |
| 5 | `0` exceptions; `1` open warning after the restart, `resolution` absent |
| 6 | reset `200`; `1` open warning, `resolution` absent; the reseed row *The database holds no quotes* with `backupCaution = true` and `["backupthenreseed"]`; `0` rows offering an action without the caution |
| 6b | `innerHeight = 1100`, `cautioned = 1`, buttons *Back up, then reseed* and *Dismiss* |
| 7 | `reserveInUse = false`; `0` open, the newest resolved one `underquota` |
| 8 | refresh `raised`, `1` open, `resolution` absent; refresh `cleared`, `0` open |
| 9 | `409 BudgetExceeded`; `0` open quota warnings; `1` open `error`, `resolution` absent |
| 10 | `canBackUp = true`, a backup taken; `0` open errors, `0` open quota warnings; `0` exceptions |

**The conversion corrected one assertion of its own.** Its first run asserted the open warning's
`resolution` as `null` and failed: the response omits the field rather than sending it as `null`. The
assertion was then wrongly dropped, on the reasoning that `isDismissed` already showed the row open; but
that filter only selects rows and asserts nothing about them, so the document no longer checked that an
open notice is unresolved. TestProbe gained `--absent` for it, and every open warning and error the
document reads now asserts `resolution` absent, against step 7's resolved row that carries one.

**Measured 2026-09-27** against `quotinator:local`, and first run red against an image built from
`7f83e92a`, the model #348 replaced.

Both bands behave as the two-level model intends. A fresh install reports `canBackUp=True` with `used=0`
and writes a real backup of roughly 4.7 MB. Filling to 92% of the 1 GB ceiling reports `reserveInUse=True`
*and* `canBackUp=True` together, with about 81 MB still below the ceiling, and a backup is taken from that
band rather than refused. It raises one `Warning` row titled *Backups have reached their quota*, whose
body names what the folder holds, the quota it has passed and the ceiling it has not, and which links the
`backups-have-reached-their-quota` Knowledgebase entry. A restart leaves it at exactly one, although the
folder grew by a backup first. A Reset answers `200` from the reserve and, because it drops the row with
every other table, the post-reset check raises it again; the reseed row it writes offers
`backupthenreseed` with `backupCaution=true`, and the page renders the caution beneath that button. An
API deletion clears the warning as `underquota` with no restart, and a fill and a removal performed on the
host are picked up by `POST /notifications/refresh`, which needs no admin key. At the full ceiling `create`
answers `409 BudgetExceeded`, and removing the filler restores `canBackUp=True` and a working backup.

**The red run inverted all four of the observations this document turns on:** step 3 reported
`canBackUp=False` while `fits=True` and 81 MB remained below the ceiling, step 4's backup was refused
`409 BudgetExceeded`, no `backupquotareached` row existed at all, and `POST /notifications/refresh`
answered `404`. Step 3's *On failure* text was wrong before that run and is corrected: it blamed
`canBackUp=False` on overshooting the ceiling alone, which `fits=True` disproves.

**This pass found one defect, and step 9's quota-warning count of `0` is the assertion added for it.** On
the day of the run, past the *ceiling* the check raised the same body it raises inside the reserve: at
1.02 GB against a 1.00 GB ceiling, with `canBackUp=False`, the warning read *"Backups are still being
taken, from the reserve below the ceiling of 1.00 GB, but once the folder reaches the ceiling a backup
will be refused."* Both clauses are false there. The warning's condition is now the band it describes
rather than the threshold it starts at, so above the max it is removed instead, and the body needed no
change since it is only ever shown inside the reserve.

## Canary

**Step 2 is the control and says so in its own title.** Below the quota a backup is taken,
`reserveInUse` is `false` and nothing is warned about. Every later step asserts a change against that
reading, so a build that warned about everything or about nothing fails step 2 before it can
accidentally satisfy steps 4 to 10.

For the pre-fix form, build `4cfe1006`, the commit before step 8 of
[#348](https://github.com/DutchJaFO/Quotinator/issues/348)'s plan, as `quotinator:canary348`, and run
steps 3 to 6 against it. The reserve band is what #348 introduced, so `reserveInUse` has no meaning
there: step 3 cannot report the band it names, and step 4's warning does not exist, which is what
makes the band observable rather than merely configured.

Steps 7 to 10 were reworked after the reserve bands were first measured and found wrong, so run them
against the current build only; a pre-fix reading of a band that did not exist yet is not evidence
about them.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-08 --bind C:\repos\Quotinator\.claude\temp\qt-backup-08
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-08 --delete C:\repos\Quotinator\.claude\temp\qt-backup-08-out
```

**Expected:** the container and both directories are gone.
