# Every database count reads 0 after a startup problem, although the data is still there

**Kind:** diagnostic
**Entry code:** none
**Status code:** `QTN-KNOWN`
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** [#428](https://github.com/DutchJaFO/Quotinator/issues/428)

## Symptom

After a startup that did not complete (the Home page says the application *started with a problem*),
the startup popup lists every database count as `0` under:

```
The database was left at its last known-good state:
```

and the Statistics page shows the same zeros: `Quotes`, `Sources`, `Characters` and the rest, every one
`0`.

## Does it prevent the app or API from functioning?

**No.** The startup problem the Home page names is what limits the application; these counts add nothing
to it. They are wrong, not harmful: the database still holds whatever it held before.

## Cause

The counts are read only once startup completes, and a startup with a problem never completes, so they
are never read. The page and the popup render the unread values as `0` rather than saying they are
unknown.

**One degraded start shows populated counts instead, and they are no safer.** A schema version
recorded ahead of the running build degrades the application (#350) while `InitialiseAsync` completes
normally, so the counts were read and rendered: measured 2026-10-14 on such a database, the Statistics
page reports `795` rather than `0`. Those numbers come from a schema this build cannot vouch for, so
they are not evidence of anything either. Treat any count shown during a reported problem as unknown,
whichever of the two it looks like, and resolve the problem the Home page names.

## Remedy

Ignore the counts while the application reports a startup problem, and resolve the problem the Home page
names; the counts are correct again on the first startup that completes.

## Notes

Measured 2026-09-26: a database holding 799 quotes, started with a read-only data directory, and a
database holding 795 quotes whose migration was refused for want of a backup, both showed every count as
`0`. The popup and the Statistics page first shipped in 1.8.3; that release was not measured. #428 makes
both surfaces withhold the counts while the database is degraded, instead of showing a number.
