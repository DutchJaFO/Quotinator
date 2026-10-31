# A quote's date is missing after an import that reported success

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#470](https://github.com/DutchJaFO/Quotinator/issues/470)

## Symptom

An import file gives a quote a `date`, the import answers `200`, and `GET /api/v1/quotes/<id>` shows no `date`. Importing
the same file again answers `202` with an action to review, although nothing in the file changed.

## Does it prevent the app or API from functioning?

**No.** The quote is stored and served. What it costs is the date, which the import discards without saying so.

## Cause

The same file declares that quote's Source in `sources[]` without a date. A quote follows the date of its Source, and the
Source is stored with none. The cause within the importer has not been traced: the same two pieces work when they are in
separate files, and the bundled sources do not contain this shape.

## Remedy

Give the `sources[]` entry the date as well, or leave the entry out if it sets nothing else. To repair a file already
imported, correct the file and import it again, and decide the action it stages. No new issue should be filed for this:
#470 covers it.

## Notes

Measured 2026-11-01. `database-lifecycle/fixtures/reset-content.json` contains this shape, which is how it was found; the
Reset test reads counts, not dates, so its results are unaffected. This entry goes when #470 is fixed.
