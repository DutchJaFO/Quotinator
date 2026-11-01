# The database is empty after a Reset from a notification, and no notification says so

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#472](https://github.com/DutchJaFO/Quotinator/issues/472)

## Symptom

After clicking **Reset the database** and **Confirm** on a notification, the database holds no quotes and the notification list
is empty. A Reset through `POST /api/v1/admin/database/reset` instead leaves a notification titled "The database holds no quotes",
offering a reseed.

## Does it prevent the app or API from functioning?

**No.** The Reset did what it should. What is missing is the notification that explains the empty database and offers the next step.

## Cause

The admin endpoint writes the reseed recommendation after its Reset, and a notification's action does not.

## Remedy

Reseed or import content as you would have from the recommendation: `POST /api/v1/admin/database/reseed` reloads the sources, and
`POST /api/v1/import` loads a file of your own. No new issue should be filed for this: #472 covers it.

## Notes

Measured 2026-11-01. `notifications-and-changelog/01` step 9 asserts the recommendation and fails until #472 is fixed, and this entry
goes then.
