# Backups have reached their quota

**Kind:** diagnostic
**Entry code:** none yet (allocated when codes are, per `docs/knowledgebase.md`'s bootstrapping rule)
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** [#348](https://github.com/DutchJaFO/Quotinator/issues/348)

## Symptom

A warning notification titled *Backups have reached their quota*, reading, with the default 1 GB
ceiling:

> The backups folder holds 950.0 MB, past its operating quota of 921.6 MB. Backups are still being
> taken, from the reserve below the ceiling of 1.00 GB, but once the folder reaches the ceiling a backup
> will be refused. Delete older backups, or raise the quota, to bring the folder back under it; this
> notice clears itself once it is.

While it stands, every option that takes a backup (*Back up, then reseed*, *Remove the oldest backup,
then back up and reseed*, and a Reset) carries a caution:

> The backups folder is at its quota: the backup this takes comes from the reserve, and is refused if it
> would pass the ceiling.

and `GET /api/v1/notifications` reports `backupCaution: true` on a row offering one.
`GET /api/v1/admin/backups/status` reports `storage.reserveInUse: true`.

## Does it prevent the app or API from functioning?

**No.** Every backup is still taken, and nothing that needs one is refused. The warning is advance notice
that the folder is running out of room before that happens.

## Cause

Quotinator keeps its backups folder under two limits:

- **The ceiling**, `Quotinator:MaxBackupStorageGb` (default 1 GB). A backup that would take the folder
  past it is refused; see
  [Content was not loaded, or a migration did not run, because no backup could be taken](no-backup-could-be-taken.md),
  under `BudgetExceeded`.
- **The operating quota**, `Quotinator:BackupQuotaPercent` of the ceiling (default 90%). It refuses
  nothing. The space between it and the ceiling is a reserve, so a backup that has to be taken still is,
  and this warning is raised instead.

The folder is at or past the quota. Backups accumulate from migrations, content loads, Resets and
on-demand backups, and none is removed unless you remove it, directly or through the notification option
that removes the oldest.

A backup's size is estimated from the database file before it is taken, and the estimate is only an
approximation. That is why an option taking a backup is cautioned from the quota onward: at that point
one backup may be enough to reach the ceiling.

## Remedy

Bring the folder back under the quota, by either of:

- **Removing older backups:** list them with `GET /api/v1/admin/backups` and remove one with
  `DELETE /api/v1/admin/backups/{name}`. The warning clears as soon as the removal brings the folder
  under the quota.
- **Raising the limit:** increase `Quotinator:MaxBackupStorageGb`, which raises the quota with it, or
  `Quotinator:BackupQuotaPercent`, then restart. The warning clears at the next startup.

The warning is re-checked at startup, after a Reset, and after every backup taken or removed by the
application. If backups are removed some other way, such as from the host's filesystem,
`POST /api/v1/notifications/refresh` re-checks it without a restart; it needs no admin key.
