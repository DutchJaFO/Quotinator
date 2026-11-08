# The audit export gives every row the export's own moment as its creation date

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#487](https://github.com/DutchJaFO/Quotinator/issues/487)

## Symptom

In a downloaded audit export (`GET /api/v1/admin/audit/export`), and in `GET /api/v1/admin/audit`, every
row's `dateCreated` is the same value: the moment the export or listing was read. Exporting twice gives
one unchanged row two different creation dates. `dateModified`, `dateDeleted` and `isDeleted` are
likewise defaults rather than what is stored.

## Does it prevent the app or API from functioning?

**No.** Every audit row is exported, and the fields that say what happened and when, `operation` with
`performedAt` for an entry and `action` with `occurredAt` for a change, are read from the database and are
correct. Only the four record-keeping fields are wrong.

## Cause

The queries behind the audit endpoints do not read the record-keeping columns, so each row is reported
with the values a new, unsaved row would have, and a new row's creation date is "now".

## Remedy

Nothing is needed, and nothing is lost: the stored values are intact in the database. Use `performedAt`
or `occurredAt` for when an audit row was written.
[#487](https://github.com/DutchJaFO/Quotinator/issues/487) makes the endpoints report the stored values.
