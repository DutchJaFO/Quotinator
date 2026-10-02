# Two backups of the same database show different version numbers

**Kind:** diagnostic
**Entry code:** (allocated when codes are, per `docs/knowledgebase.md`'s bootstrapping rule)
**Affected versions:** v1.8.0 through 1.9.0-alpha
**GitHub issue:** #430

## Symptom

Two files in the backups folder, written seconds apart from the same database, carry different version
numbers in their names:

```
quotinatordata_v9_20260929T201455123Z.db
quotinatordata_v26_20260929T201510876Z.db
```

No migration ran between them. `docs/troubleshooting.md` tells you to read that number when choosing a
backup to restore, so the two names suggest two different schemas where there is only one.

The reverse also happens and is harder to notice: two backups taken from genuinely different schemas can
carry the *same* number.

## Does it prevent the app or API from functioning?

**No.** Both files are complete, valid backups of the database as it stood when each was taken, and either
restores correctly. Only the version number in the name is wrong. Nothing in the application reads that
number: backups are listed, downloaded and removed by name and sorted by the time the file was written, so
no feature behaves differently because of it.

What it costs is a judgement call at the moment you most need to get one right. The number is there to
tell you which schema a backup holds, and it does not reliably do that.

## Cause

The name's version comes from whichever code path took the backup, and the paths do not agree.

The application tracks two schema versions that move independently: one for its own internal tables, one
for the quote domain's. A Reset labels its backup with only the second; a reseed, an on-demand backup and
the migration step label theirs with the larger of the two. With the internal counter at 26 and the domain
counter at 9, the same database comes out as `v9` from one path and `v26` from the other.

Taking the larger of the two is also lossy in its own right: a later state with a different domain counter
but the same internal counter produces the same label, so the number cannot distinguish them.

## Remedy

**Nothing to do, and nothing to repair.** Do not delete or rename an affected file: it is a good backup.

When choosing which backup to restore, go by the **timestamp in the name**, which is accurate to the
millisecond and always correct, rather than by the version number. The newest file is the one closest to
your current data.

If you need to know which schema a given backup actually holds, the recorded versions are inside the file
itself, in its `System_SchemaVersion` and `System_ConsumerSchemaVersion` tables.

## Notes

First observed 2026-09-29, in the startup verification for #348, as a Reset's backup and a reseed's
backup fifteen seconds apart reading `v9` and `v26`.

Present since v1.8.0, which is where the two independent counters were introduced; v1.7.2 and earlier had
a single call site and could not disagree with itself.

The fix (#430) changes the name to carry both versions (`quotinatordata_v26.9_<timestamp>Z.db`), so
the label becomes a faithful description of the schema state. Existing files are not renamed and keep
working as restore sources.
