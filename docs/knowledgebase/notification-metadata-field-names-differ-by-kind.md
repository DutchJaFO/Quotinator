# A notification's metadata mixes field-name styles, and shows a number for its reason

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#476](https://github.com/DutchJaFO/Quotinator/issues/476)

## Symptom

`GET /api/v1/notifications` returns `metadata` such as:

```
{"Reason":1,"ChangedFiles":[],"releaseState":"NotApplicable"}
```

## Does it prevent the app or API from functioning?

**No.** The application reads its own stored values correctly. Only something reading this field itself is affected.

## Cause

Four kinds of notification do not declare the names of their own fields, so those fields keep their code spelling beside the
declared common ones: `reseedrecommended`, `backuprefused`, `backupmaxexceeded` and `backupquotareached`. The reseed reason also
stores its position in a list rather than its name.

## Remedy

Do not build anything on these field names or on the number until #476 lands. They change with it.
