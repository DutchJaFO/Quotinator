# Notifications written at the same moment are listed out of order

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#486](https://github.com/DutchJaFO/Quotinator/issues/486)

## Symptom

After an upgrade, the notifications describing each applied migration do not read in sequence. On the
Notifications page, in the startup popup and in `GET /api/v1/notifications`, the list is newest first
overall, but notifications written within the same second can appear in any order: one upgrade listed
`Migration applied (Data v10)` through `(Data v28)` first, then `(Data v5)` through `(Data v9)`.

## Does it prevent the app or API from functioning?

**No.** Every notification is listed, and each one's own content is correct. Only the order among
notifications written within the same second is affected.

## Cause

The lists are sorted by when each notification was written, which is recorded to the second. Notifications
written within the same second, such as the one per migration an upgrade writes, have no further order
defined, so the database returns them in whatever order it finds them.

## Remedy

Nothing is needed. Read the migration number in each notification's title rather than its position in the
list. [#486](https://github.com/DutchJaFO/Quotinator/issues/486) gives the list a defined order.
