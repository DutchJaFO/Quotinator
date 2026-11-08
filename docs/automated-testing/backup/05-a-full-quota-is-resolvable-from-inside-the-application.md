# A full backups folder is resolvable from inside the application

**Smoke:** no
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #349, #348

## Preconditions

A normal seeded run on a writable bind mount. Nothing is sabotaged at the filesystem level until the
final step: the backups folder is filled to its ceiling with a real file inside `backups/`, which is the same condition an
installation reaches on its own after enough migrations and resets.

This is the one obstacle of the five that an operator can resolve without leaving the application, and
until #349 there was no way to do it: the remedy named an action with no route. The document exists to
prove the loop closes.

## Determinism

- **The ceiling, not the quota.** The filler is the whole of `MaxBackupStorageGb`, so any backup would
  pass the ceiling. Filling only past the 90% operating quota refuses nothing since #348: a backup there
  is still taken, from the reserve below the ceiling, and a warning raised instead.
- **`MaxBackupStorageGb` stays at its default 1 GB**, so the filler is sized from that. A smaller budget
  cannot be configured, since the setting is whole gigabytes.
- **One filler file, sparse.** `SetLength` allocates without writing a gigabyte of content, so the step is
  fast and does not depend on the host's write throughput.
- **The stored backup is readable from the host, and that is itself part of what this document
  proves.** It was not, on the first run: the connection that writes a backup was pooled, so its file
  handle survived disposal and the file stayed locked for the life of the process. The first version of
  this document worked around it by hashing inside the container and blamed the host filesystem; the
  cause was ours, and it is fixed at source (`Pooling=false` on the destination connection). Hashing
  from the host now is the check that the leak has not returned.
- **The download is saved byte for byte** (`http.csx --save`), since `--body-out` decodes a body as text
  and would corrupt a database file. It is then matched against the stored backups by content
  (`TestProbe file --expect-identical-in`), because the stored file's name is a timestamp known only at
  run time.

## Steps

### 1. Seed a healthy database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-05 --delete C:\repos\Quotinator\.claude\temp\qt-backup-05-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-05\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-05-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-05 --port 18385 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-05
```

**Expected:** the environment reports healthy.

### 2. Confirm status says a backup is possible, and take one

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18385/api/v1/admin/backups/status --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\status-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\status-before.json --equals canBackUp=true --equals storage.usedBytes=0 --equals storage.reserveInUse=false
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18385/api/v1/admin/backups/create --expect 201 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\created.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\created.json --not-empty name --greater-than sizeBytes=0
```

**Expected:** status answers `200` and create `201 Created`, and both `json` commands exit `0`: a backup is
possible with nothing used and the reserve not in use, and the created backup has a name and a non-zero
size.

**This is the positive control for the whole document.** Everything below asserts a refusal, and a build
that refused every backup would satisfy all of it without this step.

**On failure:** if create refuses here the environment is not clean, and nothing below is measuring the
quota.

### 3. Fill the folder to its ceiling, and confirm status now says a backup is not possible

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --sparse C:\repos\Quotinator\.claude\temp\qt-backup-05\backups\filler.db 1073741824 --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-05\backups\filler.db 1073741824
dotnet script scripts/testing/http.csx -- --url http://localhost:18385/api/v1/admin/backups/status --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\status-full.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\status-full.json --equals canBackUp=false --equals obstacle=BudgetExceeded --equals storage.reserveInUse=true --length-equals remedies=3 --contains "remedies.0=GET /api/v1/admin/backups" --contains "remedies.0=DELETE /api/v1/admin/backups/{name}"
```

**Expected:** the filler is `1073741824` bytes, and the `json` command exits `0`: no backup is possible,
the obstacle is `BudgetExceeded`, the reserve is in use, and of three remedies the first names
`GET /api/v1/admin/backups` and `DELETE /api/v1/admin/backups/{name}`.

**On failure:** `canBackUp=true` here means the status endpoint is not measuring against the ceiling,
and step 4's refusal would be for some other reason.

### 4. Confirm a reset now refuses, naming the same obstacle

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18385/api/v1/admin/database/reset --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\reset-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\reset-refused.json --equals backupObstacle=BudgetExceeded
```

**Expected:** `409`, and the `json` command exits `0` on `BudgetExceeded`.

**The remedy naming the endpoints is the assertion, not decoration.** #348 shipped this text describing an
action the operator had no way to perform; the point of this step is that it now tells them exactly which
call resolves it.

### 5. Apply the remedy through the endpoints, and confirm the reset then works

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18385/api/v1/admin/backups --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\list.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\list.json --equals totalCount=2 --contains items=filler.db
dotnet script scripts/testing/http.csx -- --method DELETE --url http://localhost:18385/api/v1/admin/backups/filler.db --expect 204 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18385/api/v1/admin/backups/status --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\status-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\status-after.json --equals canBackUp=true --equals storage.reserveInUse=false
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18385/api/v1/admin/database/reset --expect 200 --status
```

**Expected:** the list reports two files, the filler among them; the delete returns `204`; status returns
to a backup being possible with the reserve not in use; and the reset returns `200`.

**This closes the loop the issue was filed for:** the obstacle was reached, the message named a remedy,
the remedy was performed through the API alone with no filesystem access, and the action that had refused
now succeeds.

### 6. Confirm the download returns the backup byte for byte

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18385/api/v1/admin/backups --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\list-after.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18385/api/v1/admin/backups/{name}/content" --value name=C:\repos\Quotinator\.claude\temp\qt-backup-05-out\list-after.json#items.0.name --out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\content-url.txt
dotnet script scripts/testing/http.csx -- --url-file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\content-url.txt --expect 200 --save C:\repos\Quotinator\.claude\temp\qt-backup-05-out\downloaded.db
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-identical-in C:\repos\Quotinator\.claude\temp\qt-backup-05-out\downloaded.db C:\repos\Quotinator\.claude\temp\qt-backup-05\backups
```

**Expected:** the download answers `200`, and the `file` command exits `0`, naming the stored backup the
download is identical to.

**Two assertions in one.** A match says the download is byte for byte. Reading the *stored* files from the
host at all says no handle is still held on them: the leak that made an immediate download fail with an
unhandled `500` in T1. A check that cannot open a stored file is that leak returning, not a test problem.

**On failure:** a downloaded backup that does not match the stored file is not a restore point, whatever
the status code said.

### 7. Confirm a removal the filesystem refuses says so, rather than failing raw

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-05 --port 18385 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-05 --read-only-data --wait-listening
dotnet script scripts/testing/http.csx -- --url http://localhost:18385/api/v1/admin/backups --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\list-read-only.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- url --template "http://localhost:18385/api/v1/admin/backups/{name}" --value name=C:\repos\Quotinator\.claude\temp\qt-backup-05-out\list-read-only.json#items.0.name --out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\delete-url.txt
dotnet script scripts/testing/http.csx -- --method DELETE --url-file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\delete-url.txt --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-05-out\delete-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-05-out\delete-refused.json --contains "detail=could not be removed" --contains detail=read-only
```

**Expected:** the listing still answers `200` on the read-only mount, and the removal answers `409` with a
detail saying the backup exists but could not be removed because the data directory is read-only.

**This is the case the endpoint is most likely to meet in anger:** a read-only mount is what degrades
startup in the first place, and removing old backups is what the operator is then told to do. The
listing still answers on the same mount, so the operator can see what is there even while unable to
remove it.

## Observed effect

**Measured 2026-11-08** against `quotinator:local` built from `01ab4ef6`, in this form and in the
PowerShell form it replaced. A fresh install reports a backup possible with `0` bytes used against a
`966,367,641`-byte quota; `create` answers `201` with a 4.7 MB backup. With the 1 GB filler in place the
status reads `BudgetExceeded`, reserve in use, three remedies, the first naming the list and delete
endpoints, and a reset answers `409` with the same obstacle. Listing shows both files; deleting the filler
answers `204`, status recovers, and the reset answers `200`. The downloaded backup, `4,689,920` bytes, is
identical to the stored backup the reset took, read from the host. On a read-only mount the listing still
answers and the removal answers `409`, naming the read-only data directory.

The two stored backups carry different version numbers in their names, `_v28_` from `create` and `_v9_`
from the reset, for the same database: see the Knowledgebase entry
[*Two backups of the same database show different version numbers*](../../knowledgebase/two-backups-of-one-database-show-different-versions.md).

**Measured 2026-08-29** against `quotinator:local`, before #348's reserve, when step 3 filled to 95% and
the operating quota refused a backup. Step 3 has filled to the ceiling since.

The loop closes exactly as intended. A fresh install reports `canBackUp=True` with `used=0` against a
`966,367,641`-byte quota beneath a `1,073,741,824`-byte ceiling; `create` writes a real 4.5 MB backup and
names it. Filling to 95% flips status to `canBackUp=False`/`BudgetExceeded` with `reserveInUse=True`, and
a reset then answers `409` with the same obstacle. Listing, deleting the filler, and re-checking status
restores `canBackUp=True`, and the reset returns `200`. The downloaded backup's SHA-256 matches the
container's own hash of the stored file.

**This pass found three defects in this document and one in the application.**

The document named the wrong admin key, used `$_.ErrorDetails.Message` (null in Windows PowerShell 5.1,
so the obstacle read as empty), and worked around a locked backup file by hashing inside the container,
attributing the lock to the host filesystem. That third one was wrong twice over: the lock was ours, and
calling it a host property hid a real defect for a day. See the T1 note below.

**Re-run 2026-08-29, after the handle leak was fixed.** Creating a backup and downloading it immediately
now returns `200`, and the stored file is readable from the host, which it was not before, so step 6
hashes it directly rather than reaching into the container.

**A second application defect was found in T1, which this T2 pass had missed entirely.** Downloading a
backup created moments earlier answered an unhandled `500`: `Microsoft.Data.Sqlite` pools connections by
default, so the destination connection that writes a backup returned to the pool on disposal and kept
its file handle open for the life of the process. Every backup this application had ever written stayed
locked. It is invisible on Unix (a retained handle blocks neither a second open nor an unlink), which
is exactly why running this document in a Linux container could not catch it, and why the workaround
above looked like a host quirk instead of evidence. The destination connection now opts out of pooling.

The other application defect is step 7's subject, and it was not in the plan. `DELETE` against a read-only data
directory answered an **unhandled `500`**: `File.Delete` threw, nothing caught it. That is precisely the
defect class #348 was filed to remove, reintroduced one endpoint over, on the single path an operator is
most likely to take: the read-only mount that degraded their startup is the same one that refuses the
removal they were just told to perform. The writer now reports a typed outcome instead of throwing, and
the endpoint answers `409` naming the condition and its remedy.

## Canary

Build the commit before [#349](https://github.com/DutchJaFO/Quotinator/issues/349)'s first code
change, under a distinct tag, and run steps 2 to 7 against it.

Steps 5, 6 and 7 must answer `404`. Listing, deleting and downloading a backup are the endpoints #349
added, and this document's title is the claim they establish: that a full folder can be resolved from
inside the application instead of by reaching into the volume. Without them the only remedy is a shell
on the host, which is the state #349 ended.

Steps 2 to 4 belong to [#348](https://github.com/DutchJaFO/Quotinator/issues/348) and are expected to
pass on that build. They are the control: the folder genuinely reached its ceiling and the reset
genuinely refused, so the `404`s in steps 5 to 7 are about the missing remedy rather than about a
container that never got into the state.

Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-05 --bind C:\repos\Quotinator\.claude\temp\qt-backup-05
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-05 --delete C:\repos\Quotinator\.claude\temp\qt-backup-05-out
```

**Expected:** the container and both directories are gone.
