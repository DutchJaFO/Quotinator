# #420: Generating conflict rules answers an unhandled 500 when a rule file holds two rules for one entity

**Status:** Planning
**GitHub issue:** #420
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Execute step 2.** Step 1 is done — ADR 023 is written and indexed. Nothing else is outstanding: shape A
is decided (developer, 2026-10-04, recorded in ADR 023), the cross-check against the authoritative
sources is done, and every finding it produced is settled in *Scope changes* below rather than left open.

**One of those findings corrects the issue body**, and is the reason steps 2 and 8 read as they do: the
issue's point 2 specifies `JsonElement?` for the per-field recorded value on the grounds that it keeps
"recorded as null" distinguishable from "not recorded at all". Measured against .NET 10, it does not —
`JsonElement?` collapses both to `HasValue == false`. The type is a non-nullable `JsonElement`, which
distinguishes all three states. See *Scope changes* for the measurement and the second defect it
exposed on the write path. **The issue body's point 2 is corrected in the closing comment**, not
silently diverged from.

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

**Status:** ✅ Done, 2026-10-04 — [023-one-conflict-rule-entry-per-entity.md](../../architecture-decisions/023-one-conflict-rule-entry-per-entity.md),
indexed in `docs/architecture-decisions/README.md` and registered in `Quotinator.slnx`. Its rule 3 carries
the three-state requirement and rule 4 the `WhenWritingDefault` attribute, so the type and the write-path
defect both have a decision behind them rather than only a plan step.

One entry per entity stays the contract, and the recorded incoming value moves from the entry to the
field it governs. Records the rejected alternative (several entries per entity become the supported
shape) and why: it is the smaller change, but it accommodates the symptom while leaving a per-entity
snapshot that is read per field, and it makes "which entry does a newly generated field join" an
arbitrary choice.

The ADR also records the **three-state requirement** on the recorded value — a value, an explicit
`null`, and no recorded value at all are three different things, and the third must keep degrading to
`Stale` — because that requirement is what fixes the C# type, and a later reader changing the type
without it would reintroduce the defect *Scope changes* measured. 023 is the next free number
(022 is the highest in `docs/architecture-decisions/`).

Add it to the index in `docs/architecture-decisions/README.md` in the same commit, per
`docs/workflow/issues.md`. Context and effective result only, no history of this session.

This and the plan doc land in their own commit, separate from code, per `process.md`.

### 2. Extend the schema and the model with the per-field recorded value

**Status:** ✅ Done, 2026-10-04 — `recordedIncomingValue` on `fields[]` (required, no `type` constraint)
and `ConflictResolutionFieldRule.RecordedIncomingValue` as a non-nullable `JsonElement` carrying
`[JsonIgnore(WhenWritingDefault)]`. Build clean: 0 warnings, 0 errors. `RuleFiles_ConformToSchema` is red
exactly as designed — every `fields[]` item across the four files reports
*Required properties ["recordedIncomingValue"] are not present*, and nothing else. Confirmed by reverting
the schema alone and re-running: green before, red after, so the validator's additional
`resolution`/`customValue` lines are its own subschema-evaluation verbosity on an
already-invalid document, not a second violation. Turns green in step 9.

`schemas/conflict-resolution-rules.schema.json`: each `fields[]` item gains a required recorded incoming
value, and `incomingRecord`'s description is reworded to documentation only, the standing
`existingRecord` already has. The new property needs **no `type` constraint** — a recorded value is
legitimately a string, `null`, or an array (`genres`) — and it has to be declared explicitly, since
`fields[]`'s item schema sets `additionalProperties: false` and would otherwise reject it.
`ConflictResolutionRule`'s and `ConflictRuleLookup`'s XML docs lose the claim that `incomingRecord` is
read by the matching logic (`ConflictResolutionRule.cs:44`, and `ConflictRuleLookup`'s own `<remarks>`).

`ConflictResolutionFieldRule` gains the matching property as a **non-nullable `JsonElement`, not
`JsonElement?`** — corrected from the issue body's point 2, which has the right requirement and the
wrong type for it. Measured on .NET 10 (*Scope changes* carries the probe and its output):

| In the file | `JsonElement?` | `JsonElement` |
|---|---|---|
| property absent | `HasValue == false` | `ValueKind == Undefined` |
| `"…": null` | `HasValue == false` — **indistinguishable** | `ValueKind == Null` |
| `"…": "2017"` | `HasValue == true`, `String` | `ValueKind == String` |

`JsonElement?` fails the one requirement it was chosen for. The non-nullable form distinguishes all
three, and matches `ExistingRecord`/`IncomingRecord`, which are already non-nullable `JsonElement`.

It carries `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`, which is not cosmetic and
is step 8's subject: without it the write path throws.

Required by the schema, `Undefined`-able in C#: a bundled or generated file always carries it, and a
hand-written file that omits it still degrades to `Stale`, which is the behaviour
`TryResolve_GovernedFieldMissingFromIncomingRecord_ReportsStale` already asserts.

`SourceDataIntegrityTests.RuleFiles_ConformToSchema` goes red here, by design, and turns green in step 9.

### 3. Write the new tests and confirm them red

**Status:** ⬜ Not started

Every test named in the Verification table that does not yet exist, confirmed red against the code as it
stands after step 2, before any behaviour changes. Step 2 supplies the property the tests need to
compile; nothing reads it yet, so each new test fails on the behaviour rather than on compilation.

`TryResolve_GovernedFieldMissingFromIncomingRecord_ReportsStale` is rewritten as
`TryResolve_FieldWithNoRecordedIncomingValue_ReportsStale`: the same statement, asserted through the
absent per-field value rather than an empty `incomingRecord`.

### 4. Migrate the existing rule fixtures

**Status:** ⬜ Not started

**Mechanical, and the largest single body of work in this issue — called out as its own step so a red
test here is not mistaken for a regression.** Once step 5 judges staleness per field, every existing
fixture that builds a `ConflictResolutionRule` without a per-field recorded value has *no* recorded
value for its governed fields, so every such rule resolves `Stale` and every test asserting
`Apply`/`AlreadyApplied` or an auto-resolved field fails. `ImportActionPlannerTests` says so itself, at
its own `BuildQuoteTextKeepRule` (`:405`): a shape that treats these rules as stale "would ... never
reach the auto-resolve behaviour these tests exist to prove."

Measured surface, to be migrated with step 5 rather than after it:

| File | Fixtures |
|---|---|
| `tests/Quotinator.Core.Tests/Database/ImportActionPlannerTests.cs` | 38 |
| `tests/Quotinator.Data.Tests/Import/ConflictRuleLookupTests.cs` | 21 |
| `tests/Quotinator.Core.Tests/Database/ConflictRuleGeneratorTests.cs` | 1 |
| `tests/Quotinator.Core.Tests/Services/SqliteImportActionServiceTests.cs` | 1 |
| `tests/Quotinator.Core.Tests/Database/DatabaseInitializerTests.cs` | 12 JSON `incomingRecord` occurrences |
| `tests/Quotinator.Api.Tests/Endpoints/ImportRuleEndpointsTests.cs` | 3 JSON `incomingRecord` occurrences |

Each field's recorded value is taken from that fixture's own `incomingRecord` for the same field, which
is exactly the transformation step 9 applies to the bundled files — the same rule, so a fixture that
has to be *reasoned about* rather than transcribed is a signal the fixture was asserting something the
old shape allowed and the new one does not, and is reported, not quietly adjusted.

`ConflictRuleLookupTests`' deliberate negatives are the exception and keep no recorded value:
`TryResolve_FieldWithNoRecordedIncomingValue_ReportsStale` from step 3, and
`TryResolve_GovernedFieldMissingFromExistingRecord_IsNotStale`, whose point survives unchanged
(`existingRecord` is never read, before or after).

### 5. Judge each field against its own recorded value

**Status:** ⬜ Not started

`ConflictRuleLookup`'s `RuleEntry` carries the field rule's own recorded value instead of the entry's
`IncomingRecord`. `TryExtractFieldValue` currently does two things — walk into the record object for the
named property, then decode that property's `JsonValueKind` into an `object?`. **Only the decode
survives**: the per-field recorded value *is* the value, so there is no property to walk to, and the
"field absent from the record" return becomes `ValueKind == Undefined`. A field with no recorded value
still resolves `Stale`, which is the one behaviour of the old shape worth keeping.

`FieldMergeResolver.ValuesEqual` is unchanged, and still what the comparison goes through — including
its case-insensitive string handling, which `TryResolve_RecordedValueDiffersOnlyByCase_NotStale` covers
and which step 4's migration must not alter the meaning of.

### 6. Record the value on generation, and report a duplicate instead of throwing

**Status:** ⬜ Not started

`ConflictRuleGenerator.Generate` records each field's own incoming value, which it already holds as
`row.IncomingValue`, decoded through the existing `DecodeFieldValue` and serialized with
`JsonSerializer.SerializeToElement`. That never yields `Undefined` — measured: a `null` value through
`SerializeToElement` is `ValueKind.Null`, a list is `Array` — so a generated file always carries a real
recorded value, which is what lets the schema require it.

`Merge` no longer has a snapshot to preserve or choose for a newly added field, and returns a duplicate
entity id as an outcome rather than letting `ToDictionary` throw at `ConflictRuleGenerator.cs:82`, per
ADR 022: the condition is checkable before the dictionary is built.

### 7. Answer the duplicate as a stated 422

**Status:** ⬜ Not started

The `generate` endpoint maps `Merge`'s outcome to a `422` naming the file and the repeated id, and
declares it with `.Produces<ProblemDetails>` (the endpoint already declares a `422`, so this is the
message and the mapping, not a new declaration). A new `ApiMessages` key with `{0}`/`{1}` placeholders,
substituted through `IApiLocalizer.Format`, never `string.Format`, and translated in `UI.en-GB.json`,
`UI.de.json` and `UI.nl.json` in this same commit.

### 8. Round-trip a field with no recorded value without throwing

**Status:** ⬜ Not started

**A second unhandled `500` on this same endpoint, of the same class as the one this issue exists to
remove, and reachable only once step 2 lands** — found by cross-checking the write path, not reported in
the issue. `ImportRuleEndpoints.cs:108` serializes `Merge`'s output straight back to the override file:

```csharp
string json = System.Text.Json.JsonSerializer.Serialize(merged, RuleFileWriteOptions);
```

A hand-authored override file that omits a field's recorded value — which step 2 explicitly permits, and
which degrades to `Stale` by design — deserializes that field to `default(JsonElement)`
(`ValueKind.Undefined`). If `Merge` then folds a new field into that same entity, the entry is written
back out, and **serializing an `Undefined` `JsonElement` throws `InvalidOperationException` from
`JsonElement.CheckValidInstance()`** — unhandled, a `500`, from `POST /generate`.

The remedy is the `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]` from step 2, and it
round-trips every state faithfully. Measured:

| In | `ValueKind` | Written back as |
|---|---|---|
| `{"field":"date"}` | `Undefined` | `{"field":"date"}` — omitted, no throw |
| `{…,"recordedIncomingValue":null}` | `Null` | `…,"recordedIncomingValue":null` |
| `{…,"recordedIncomingValue":"2017"}` | `String` | `…,"recordedIncomingValue":"2017"` |

This step is the attribute's own red-first test, at the endpoint level where the throw actually lands —
not a unit test of the attribute, which would pass against a model the endpoint never serializes.

**Why `JsonElement?` is not the way out of this.** It does not throw here; it writes `"…": null` for a
field that recorded nothing, silently converting "not recorded" into "recorded as `null`" and flipping
that field's next outcome away from `Stale`. A silent change to a rule's meaning on a write the user did
not ask for is worse than the throw, and it is the same collapse step 2 rejects the type for.

### 9. Migrate the four bundled rule files and collapse the duplicate

**Status:** ⬜ Not started

Every `fields[]` entry in the four files gains its recorded value, taken from that entry's own
`incomingRecord` for the same field. Mr. Robot's two entries collapse into one in which `date` records
`"2017"` and `character` records `null` — both verified against
`data/sources/NikhilNamal17_popular-movie-quotes.json`, which holds `date: "2017"` and `character: null`
for that quote. Note the recorded value is not the resolution: `date`'s rule still resolves `Custom`
`"2015"` while recording the `"2017"` it saw. Both rules keep applying, which step 9's own verification
row proves on a real reseed rather than by inspection.

Counts to migrate, measured: nikhilnamal17 23 rules / 22 ids (the one duplicate), vilaboim 36 / 36,
series-universe 1 / 1, curated 0.

### 10. Guard the contract where the schema cannot see it

**Status:** ⬜ Not started

JSON Schema cannot express uniqueness by a property, so `RuleFiles_ConformToSchema` passes a file with a
repeated entity id and always will. `SourceDataIntegrityTests.RuleFiles_NameEachEntityAtMostOnce` is the
guard, in the class that already reads those files.

### 11. Let the test script edit a recorded value, and correct document 16's wording

**Status:** ⬜ Not started

`scripts/testing/conflict-rule.csx` gains a recorded-value edit. *A rule whose recorded snapshot no
longer matches reality stages Stale, not Decided* names that script as the way to reach its own "before"
state, and under the new shape staleness is reached by changing a field's recorded value. The document's
Preconditions sentence ("A `ConflictResolutionRule` records an `existingRecord`/`incomingRecord`
snapshot") is corrected in the same pass. See *Scope changes* for the pre-existing gap this closes.

### 12. Update the two affected documents and the endpoint reference

**Status:** ⬜ Not started

*Rule-file override endpoints* drops its "**Fully green after:** #420" header block, and its rule counts
are corrected: step 2 says "13 at the time of writing" and "Counts as shipped today: nikhilnamal17 13,
vilaboim 36, series-universe 1, curated 0", against 23 shipped today and 22 after step 9.

`docs/api-endpoints.md` and the endpoint's own `[Description]` attributes gain the `422`, both in this
commit, per `CLAUDE.md`'s *Keeping API documentation in sync*.

### 13. Changelog

**Status:** ⬜ Not started

Entries in `changelog.en.json`'s `unreleased` section with `420` in `unreleased.issues`, and matching
translated entries in `changelog.nl.json` and `changelog.de.json` in the same commit. Regenerate
`CHANGELOG.md` only, per the Pre-Push Checklist's step 3.

### 14. Review the knowledgebase

**Status:** ⬜ Not started

`docs/knowledgebase/generating-conflict-rules-answers-500.md` is reviewed here, at the end, and its
disposition decided then, weighing all three `docs/knowledgebase.md` allows. What the review weighs: the
`500` itself exists only in development builds and never shipped (which the retention table's "written
and resolved inside one development cycle → deleted" row points at), but a user's own hand-authored rule
file still reaches the `422` from step 7, and a field with no recorded value still resolves `Stale`, so
both are conditions a user may need documented. The existing Remedy is already correct advice for the
first.

### 15. T1

**Status:** ⬜ Not started

The application still starts. Nothing beyond startup.

### 16. T2

**Status:** ⬜ Not started

The smoke set, plus the four documents this issue touches or whose mechanism it changes: *Fresh seed
produces zero pending actions*, *Rule-file live read proof*, *A rule whose recorded snapshot no longer
matches reality stages Stale, not Decided*, and *Rule-file override endpoints*.

---

## Scope changes

**The per-field value's C# type is a non-nullable `JsonElement`, correcting the issue body's point 2.**
The issue specifies `JsonElement?` and gives the reason: `null` is a legitimate recorded value, a
recorded value can be a list, and "recorded as null" has to stay distinguishable from "not recorded at
all". The requirement is right and the type does not meet it. `System.Text.Json` resolves a nullable
value type through `NullableConverter<T>`, which never calls the inner converter for a `null` token, so
an explicit `null` and an absent property both arrive as `HasValue == false`. Measured on this
container's .NET 10 (`10.0.112`), not recalled:

```
--- absent: {}
    JsonElement?  -> HasValue=false (indistinguishable from absent)
    JsonElement   -> ValueKind=Undefined
--- explicit null: {"recorded":null}
    JsonElement?  -> HasValue=false (indistinguishable from absent)
    JsonElement   -> ValueKind=Null
```

The non-nullable form gives three distinct states and matches `ExistingRecord`/`IncomingRecord`'s own
type. This is a correction to a stated decision, so it goes in the closing comment and into ADR 023's
own record of the three-state requirement, rather than being absorbed silently.

**The write path's `Undefined` throw is step 8, and is new work this issue must carry.** It is not an
expansion: it exists only because step 2 introduces the property, it lands on the same endpoint and in
the same unhandled-`500`-from-`generate` shape #420 is defined by, and shipping step 2 without step 8
would trade the reported `500` for an unreported one. ADR 022 governs it the same way it governs
`Merge`'s own throw.

**Rule-file schema validation at load stays with #384.** ADR 021 requires every file input to be
validated against its schema, rejected whole on non-conformance, bundled content included at runtime.
`LoadConflictRulesAsync` does none of that today: it deserialises, catches `JsonException`, logs
*"not valid JSON"*, and continues without rules. #384 is open, in milestone v1.9.0, and no runtime
schema validation exists anywhere in `src/` — #384's own body states the same inventory. So the
recorded value is required by the schema and enforced only by `RuleFiles_ConformToSchema` at build time
against this repository's own four files, while a user's override file that omits it resolves `Stale`
instead of being rejected. That is correct behaviour today and #384 supersedes it.

**The comment goes on #386, not #384.** #384 is a parent with four sub-issues; the migration question —
once schema validation lands, an override file written before this issue becomes non-conforming and
would be rejected whole — belongs to the sub-issue that binds the schemas to their readers (#386),
which is where the behaviour it affects is actually implemented.

**The `conflict-rule.csx` recorded-value edit comes into this issue** (step 11) rather than being
deferred. It is not an expansion: moving where the recorded value lives is what makes the edit necessary
for the suite to keep working. It also closes a pre-existing gap that is not this issue's to report
otherwise, and did not originate here: *A rule whose recorded snapshot no longer matches reality stages
Stale, not Decided* already tells a reader to use that script to change a rule's recorded snapshot, and
the script's own usage banner offers only `--remove` and `--field … --resolution …`, so the document's
stated "before" state is unreachable with the tool it names, today, before any of this.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A rule file naming one entity twice is reported as an outcome, not thrown | Unit test | `ConflictRuleGeneratorTests.Merge_ExistingFileNamesOneEntityTwice_ReportsTheDuplicateInsteadOfThrowing` |
| 2 | ❌ | `generate` answers a stated `422` for such a file, naming the file and the repeated id | Unit test | `ImportRuleEndpointsTests.GenerateConflictRuleFile_ExistingFileNamesOneEntityTwice_Returns422` |
| 3 | ❌ | Two fields of one entity whose recorded incoming values differ are each judged against their own | Unit test | `ConflictRuleLookupTests.TryResolve_TwoFieldsWithDifferentRecordedIncomingValues_EachJudgedAgainstItsOwn` |
| 4 | ❌ | A field with no recorded incoming value still resolves `Stale` | Unit test | `ConflictRuleLookupTests.TryResolve_FieldWithNoRecordedIncomingValue_ReportsStale` |
| 5 | ❌ | A field recording an explicit `null` is judged against `null`, not treated as unrecorded | Unit test | `ConflictRuleLookupTests.TryResolve_FieldRecordingExplicitNull_IsJudgedAgainstNull` — the three-state requirement, and the one assertion `JsonElement?` cannot satisfy |
| 6 | ❌ | Generation records each field's own incoming value | Unit test | `ConflictRuleGeneratorTests.Generate_RecordsEachFieldsOwnIncomingValue` |
| 7 | ❌ | A field newly added by a merge carries its own recorded incoming value | Unit test | `ConflictRuleGeneratorTests.Merge_NewFieldCarriesItsOwnRecordedIncomingValue` |
| 8 | ❌ | `generate` against a file omitting a recorded value writes it back without throwing, and does not invent a `null` | Unit test | `ImportRuleEndpointsTests.GenerateConflictRuleFile_ExistingFieldHasNoRecordedValue_RoundTripsWithoutInventingNull` — red as an unhandled `InvalidOperationException` before step 8 |
| 9 | ❌ | Every migrated fixture still auto-resolves; no rule becomes `Stale` through the migration alone | Unit test | The full `ImportActionPlannerTests`, `ConflictRuleLookupTests`, `SqliteImportActionServiceTests` and `DatabaseInitializerTests` suites, green after step 4 |
| 10 | ❌ | The four bundled rule files conform to the extended schema | Unit test | `SourceDataIntegrityTests.RuleFiles_ConformToSchema` |
| 11 | ❌ | No bundled rule file names an entity more than once | Unit test | `SourceDataIntegrityTests.RuleFiles_NameEachEntityAtMostOnce` |
| 12 | ❌ | The new `422` message exists and is non-empty in all three locales | Unit test | `TranslationCompletenessTests` |
| 13 | ❌ | `docs/api-endpoints.md` states the `422` and the one-entry-per-entity contract | Unit test | `RepositoryStructureTests.ConflictRuleDocuments_StateTheOneEntryPerEntityContract`, following `RepositoryStructureTests.SourceRefreshDocuments_SayTheRefreshIsOffByDefault`'s precedent |
| 14 | ❌ | Build and full test run are clean | Live | `dotnet build --configuration Release` then `dotnet test --configuration Release --verbosity normal -m:1`, both `0 Warning(s)  0 Error(s)` |
| 15 | ❌ | The collapsed Mr. Robot entry applies both of its fields against a real seeded database | Live | *Fresh seed produces zero pending actions*, which cannot pass while either rule is `Stale` or `Pending` |
| 16 | ❌ | The rule file is still read live and its rules still take effect | Live | *Rule-file live read proof* |
| 17 | ❌ | Staleness still stages `Stale` after the recorded value moves, and document 16 runs as written | Live | *A rule whose recorded snapshot no longer matches reality stages Stale, not Decided*, for the readings it can make before #347 |
| 18 | ❌ | `generate` runs green end to end, and the merge still drops no existing rule | Live | *Rule-file override endpoints*, all 7 steps, `dropped=0` at step 5 |
| 19 | ❌ | The application still starts | Live | T1, Visual Studio, developer's own action |
| 20 | ❌ | T2 scope passes | Live | The smoke set plus documents 14, 15, 16 and 18 |
