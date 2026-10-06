# A correction that only changes capitalisation does not take effect

**Kind:** diagnostic
**Entry code:** —
**Status code:** `QTN-KNOWN`
**Affected versions:** 1.8.0 onwards
**GitHub issue:** [#437](https://github.com/DutchJaFO/Quotinator/issues/437)

## Symptom

A stored value whose capitalisation is wrong stays wrong, and no route corrects it:

- A conflict-resolution rule written to fix the capitalisation of a quote's text, its character, a
  title or a name reports nothing and changes nothing. Re-running an import or a reseed leaves the
  stored value exactly as it was.
- The import review list may show an item for that record with **no field offered to decide**, so
  there is nothing to click and the item cannot be resolved.
- A capitalisation change made in the source file is not reported as a change at all.

Nothing appears in the log, because from the application's point of view the two values are the same.

## Does it prevent the app or API from functioning?

**No.** Quotes are served normally and every other field still imports, merges and corrects as usual.
What is lost is the ability to fix capitalisation, and the review item for such a change is unusable.

## Cause

Capitalisation is ignored when the application compares two values to decide whether they are the same
content. That comparison was reused from the one that decides whether two values identify the *same
record*, where ignoring capitalisation is correct and deliberate: it is what keeps a record's
identifier stable when a title is recapitalised, so the record survives the change instead of becoming
a second one.

For content it is the wrong answer, because capitalisation carries meaning. A rule's intended value
and the stored value are judged identical when they differ only in capitalisation, so the rule is
recorded as already applied and writes nothing. For the same reason an incoming value that has only
been recapitalised is judged unchanged.

[ADR 024](../architecture-decisions/024-case-insensitivity-serves-identity-not-content-equality.md)
separates the two kinds of comparison and states which is which. The application does not yet follow
it; that ADR's *Known non-compliance* section lists exactly where, and #437 is the work.

## Remedy

There is no way to correct capitalisation from outside the application while this is open, and a rule
is not a workaround for it.

The record itself is otherwise intact, and its identifier does not change when capitalisation does, so
nothing has to be recreated or re-imported: once #437 ships, a rule or a review decision corrects the
capitalisation in place.

An import review item with no field to decide can be left alone. It is the same record the
capitalisation question belongs to, and it is not blocking any other item.

## Notes

Capitalisation has been ignored in content comparison since the conflict-resolution rule mechanism
shipped in 1.8.0. Correcting it changes when a record is held for review, which is why it is its own
issue rather than part of the work that found it (#420).
