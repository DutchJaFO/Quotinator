# Generating conflict rules answers 500 for a source

**Kind:** diagnostic
**Entry code:** —
**Status code:** `QTN-KNOWN`
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** [#420](https://github.com/DutchJaFO/Quotinator/issues/420)

## Symptom

`POST /api/v1/import/rules/conflict/generate` answers `500` instead of a rule file. The log carries:

```
[Runtime - Exception] <id> thrown: ArgumentException
System.ArgumentException: An item with the same key has already been added. Key: e69951f1-4d01-964d-86d5-13f80f5bfd8a
```

with the frames ending in `ConflictRuleGenerator.Merge`.

## Does it prevent the app or API from functioning?

**Yes, for that one endpoint.** Rule generation for the affected source cannot be used at all. Nothing
else is affected: quotes are served normally, seeding and importing work, and the rule file already on
disk is still read and applied as usual.

## Cause

Generation merges what it produces into the rule file already on disk, and assumes that file names each
entity at most once. `nikhilnamal17-conflict-rules.json` names one quote twice — once to resolve its
date, once to resolve its character — so the merge is handed the same key twice and stops.

Only that one bundled file is affected: it is the only one of the four with a repeated entity, across 23
rules and 22 distinct entities.

## Remedy

There is no way to make the endpoint succeed for that source from outside the application. The rule file
itself works: importing and seeding read it and apply both of its rules, so only generation is blocked.

If you maintain your own rule file and hit the same message, give each entity a single entry with all of
its fields listed together, rather than one entry per field.

## Notes

Found 2026-09-22 while running the automated-test documents for
[#411](https://github.com/DutchJaFO/Quotinator/issues/411). The duplicate entry has been in the bundled
file since 2026-09-08. Whether one entity may legitimately carry two rules is the open question on the
issue — the fix is either to merge them or to accept both.
