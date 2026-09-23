# Quotes come back after a restart, although the database was reset

**Kind:** diagnostic
**Entry code:** —
**Status code:** `QTN-KNOWN`
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** [#423](https://github.com/DutchJaFO/Quotinator/issues/423)

## Symptom

A database reset empties the library — `GET /api/v1/quotes` reports nothing, and a notification appears
reading *The database holds no quotes*. After the next restart, the bundled quotes are back:

```
[Database - Seed] seeding complete — 6 file(s) processed
[Database - Stats] 795 quotes  473 sources  ...
```

and the notification that offered to reload them now reads **Done**, as though the reload had been
requested.

## Does it prevent the app or API from functioning?

**No.** Everything serves normally, and no data is lost: a reset already discarded the previous content,
and what returns is the bundled content that ships with Quotinator. What it costs is the intent of the
reset — an operator clearing the library to load their own content finds the bundled library back after
a restart, and has to remove it again.

## Cause

Startup loads the configured source files whenever the quote table is empty. A fresh install is empty,
so this is how a new database gets its content — but a reset also leaves it empty, so the next start
treats the reset database as though it were new.

The intended behaviour is that loading happens on a fresh install or when you ask for it, never on its
own after a reset.

## Remedy

Until this is fixed, treat "reset" as "reset, then load your own content before the next restart":

1. Reset the database.
2. Put your own files in `{dataDir}/imports/`, and set `Quotinator__IncludeDefaultSources=false` if you
   do not want the bundled ones.
3. Run a reseed, or restart — either way only the files you have configured are loaded.

With `Quotinator__IncludeDefaultSources=false` and an empty imports folder, a restart after a reset
loads nothing, because there is nothing configured to load.

## Notes

Found 2026-09-23 while running the notification suite for
[#308](https://github.com/DutchJaFO/Quotinator/issues/308). The reseed recommendation resolving itself
is the same cause seen from the other side: the restart performs the load the notification was offering,
so it records the action as carried out.
