# ADR 027 — A Reset recreates the database; a failed migration is restored, never interpreted

**Status:** Accepted
**Date:** 2026-11-09
**GitHub issues:** #156, #482

---

## Context

A database can reach a state the application cannot reason its way out of: a migration that failed
halfway, a recorded schema version that no longer matches the tables on disk, or a file SQLite cannot
read at all. Two kinds of answer are available. One inspects the failure and repairs what it finds: it
catches the error a migration raised, decides from its message that the change was already applied, and
carries on. The other refuses to interpret anything, puts the database back exactly as it was, and
offers an operator one explicit way out that does not depend on the old state.

The first kind fails silently on the case it did not anticipate. A different failure carrying the same
error message is misclassified and swallowed, and nothing afterwards can tell whether the migrations that
should have run actually did. It also grows: each repair adds a check, and each check is another place
the next unforeseen state can slip through.

These rules were held in `CLAUDE.md` alone until #482, which found them describing a Reset the code had
stopped performing. This ADR is where they are defined; `CLAUDE.md` points here.

## Decision

**A failed migration is restored and rethrown, never inspected.** Before a pending migration runs, the
database is backed up (ADR 025's startup case). If the migration throws, for any reason, the backup is
restored and the exception propagates: the database is left exactly as it was before the attempt, and
the failure is visible rather than interpreted. The migration phase itself catches nothing; a failing
migration's own transaction rolls back when it is disposed. A migration never catches its own failure to
detect an already-applied state.

**A recorded schema version that does not match the schema on disk is a hard failure.** It is never
self-healed. The operator resolves it with a Reset.

**A Reset recreates the database; it never repairs one in place.** It builds a new database beside the
old one, through the same path a fresh install takes (the baseline when one is configured, otherwise
every migration from zero), and only then puts it in place. Nothing of the old database survives: no
table is protected, and the audit-trail tables go with the rest (ADR 014). Because the new database is
built from nothing, a Reset replays no migration history, so no rename collision or partially applied
change in the old file can affect it, and no check against such states is needed.

**A Reset works whatever state the old file is in.** A readable database is backed up first (ADR 025)
and the new one is copied over it through SQLite's backup API, so connections already open see the new
database. A file SQLite cannot read is never backed up: it is set aside in the backup folder's reserved
`unreadable` folder (ADR 025) and the new file takes its place. Whether the file can be read is decided by
reading every page before anything else; that integrity check is an engine-specific read ADR 003's design
goal 5 permits, and it is never a substitute for catching an exception.

**A failed Reset leaves the previous state in place.** A failure while building touches nothing but the
file being built, which is removed. A failure while copying the new database over a readable one restores
the backup taken first. A failure while setting an unreadable file aside moves back whatever was moved.
In every case the exception, or the refusal naming its obstacle, reaches the caller.

**`preserveSchemaVersion` keeps the version history, not the schema.** The baseline leaves one collapsed
row in each version table, `System_SchemaVersion` and `System_ConsumerSchemaVersion`. With
`preserveSchemaVersion=true`, a Reset of a readable database reads both tables' per-version rows first
and writes them into the new database in place of that row. An unreadable file's rows cannot be read, so
the baseline's own are kept.

## Consequences

A Reset is the single remedy for every state a migration cannot resolve on its own, and it is always
available: an unreadable file no longer blocks the one action that could replace it. The cost is that a
Reset discards everything, which is why a readable database is backed up first and why the audit trail
has its own export (ADR 014).

A migration that cannot be made idempotent is not made safe by a check that catches its failure. It is
made safe by the restore around it, and by a Reset when the restore is not enough.

A new recovery path that interprets an exception to decide what state the database is in contradicts
this ADR, however narrow the case it handles.
