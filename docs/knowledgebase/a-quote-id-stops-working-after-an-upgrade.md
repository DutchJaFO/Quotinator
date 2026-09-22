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

## Does it prevent the app or API from functioning?

**No.** Every quote is still served, and every page and endpoint works. What breaks is a stored id: a
consumer that saved one before the upgrade may find it gone, even though the quote itself is still
there.

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

## Notes

Found 2026-09-22 while running the automated-test documents for
[#411](https://github.com/DutchJaFO/Quotinator/issues/411), upgrading a 1.8.2 install. A fresh install
of the same build keeps the curated id and drops the other, so the two installs disagree about which id
names that quote. The issue decides which one is right; until then, treat a quote id from before this
upgrade as something to re-read rather than to rely on.
