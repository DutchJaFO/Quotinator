# ADR 023 — One conflict-rule entry per entity, and the recorded incoming value belongs to the field it governs

**Status:** Accepted
**Date:** 2026-10-04
**GitHub issues:** #420

---

## Context

A `*-conflict-rules.json` file carries one entry per entity, each naming the fields that entry resolves.
Each entry also records a complete snapshot of both sides as they stood when the rule was authored:
`existingRecord`, which is documentation, and `incomingRecord`, which `ConflictRuleLookup.TryResolve`
reads (#374) to decide whether a rule has gone stale or become retirable.

**The snapshot is recorded per entry and read per field, and those two granularities cannot always
agree.** A second rule authored for the same entity in a later sitting sees an incoming side that the
first rule has already enriched, so one entry cannot carry a snapshot that is correct for every field it
governs. A curator who needs two snapshots has only one way to record them: a second entry for the same
entity.

Three parts of the codebase then gave three different answers about whether that is allowed, and no test
anywhere exercised the shape:

| Where | Said |
|---|---|
| `schemas/conflict-resolution-rules.schema.json` and `ConflictResolutionRule`'s XML doc | one entry per entity |
| `ConflictRuleLookup`'s constructor | any number of entries, keyed on entity id plus field |
| `ConflictRuleGenerator.Merge` | rejects it, by throwing |

`Merge` keys a dictionary on entity id, so the bundled `nikhilnamal17-conflict-rules.json` — which names
one Mr. Robot quote twice, once for `date` and once for `character`, with different snapshots — made
`POST /api/v1/import/rules/conflict/generate` answer an unhandled `500`. The duplicate entry was the
symptom; the mismatch between where the value is recorded and where it is read was the cause.

## Decision

**1. One entry per entity is the contract.** A rule file names an entity at most once. This was already
what the schema and the XML doc claimed; it is now what every part of the codebase agrees on.

**2. The recorded incoming value belongs to the field it governs**, not to the entry. Each `fields[]`
item carries its own recorded value, and staleness for a field is judged against that value alone. The
entry-level `incomingRecord` remains, reworded to documentation only — the same standing
`existingRecord` already had.

This is what makes rule 1 expressible. Mr. Robot's two entries collapse into one in which `date` records
`"2017"` and `character` records `null`, with no snapshot to choose between. Under the previous shape no
single `incomingRecord` was correct for both fields, so the collapse was not possible before this change.

**3. A recorded value has three distinct states, and all three must stay distinguishable:** a value
(including a list, as `genres` is), an explicit `null`, and no recorded value at all. `null` is a
legitimate recorded value — it is what the NikhilNamal17 source holds for that quote's `character` — and
"recorded as `null`" is a different claim from "never recorded". A field with no recorded value resolves
`Stale`, which is the safe degradation for a hand-written file that omits it.

**This requirement fixes the C# type, and the obvious choice does not satisfy it.** A `JsonElement?`
collapses the second and third states: `System.Text.Json` resolves a nullable value type through
`NullableConverter<T>`, which never calls the inner converter for a `null` token, so an explicit `null`
and an absent property both deserialize to `HasValue == false`. The property is therefore a
**non-nullable `JsonElement`**, which distinguishes all three as `Null`, `Undefined`, and the value's own
kind — and matches `ExistingRecord`/`IncomingRecord`, which are already non-nullable `JsonElement`.

**4. A non-nullable `JsonElement` that may be `Undefined` carries
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`.** Serializing an `Undefined`
`JsonElement` throws `InvalidOperationException` from `JsonElement.CheckValidInstance()`, and
`POST /generate` writes its merged result straight back to the override file — so a file that legitimately
omits a recorded value would otherwise fail on write rather than on read. The attribute omits the
property instead, which round-trips all three states faithfully.

**5. A file that names one entity twice is reported, not thrown.** The condition is checkable before the
dictionary is built, so per [ADR 022](022-exceptions-only-for-undetectable-conditions.md) `Merge` returns
it as an outcome and the endpoint answers a stated `422` naming the file and the repeated id.

## Consequences

**The hand-authored file format is constrained.** A curator can no longer record two snapshots for one
entity by adding a second entry; the per-field value is where that distinction now lives. This is the
trade-off the decision accepts, and the reason it is recorded here: the alternative was smaller.

**The rejected alternative was to make several entries per entity the supported shape** — teaching
`Merge` to tolerate what `ConflictRuleLookup` already tolerated. It is the smaller change and it would
have cleared the `500`. It was rejected because it accommodates the symptom while leaving a per-entity
snapshot that is read per field, so the same mismatch would keep producing duplicate entries, and
because it makes "which entry does a newly generated field join" an arbitrary choice with no principled
answer.

**Uniqueness is guarded by a test, not by the schema.** JSON Schema cannot express uniqueness by a
property, so `RuleFiles_ConformToSchema` passes a file with a repeated entity id and always will.
`SourceDataIntegrityTests.RuleFiles_NameEachEntityAtMostOnce` is what enforces rule 1 on this
repository's own files.

**An override file written before this change is still readable, and degrades rather than failing.** Its
fields have no recorded value, so they resolve `Stale` — visible, reviewable, and never silently
reapplied. Once [ADR 021](021-file-inputs-are-schema-validated.md)'s runtime schema validation lands
(#384, implemented by #386), such a file becomes non-conforming and would be rejected whole instead; that
migration question belongs to #386.

**Rule 3 is the part most at risk of being undone by a later reader**, since `JsonElement?` looks like
the natural type for an optional value and the defect it causes is invisible until a file records an
explicit `null`. The three-state requirement is stated here so that a type change has to argue with it.
