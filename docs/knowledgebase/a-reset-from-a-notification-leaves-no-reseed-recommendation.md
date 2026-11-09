# The database is empty after a Reset from a notification or the degraded page, and no notification says so

**Kind:** diagnostic
**Entry code:** —
**Status code:** `QTN-KNOWN`
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#472](https://github.com/DutchJaFO/Quotinator/issues/472)

## Symptom

After clicking **Reset the database** and **Confirm**, on a notification or in the dialog a degraded start shows, the database
holds no quotes and no notification offers a reseed. A Reset through `POST /api/v1/admin/database/reset` instead leaves a
notification titled "The database holds no quotes", offering a reseed.

## Does it prevent the app or API from functioning?

**No.** The Reset did what it should. What is missing is the notification that explains the empty database and offers the next step.

## Cause

The admin endpoint writes the reseed recommendation after its Reset. A notification's action and the degraded page's Reset button
both run the Reset through the same notification action, which does not.

## Remedy

Reseed or import content as you would have from the recommendation: `POST /api/v1/admin/database/reseed` reloads the sources, and
`POST /api/v1/import` loads a file of your own. No new issue should be filed for this: #472 covers it.

## Notes

Measured 2026-11-01 for a notification's action. Measured 2026-11-09 for the degraded page's button, during #482's check in Visual
Studio: the Reset of an unreadable database file recovered the application, and no reseed was offered.
`notifications-and-changelog/01` step 9 asserts the recommendation and fails until #472 is fixed, and this entry goes then.
