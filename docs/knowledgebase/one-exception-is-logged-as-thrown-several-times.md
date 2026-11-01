# The log shows the same exception thrown several times

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha, the only version measured
**GitHub issue:** [#475](https://github.com/DutchJaFO/Quotinator/issues/475)

## Symptom

Several `Error` entries in a row carry the same id and the same exception type, each with a longer stack trace than the last:

```
[Runtime - Exception] 5637b7c3 thrown: QuoteImportValidationException
[Runtime - Exception] 5637b7c3 thrown: QuoteImportValidationException
```

## Does it prevent the app or API from functioning?

**No.** It says nothing about how the request ended, and the request answers as it otherwise would.

## Cause

The same exception is being reported each time it passes up through an awaited call, so one exception produces one line per level
it crosses. The repeated id is what shows it is one exception rather than several.

## Remedy

Nothing is needed. Count the distinct ids rather than the lines to see how many exceptions there were, and read the last line for
the fullest stack trace.

## Notes

Measured at four lines for a rejected upload. Whether this also accounts for the four lines of
[#445](https://github.com/DutchJaFO/Quotinator/issues/445) has not been measured.
