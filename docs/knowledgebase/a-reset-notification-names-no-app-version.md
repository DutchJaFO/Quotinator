# The notification raised by a Reset names no application version

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#477](https://github.com/DutchJaFO/Quotinator/issues/477)

## Symptom

The `The database holds no quotes` entry in `GET /api/v1/notifications` has no `appVersionId` field, where other notifications have
one.

## Does it prevent the app or API from functioning?

**No.** The notification and its action work. Only the record of which version raised it is missing.

## Cause

Reset empties the version history along with every other table, and the notification is written just before the history is filled
in again.

## Remedy

Nothing is needed. `GET /api/v1/version` names the version running now. A notification from an earlier version cannot be attributed
this way once the application has been upgraded.
