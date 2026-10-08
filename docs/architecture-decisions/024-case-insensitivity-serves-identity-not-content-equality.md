# ADR 024 — Case-insensitivity serves identity and lookup, never content equality

**Status:** Accepted
**Date:** 2026-10-06
**GitHub issues:** #420, #437

---

## Context

Case-insensitivity in this project answers three different questions, and only two of them were
written down.

ADR 012 governs entity ids: an id derived from content is canonicalized at capture, compared
case-insensitively, and presented canonically. ADR 013 governs Character identity: the
merge-candidate comparison folds case, while storage keeps the casing a name was written with. Both
decide whether two things are *the same thing*, and both are settled.

A third kind of comparison decides whether two values *are the same content*: whether an incoming
value has moved since a rule was authored, and whether a rule's wanted value is already stored.
Nothing recorded that this is a different question from the first two. It is currently answered with
the same case-insensitive comparison, on the stated grounds that the derived id already normalises
casing away, which is reasoning from identity to content and is the conflation this ADR exists to
end.

A casing change in a display-bearing field is a real change. It can be a correction or a downgrade,
and only a person can tell which. Because the derived id deliberately does not move when casing
changes (ADR 012), the entity survives the change intact, which is what makes such a change
reviewable and correctable rather than destructive. Content equality is therefore the one place where
folding case loses information the project needs.

## Decision

**1. Identity comparisons fold case.** An id derived from content, and the merge-candidate and
natural-key lookups that decide whether a row already exists, are case-insensitive. A casing change
never re-identifies an entity and never creates a duplicate. ADR 012 and ADR 013 continue to govern
these, unchanged.

**2. Search and filter comparisons fold case**, for the caller's convenience.
`Quotinator.Data.Queries.IdClauses` and `TextClauses` remain the only sanctioned way to build them.

**3. A content-equality comparison is case-sensitive, for every display-bearing field of every
entity, current and future.** A comparison is a content-equality comparison when its answer decides
whether a value is *unchanged*, not whether it *matches*. A field is display-bearing when its stored
value is shown to a person rather than used to identify or match a row: a quote's text, character and
author; a Source title; a Series, Universe, Season or Person name; and any field added later on any
entity. This is the rule, not a list of exceptions to a case-insensitive default.

Casing is not meaning in a value drawn from a closed set, so an id, an enum or status discriminator,
a type value and a language code stay case-insensitive everywhere, including in content comparison.

**4. Storage and display never normalise casing.** A display-bearing field is stored in the casing it
was written with. This extends ADR 013's rule for `Character.Name` to every such field, and is
distinct from ADR 012's canonical presentation, which applies to ids only.

**5. An action a person must judge is staged with something to decide.** An action staged because
casing differs names the differing field among its decidable fields. An action with no decidable
field is never staged.

**6. A rule can set casing.** A rule whose wanted value differs from the stored value only by case is
applied, not reported as already applied. Correcting casing is a rule's job, in the same way
correcting any other wrong stored value is.

## Consequences

**Every content-equality comparison needs the field's classification available to it.**
`FieldMergeResolver.ValuesEqual` has a two-argument overload that cannot see which field it is
comparing, and a four-argument overload that can. Decision 3 makes the four-argument form the only
correct one for a content comparison, and the two-argument form appropriate only for a closed-set
value.

**The classification stays in one place per entity.**
`QuoteFieldMerge.CaseSensitiveContentFields` is the existing shape for Quote; each entity's
equivalent is defined once, never restated at a call site.

**A case-only retitling becomes a supported, visible operation.** The id does not move (Decision 1),
the lookup still matches (Decision 2), the new casing is what gets stored (Decision 4), and the
change is either surfaced for review or applied by a rule (Decisions 5 and 6) instead of being
silently discarded.

**Changing a title's punctuation is a different act from changing its casing.** Punctuation is part
of the normalised form the derived id is built from, so altering it produces a different entity
rather than a changed one. Nothing in this ADR makes a punctuation change reviewable, and it should
not be mistaken for a casing change.

## Known non-compliance

**The codebase does not satisfy Decisions 3, 5 and 6 today**, and this section exists so that is not
forgotten. Resolved by **#437**, which owns the work and the tests.

| Decision | Current behaviour |
|---|---|
| 3 | `FieldMergeResolver.ValuesEqual` folds case for every entity's conflict and merge detection. `QuoteFieldMerge.CaseSensitiveContentFields` covers a quote's `quoteText` and `character` only, so every other display-bearing field on every entity, including a quote's `author`, is non-compliant. `ConflictRuleLookup` calls the two-argument overload for all three of its comparisons, so it is non-compliant even for the two fields Quote does cover |
| 5 | A case-only difference stages a `Modify` action whose decidable-field list is empty, leaving a reviewer an item with nothing to decide |
| 6 | A rule whose wanted value differs from the stored value only by case reports already-applied and writes nothing, so a casing correction cannot be made through a rule |

Decisions 1, 2 and 4 are satisfied.
