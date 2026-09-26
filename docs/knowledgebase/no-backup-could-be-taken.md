# Content was not loaded, or a migration did not run, because no backup could be taken

**Kind:** diagnostic
**Entry code:** none yet (allocated when codes are, per `docs/knowledgebase.md`'s bootstrapping rule)
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** [#348](https://github.com/DutchJaFO/Quotinator/issues/348)

## Symptom

Quotinator takes a backup of the database before any step that changes it in a way only a backup could
undo: applying a migration, loading content into an empty database, and a Reset. When that backup cannot
be taken, the step does not run, and you see one of the following. The value in brackets names the
obstacle; its section below explains it.

**At startup, when content was not loaded,** a notification titled *Content was not loaded: no backup
could be taken*, reading:

> Quotinator did not load its content, because a backup of the database has to be taken first and none
> could be (BudgetExceeded). The Notifications page offers the options that can run now; the linked
> Knowledgebase entry explains why any others are blocked and how to resolve it.

and in the log:

```
[Database - Backup] seeding refused: no backup could be taken (BudgetExceeded). Loading content without one would leave no restore point, so the database is left untouched and nothing is loaded
```

**At startup, when a migration was refused,** `/api/v1/health` answers `503`, and the degraded page and
the health response give a reason beginning:

```
Database migration refused: no backup could be taken (BudgetExceeded).
```

followed by what the obstacle means, what you can do about it, and a link to this entry. The log reads:

```
[Database - Backup] migration refused: no backup could be taken (BudgetExceeded). A migration changes the schema in a way only a backup can undo, so none is applied and the database stays at its recorded version
```

**When a Reset was refused,** `POST /api/v1/admin/database/reset` answers `409 Conflict` titled
*"Reset refused — no backup could be taken"*, with the obstacle in `backupObstacle` and what you can do
in `remedies`. The log reads:

```
[Database - Backup] reset refused: no backup could be taken (BudgetExceeded). A reset drops every table, so it does not run without a restore point unless the caller explicitly accepts that
```

## Does it prevent the app or API from functioning?

**Yes, when a migration was refused:** the database is behind this build, so the application degrades
and serves only health, version, admin and backup requests until the migration can run. **No, when
content was not loaded or a Reset was refused:** the database is left exactly as it was and everything
keeps working; there is simply no content yet, or the Reset did not happen.

## Cause

The backup was stopped by one of the obstacles below. Each section says which of the notification's
options it blocks, why, and how to resolve it. The notification offers only the options that can run at
the moment you look, so an option missing from it is blocked for the reason given here.

The options, for reference:

- **Back up, then reseed** takes a backup and loads the content only if the backup succeeded.
- **Remove the oldest backup, then back up and reseed** removes your single oldest backup to make room,
  then does the same. It is offered only when removing that one backup would be enough.
- **Reseed without a backup** loads the content with no restore point. It asks for your permission first,
  and records in the log and the audit trail that no backup was taken.

A refused migration has no options: it runs at the next start once the obstacle is resolved. A refused
Reset can be retried with `allowNoBackup=true`, accepting that it has no restore point, where the
obstacle allows it.

### `BudgetExceeded`

The backups folder has reached its storage quota (by default 90% of `Quotinator:MaxBackupStorageGb`).

- **Blocked:** *Back up, then reseed*, because a new backup would exceed the quota.
- **Offered:** *Remove the oldest backup, then back up and reseed*, when removing your oldest backup
  frees enough; *Reseed without a backup*.
- **Resolve:** remove old backups, from the notification's option or with
  `GET /api/v1/admin/backups` and `DELETE /api/v1/admin/backups/{name}`; or raise
  `Quotinator:MaxBackupStorageGb` and restart.

### `InsufficientDiskSpace`

The volume holding the backups folder has no free space left.

- **Blocked:** *Back up, then reseed*, because there is nowhere to write the backup.
- **Offered:** *Remove the oldest backup, then back up and reseed*, when the space that backup occupies
  is enough; *Reseed without a backup*.
- **Resolve:** free disk space on that volume. Removing old backups reclaims some of it.

### `DestinationDirectoryNotWritable`

The backups folder could not be created: the data directory is read-only, or the container user lacks
write permission on it.

- **Blocked:** *Back up, then reseed* and *Remove the oldest backup, then back up and reseed*, because no
  backup can be written there however much room there is.
- **Offered:** *Reseed without a backup*. It can still fail if the data directory is read-only as a
  whole, since loading content writes to the database in the same directory.
- **Resolve:** restore write access to the data directory (remount the volume writable, or correct its
  permissions), then restart.

### `DestinationFileNotWritable`

The backups folder exists, but a file could not be created inside it, which usually means a permission
problem on that folder.

- **Blocked:** *Back up, then reseed* and *Remove the oldest backup, then back up and reseed*, for the
  same reason as above.
- **Offered:** *Reseed without a backup*.
- **Resolve:** correct the permissions on the backups folder, then restart.

### `SourceUnreadable`

The database file itself cannot be read: it is corrupt, truncated, or not a database. This is found only
when a backup is actually attempted, never by the check made beforehand.

- **Blocked:** every option. No backup of an unreadable file is possible by any means, and loading
  content into it cannot succeed either.
- **Resolve:** stop the application, move or delete the database file, and restart, which rebuilds it
  empty; or restore an older backup in its place, then restart.

### `DiskFilledDuringBackup`

The volume ran out of space partway through writing the backup, after the check made beforehand had
passed. The partly written file is not a usable backup.

- **Blocked:** nothing permanently. The option you chose stopped before loading anything, and the
  notification stays. The options are worked out again from the volume's state when you next look.
- **Resolve:** free disk space on the volume, and remove the partly written backup file if one was left
  behind. Then choose an option again.

### `Unclassified`

The backup failed for a reason this build does not recognise.

- **Blocked:** the options that need a backup. *Reseed without a backup* is not offered, since nothing
  says the database can be written.
- **Resolve:** check the application log for the underlying error, and report it if it is not obvious.

## Remedy

Resolve the obstacle as its section above describes. A refused migration then runs at the next start. For
content that was not loaded, use one of the notification's options: once the obstacle is resolved,
*Back up, then reseed* is offered and is the one that keeps a restore point. A refused Reset can simply
be run again.

## Notes

Found by [#327](https://github.com/DutchJaFO/Quotinator/issues/327) and resolved in
[#348](https://github.com/DutchJaFO/Quotinator/issues/348). Before 1.9.0, a startup with no backup
possible loaded content or ran migrations without one, and a Reset answered an unhandled `500`.
