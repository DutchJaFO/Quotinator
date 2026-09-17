# An uploaded file is rejected as invalid JSON

**Kind:** diagnostic
**Entry code:** —
**Status code:** —
**Affected versions:** up to and including 1.9.0-alpha
**GitHub issue:** [#384](https://github.com/DutchJaFO/Quotinator/issues/384)

## Symptom

`POST /api/v1/import` answers `422` with:

```
File content is not valid JSON in Quotinator's canonical quote schema.
```

and the log carries two `Error` lines:

```
[Runtime - Exception] <id> thrown: JsonReaderException
System.Text.Json.JsonReaderException: '0x0D' is invalid within a JSON string. … LineNumber: 0 | BytePositionInLine: 38.
[Runtime - Exception] <id> thrown: QuoteImportValidationException
```

## Does it prevent the app or API from functioning?

**No.** The upload is refused and nothing is imported. The application is unaffected and every other
endpoint keeps working.

## Cause

Two different causes produce this one message, which is why the message is being changed:

1. **The file is not well-formed JSON** — a missing brace or quote, a stray control character. The
   `JsonReaderException` names the line and byte position where parsing stopped.
2. **The file is valid JSON but does not match the schema it must conform to**
   (`schemas/source-flat.schema.json`) — a missing section, a field of the wrong shape, a value outside
   an allowed set. Today this is reported with the same "not valid JSON" text, which sends a curator
   looking for a syntax error that is not there.

## Remedy

Validate the file against `schemas/source-flat.schema.json`, correct what the validator names, and
upload again. For cause 1, the byte position in the log points at the character where parsing stopped.

## Notes

[#384](https://github.com/DutchJaFO/Quotinator/issues/384) separates the two causes, so a schema
violation will report as a schema violation and name the failing section. This entry gains that
behaviour, and its own new symptom text, when that lands.
