# #420: Generating conflict rules answers an unhandled 500 when a rule file holds two rules for one entity

**Status:** Planning
**GitHub issue:** #420
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Execute this plan**, starting at step 1. Nothing is outstanding: shape A is decided (developer,
2026-10-04, recorded in step 1), the cross-check against the authoritative sources is done, and both
findings it produced are settled in *Scope changes* below rather than left open.

---

## What this fixes

`POST /api/v1/import/rules/conflict/generate` answers an unhandled `500` for the NikhilNamal17 source,
because `ConflictRuleGenerator.Merge` keys a dictionary on entity id over a file that names one entity
twice. The duplicate entry is the symptom. The cause is that `incomingRecord` is recorded per entry but
read per governed field, so two rules authored in separate sittings against a mutating incoming side
cannot share one snapshot, and a curator reaches for a second entry. This issue moves the recorded
incoming value to the field that it governs, which is the only change that makes one entry per entity
expressible for Mr. Robot at all.

Three places currently disagree about whether one entity may carry two entries, and none of them is
tested: the schema and `ConflictResolutionRule`'s XML doc say one entry per entity, `ConflictRuleLookup`
tolerates any number keyed on entity id plus field, and `Merge` rejects it by throwing.

---

## Steps

### 1. Write ADR 023

**Status:** ⬜ Not started

One entry per entity stays the contract, and the recorded incoming value moves from the entry to the
field it governs. Records the rejected alternative (several entries per entity become the supported
shape) and why: it is the smaller change, but it accommodates the symptom while leaving a per-entity
snapshot that is read per field, and it makes "which entry does a newly generated field join" an
arbitrary choice.

Add it to the index in `docs/architecture-decisions/README.md` in the same commit, per
`docs/workflow/issues.md`. Context and effective result only, no history of this session.

This and the plan doc land in their own commit, separate from code, per `process.md`.

### 2. Extend the schema and the model with the per-field recorded value

**Status:** ⬜ Not started

`schemas/conflict-resolution-rules.schema.json`: each `fields[]` item gains a required recorded incoming
value, and `incomingRecord`'s description is reworded to documentation only, the standing
`existingRecord` already has. `ConflictResolutionRule`'s and `ConflictRuleLookup`'s XML docs lose the
claim that `incomingRecord` is read by the matching logic.

`ConflictResolutionFieldRule` gains the matching property as `JsonElement?`, not `string?`: `null` is a
legitimate recorded value, a recorded value can be a list (`genres`), and "recorded as null" has to stay
distinguishable from "not recorded at all". Required by the schema, nullable in C#.

`SourceDataIntegrityTests.RuleFiles_ConformToSchema` goes red here, by design, and turns green in step 7.

### 3. Write the new tests and confirm them red

**Status:** ⬜ Not started

Every test named in the Verification table that does not yet exist, confirmed red against the code as it
stands after step 2, before any behaviour changes. Step 2 supplies the property the tests need to
compile; nothing reads it yet, so each new test fails on the behaviour rather than on compilation.

`TryResolve_GovernedFieldMissingFromIncomingRecord_ReportsStale` is rewritten as
`TryResolve_FieldWithNoRecordedIncomingValue_ReportsStale`: the same statement, asserted through the
absent per-field value rather than an empty `incomingRecord`.

### 4. Judge each field against its own recorded value

**Status:** ⬜ Not started

`ConflictRuleLookup`'s `RuleEntry` carries the field rule's own recorded value instead of the entry's
`IncomingRecord`, and `TryExtractFieldValue`'s walk into that `JsonElement` goes with it. A field with no
recorded value still resolves `Stale`, which is the one behaviour of the old shape worth keeping.

### 5. Record the value on generation, and report a duplicate instead of throwing

**Status:** ⬜ Not started

`ConflictRuleGenerator.Generate` records each field's own incoming value, which it already holds as
`row.IncomingValue`, decoded through the existing `DecodeFieldValue`. `Merge` no longer has a snapshot to
preserve or choose for a newly added field, and returns a duplicate entity id as an outcome rather than
letting `ToDictionary` throw, per ADR 022: the condition is checkable before the dictionary is built.

### 6. Answer the duplicate as a stated 422

**Status:** ⬜ Not started

The `generate` endpoint maps `Merge`'s outcome to a `422` naming the file and the repeated id, and
declares it with `.Produces<ProblemDetails>`. A new `ApiMessages` key with `{0}`/`{1}` placeholders,
substituted through `IApiLocalizer.Format`, never `string.Format`, and translated in `UI.en-GB.json`,
`UI.de.json` and `UI.nl.json` in this same commit.

### 7. Migrate the four bundled rule files and collapse the duplicate

**Status:** ⬜ Not started

Every `fields[]` entry in the four files gains its recorded value, taken from that entry's own
`incomingRecord`. Mr. Robot's two entries collapse into one in which `date` records `"2017"` and
`character` records `null`, which is what `NikhilNamal17_popular-movie-quotes.json` actually holds for
that quote. Both rules keep applying, which step 7's own verification row proves on a real reseed rather
than by inspection.

### 8. Guard the contract where the schema cannot see it

**Status:** ⬜ Not started

JSON Schema cannot express uniqueness by a property, so `RuleFiles_ConformToSchema` passes a file with a
repeated entity id and always will. `SourceDataIntegrityTests.RuleFiles_NameEachEntityAtMostOnce` is the
guard, in the class that already reads those files.

### 9. Let the test script edit a recorded value, and correct document 16's wording

**Status:** ⬜ Not started

`scripts/testing/conflict-rule.csx` gains a recorded-value edit. *A rule whose recorded snapshot no
longer matches reality stages Stale, not Decided* names that script as the way to reach its own "before"
state, and under the new shape staleness is reached by changing a field's recorded value. The document's
Preconditions sentence about an `existingRecord`/`incomingRecord` snapshot is corrected in the same pass.
See *Scope changes* for the pre-existing gap this closes.

### 10. Update the two affected documents and the endpoint reference

**Status:** ⬜ Not started

*Rule-file override endpoints* drops its "Fully green after #420" header block, and its rule counts are
corrected: step 2 says "13 at the time of writing" and "Counts as shipped today: nikhilnamal17 13,
vilaboim 36, series-universe 1, curated 0", against 23 shipped today and 22 after step 7.

`docs/api-endpoints.md` and the endpoint's own `[Description]` attributes gain the `422`, both in this
commit, per `CLAUDE.md`'s *Keeping API documentation in sync*.

### 11. Changelog

**Status:** ⬜ Not started

Entries in `changelog.en.json`'s `unreleased` section with `420` in `unreleased.issues`, and matching
translated entries in `changelog.nl.json` and `changelog.de.json` in the same commit. Regenerate
`CHANGELOG.md` only, per the Pre-Push Checklist's step 3.

### 12. Review the knowledgebase

**Status:** ⬜ Not started

`docs/knowledgebase/generating-conflict-rules-answers-500.md` is reviewed here, at the end, and its
disposition decided then. What the review weighs: the `500` itself exists only in development builds and
never shipped, but a user's own hand-authored rule file still reaches the `422` from step 6, and a field
with no recorded value still resolves `Stale`, so both are conditions a user may need documented. The
existing Remedy is already correct advice for the first.

### 13. T1

**Status:** ⬜ Not started

The application still starts. Nothing beyond startup.

### 14. T2

**Status:** ⬜ Not started

The smoke set, plus the four documents this issue touches or whose mechanism it changes: *Fresh seed
produces zero pending actions*, *Rule-file live read proof*, *A rule whose recorded snapshot no longer
matches reality stages Stale, not Decided*, and *Rule-file override endpoints*.

---

## Scope changes

**Rule-file schema validation at load stays with #384.** ADR 021 requires every file input to be
validated against its schema, rejected whole on non-conformance, bundled content included at runtime.
`LoadConflictRulesAsync` does none of that today: it deserialises, catches `JsonException`, logs
*"not valid JSON"*, and continues without rules. #384 is open, in milestone v1.9.0, and no runtime
schema validation exists anywhere in `src/`. So the recorded value is required by the schema and
enforced only by `RuleFiles_ConformToSchema` at build time against this repository's own four files,
while a user's override file that omits it resolves `Stale` instead of being rejected. That is correct
behaviour today and #384 supersedes it. #384 gets a comment: once it lands, an override file written
before this issue becomes non-conforming and would be rejected whole, which is a migration question
#384 owns.

**The `conflict-rule.csx` recorded-value edit comes into this issue** (step 9) rather than being
deferred. It is not an expansion: moving where the recorded value lives is what makes the edit necessary
for the suite to keep working. It also closes a pre-existing gap that is not this issue's to report
otherwise, and did not originate here: *A rule whose recorded snapshot no longer matches reality stages
Stale, not Decided* already tells a reader to use that script to change a rule's recorded snapshot, and
the script has only ever been able to change a `resolution` or remove a rule, so the document's stated
"before" state is unreachable with the tool it names, today, before any of this.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A rule file naming one entity twice is reported as an outcome, not thrown | Unit test | `ConflictRuleGeneratorTests.Merge_ExistingFileNamesOneEntityTwice_ReportsTheDuplicateInsteadOfThrowing` |
| 2 | ❌ | `generate` answers a stated `422` for such a file, naming the file and the repeated id | Unit test | `ImportRuleEndpointsTests.GenerateConflictRuleFile_ExistingFileNamesOneEntityTwice_Returns422` |
| 3 | ❌ | Two fields of one entity whose recorded incoming values differ are each judged against their own | Unit test | `ConflictRuleLookupTests.TryResolve_TwoFieldsWithDifferentRecordedIncomingValues_EachJudgedAgainstItsOwn` |
| 4 | ❌ | A field with no recorded incoming value still resolves `Stale` | Unit test | `ConflictRuleLookupTests.TryResolve_FieldWithNoRecordedIncomingValue_ReportsStale` |
| 5 | ❌ | Generation records each field's own incoming value | Unit test | `ConflictRuleGeneratorTests.Generate_RecordsEachFieldsOwnIncomingValue` |
| 6 | ❌ | A field newly added by a merge carries its own recorded incoming value | Unit test | `ConflictRuleGeneratorTests.Merge_NewFieldCarriesItsOwnRecordedIncomingValue` |
| 7 | ❌ | The four bundled rule files conform to the extended schema | Unit test | `SourceDataIntegrityTests.RuleFiles_ConformToSchema` |
| 8 | ❌ | No bundled rule file names an entity more than once | Unit test | `SourceDataIntegrityTests.RuleFiles_NameEachEntityAtMostOnce` |
| 9 | ❌ | The new `422` message exists and is non-empty in all three locales | Unit test | `TranslationCompletenessTests` |
| 10 | ❌ | `docs/api-endpoints.md` states the `422` and the one-entry-per-entity contract | Unit test | `RepositoryStructureTests.ConflictRuleDocuments_StateTheOneEntryPerEntityContract`, following `RepositoryStructureTests.SourceRefreshDocuments_SayTheRefreshIsOffByDefault`'s precedent |
| 11 | ❌ | Build and full test run are clean | Live | `dotnet build --configuration Release` then `dotnet test --configuration Release --verbosity normal -m:1`, both `0 Warning(s)  0 Error(s)` |
| 12 | ❌ | The collapsed Mr. Robot entry applies both of its fields against a real seeded database | Live | *Fresh seed produces zero pending actions*, which cannot pass while either rule is `Stale` or `Pending` |
| 13 | ❌ | The rule file is still read live and its rules still take effect | Live | *Rule-file live read proof* |
| 14 | ❌ | Staleness still stages `Stale` after the recorded value moves, and document 16 runs as written | Live | *A rule whose recorded snapshot no longer matches reality stages Stale, not Decided*, for the readings it can make before #347 |
| 15 | ❌ | `generate` runs green end to end, and the merge still drops no existing rule | Live | *Rule-file override endpoints*, all 7 steps, `dropped=0` at step 5 |
| 16 | ❌ | The application still starts | Live | T1, Visual Studio, developer's own action |
| 17 | ❌ | T2 scope passes | Live | The smoke set plus documents 14, 15, 16 and 18 |
