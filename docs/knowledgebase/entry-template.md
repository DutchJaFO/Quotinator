# <Condition, as an operator would describe it>

**Kind:** diagnostic | question | guide
**Entry code:** — (allocated when codes are, per `docs/knowledgebase.md`'s bootstrapping rule)
**Status code:** — (`QTN-INV` while impact is unknown, otherwise omit)
**Affected versions:** <version range, or "all">
**GitHub issue:** <#N, or —>

## Symptom

What the operator actually sees, verbatim where there is text to quote — a log line, a message on a
degraded page, an API response detail. Quote it exactly, so searching for it finds this entry.

## Does it prevent the app or API from functioning?

**Yes | No | Unknown.** Answered in the first word, because it is the question the operator has. An
`Unknown` carries a status code and an issue, per `docs/knowledgebase.md` — it is never left as
"probably harmless".

## Cause

What produces it. More than one cause may produce the same symptom: list each, and say which is the
common one. An entry is not required to be complete — a partial answer that names what we know beats
no entry, and later findings are added here rather than in a new entry for the same symptom.

## Remedy

What the operator should do. "Nothing — this costs you nothing" is a legitimate and useful remedy.
Where the remedy differs per cause, say which cause it addresses.

## Notes

Anything that helps a reader judge relevance: when it was first observed, what changes it, the issue
that will remove the condition. Omit the section if there is nothing to say.
