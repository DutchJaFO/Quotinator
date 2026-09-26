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

## Remedy

Ignore the counts while the application reports a startup problem, and resolve the problem the Home page
names; the counts are correct again on the first startup that completes.

## Notes

Measured 2026-09-26: a database holding 799 quotes, started with a read-only data directory, and a
database holding 795 quotes whose migration was refused for want of a backup, both showed every count as
`0`. The popup and the Statistics page first shipped in 1.8.3; that release was not measured. #428 makes
both surfaces withhold the counts while the database is degraded, instead of showing a number.
