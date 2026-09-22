# A quote id that worked before an upgrade answers 404 afterwards

**Kind:** diagnostic
**Entry code:** —
**Status code:** `QTN-KNOWN`
**Affected versions:** upgrades from 1.8.2 or earlier to 1.9.0-alpha onwards
**GitHub issue:** [#421](https://github.com/DutchJaFO/Quotinator/issues/421)

## Symptom

A quote id that a consumer stored before the upgrade answers `404`:

```
GET /api/v1/quotes/da53310a-21c1-4742-a0b6-bcd981f926ca  →  404
```

while the same quote is still served under a different id. The quote total also drops slightly across
the upgrade — 799 to 795 on the bundled content.

**And every reseed afterwards reports one curated quote it could not write:**

```
[Database - Seed] quotinator-curated.json report: … Quote[incoming=13 new=0 unchanged=12 … blocked=1 …]
```

with a `Blocked` `Quote Add` for that same id waiting in `GET /api/v1/import/actions`.

## Does it prevent the app or API from functioning?

**No.** Every quote is still served, and every page and endpoint works. Two things are affected: a
stored id, which a consumer that saved one before the upgrade may find gone even though the quote is
still there; and a reseed, which reports one curated quote as blocked every time it runs, because the
file states an id the upgrade removed while the quote's text and source are already stored under the
other one. Nothing else in the file is held up by it — the other twelve quotes are read normally.

## Cause

The upgrade adds a rule that a quote is unique per source, and removes duplicate copies of the same
line first. Where two bundled files carried the same quote for the same source, the copy that was
imported first survives and the other is removed — including its id.

Which copy survives depends on the order the content was first imported, so an upgraded database and a
fresh install can keep different ids for the same quote. Measured on the bundled content: four such
pairs, one of which is a curated quote whose id is stated explicitly in `quotinator-curated.json`.

## Remedy

Look the quote up by its text or its source and store the id the running installation reports. A search
for the quote's text finds it under the id that survived:

```
GET /api/v1/quotes/search?q=<part of the quote's text>
```

Nothing needs to be repaired in the database — conversations that referenced a removed id were
repointed to the surviving quote during the upgrade, so they still render.

For the blocked reseed row: the quote itself is already stored, so nothing is missing and nothing needs
importing. Dismiss the review if it is in the way; it returns on the next reseed until the issue is
fixed. A database Reset followed by a reseed clears it for good — the content is then built from the
files, which keeps the curated id — but it discards everything else in the database too, so it is
worth doing only if you were resetting anyway.

## Notes

Found 2026-09-22 while running the automated-test documents for
[#411](https://github.com/DutchJaFO/Quotinator/issues/411), upgrading a 1.8.2 install. The blocked
reseed row turned up the same day on the developer's own upgraded database during that issue's T1 pass,
and was reproduced on a 1.8.2 upgrade: one `Blocked` `Quote Add`, for that same id. A fresh install
of the same build keeps the curated id and drops the other, so the two installs disagree about which id
names that quote. The issue decides which one is right; until then, treat a quote id from before this
upgrade as something to re-read rather than to rely on.
