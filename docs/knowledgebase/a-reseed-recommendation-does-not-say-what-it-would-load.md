# A reseed loaded nothing, and the notification recommending it closed as if it had worked

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#478](https://github.com/DutchJaFO/Quotinator/issues/478)

## Symptom

After a Reset, a notification says:

> The database was reset, which rebuilds the schema without reimporting any content. Run a reseed to load the bundled sources, or import your own content instead.

With the bundled sources turned off and nothing in the imports folder, the reseed it offers answers `200` with every count `0` and
`"reports":[]`. The notification then shows `resolution: reseeded`, and the database still holds no quotes.

## Does it prevent the app or API from functioning?

**No.** The application and the API keep working. The database stays empty until content is loaded, and the notification no longer
points that out.

## Cause

The notification's text was written when it was raised and does not change. It cannot reflect the current settings or the files in
the imports folder, so it can promise bundled sources a reseed would not load. A reseed also marks the recommendation resolved
without checking whether it loaded anything.

## Remedy

Give the reseed something to load: set `Quotinator__IncludeDefaultSources=true`, or put files in the imports folder. Then restart
the application and run the reseed again. The restart is needed in both cases, as the setting and the list of files are both read
at startup ([#390](https://github.com/DutchJaFO/Quotinator/issues/390) tracks the list of files). Uploading a file through the
import endpoint also adds content, with no reseed or restart.

## Notes

A reseed adds and updates only. It removes nothing, and it does not bring back content that was uploaded.
