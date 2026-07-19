# #190 — Import files cannot express "leave this property alone"

**Status:** Planning
**GitHub issue:** #190
**Tiers required:** T1, T2
**Depends on:** none (built against current `Quotinator.Core` — #206's Engine merge is already in)

---

## Spec requirements (corrected during planning review 2026-07-19)

1. Give the entry DTOs a way to distinguish "property absent from the JSON document" from "property
   present with an explicit `null`" — an `Optional<T>`-style wrapper type plus a `JsonConverter`,
   applied via `[JsonConverter]`/global registration, not manual `JsonNode` walking (stays inside the
   JSON parsing policy's permitted mechanism).
2. Apply it to every genuinely optional, correctable field on every entry DTO — **9 properties across
   5 DTOs**, one more than the issue's own list (see finding 2 below): `SourceEntry.Date`,
   `SourceEntry.SeriesName`, `PersonEntry.DateOfBirth`, `PersonEntry.DateOfDeath`,
   `SourceStageDirection.ImageUrl`, `SourceSoundCue.SoundFileUrl`, `SourceSoundCue.ImageUrl`,
   `SourceConversation.Description`. (`SourceEntry.Id` stays a plain `string?` — it is a matching-mode
   discriminator, not a correctable value; wrapping it would be wrong, not merely unnecessary.)
3. `ImportActionPlanner`'s changed-fields gate must ignore an absent property entirely, under every
   `DuplicateResolutionPolicy` — never stage any action for a field the file never mentioned.
4. An explicitly-`null` property must still resolve to a genuine reset. The fix must not collapse
   "absent" and "explicit null" back into a single treatment in the other direction.
5. `schemas/source-extended.schema.json` documents the distinction consistently on every affected
   field (most already do at the entity-description level from #162/#180; tighten the remaining
   per-field descriptions to match).
6. Once the general mechanism exists, retire `PlanSourcesAsync`'s natural-key-branch workaround that
   hard-carries `Date` through from the existing row — replace it with the same `Optional<T>`
   mechanism used everywhere else, per the issue's own instruction ("removed in favour of the general
   mechanism").

---

## Background — why this issue exists

`SourceQuote`-derived entry DTOs (`SourceEntry`, `PersonEntry`, `SourceStageDirection`,
`SourceSoundCue`, `SourceConversation`) each carry optional fields typed as plain `string?`. Both an
absent JSON property and an explicit `"date": null` deserialize to C# `null` — indistinguishable. Every
one of `ImportActionPlanner`'s five `Plan*Async` changed-fields checks builds a field map straight from
these DTOs and raw-compares it against the existing row, so an entry that simply never mentions `date`
is treated identically to one that explicitly wants to clear it: under `newest-wins` this silently wipes
the real value; under `review` it stages pointless noise a curator has to decide on for a field the file
never intended to touch.

**Verified before starting** (per this project's standing rule — every recent sub-issue in this
milestone found at least one error in its own issue body before implementation started):

- **Confirmed as claimed**: all five DTOs, their exact optional properties, and `ImportActionPlanner`'s
  raw-inequality changed-fields gate all match the issue's description exactly — read directly from
  `src/Quotinator.Core/Import/*.cs` and `src/Quotinator.Core/Database/ImportActionPlanner.cs`
  (`ToFieldMap`/`changedFields` at `PlanSourcesAsync` line 356-358, `PlanPeopleAsync` line 534-536, and
  the identical pattern repeated in `PlanStageDirectionsAsync`/`PlanSoundCuesAsync`/
  `PlanConversationsAsync`). `Sql.Sources.UpdateFieldsById`/`Sql.People.UpdateFieldsById`/etc. all write
  every listed column unconditionally, confirming an unmitigated bug really would wipe data on apply,
  not just mis-stage a pending action.
- **Confirmed as claimed**: `FieldMergeResolver`'s "empty side loses" rule (`Resolve`, lines 32-65) only
  runs for `MergeOurs`/`MergeTheirs` — every other policy (`Skip`/`NewestWins`/`Review`) builds its
  resolved payload directly from the raw field maps via `FieldMergeResolver.ValuesEqual` alone, with no
  empty-side awareness. This is exactly why the issue calls the merge policies' safety "accidental."
- **Stale reference found**: the issue's own "Expected tests" table lists
  `Quotinator.Engine.Tests.Database.ImportActionPlannerTests` and
  `Quotinator.Engine.Tests.Services.SqliteImportActionServiceTests`. #206 (merged since the issue was
  filed) moved both into `Quotinator.Core.Tests.Database`/`Quotinator.Core.Tests.Services`
  respectively — `Quotinator.Engine`/`Quotinator.Engine.Tests` no longer exist. Corrected in this plan
  doc's Expected Tests table and in the "Corrected issue text" section below.
- **Scope gap found — `SourceEntry.SeriesName` has the identical bug and belongs in scope**: the
  issue's own background text lists six affected fields but omits `SourceEntry.SeriesName`, even though
  requirement 2 in the issue itself says "every optional field... not one entity at a time." Read
  `PlanSourcesAsync`'s explicit-id branch (`ImportActionPlanner.cs` lines 329-358): `incomingSeriesId`
  is computed unconditionally from `s.SeriesName is { } seriesName` — an entry that omits `seriesName`
  resolves `incomingSeriesId` to `null` exactly like an entry that never mentions `date` does, and the
  same raw `changedFields` comparison at line 356-358 treats "existing row already has a Series" +
  "file omits seriesName" as a genuine change, staging a Modify that wipes the Series link on apply.
  This is the same bug, on the same DTO, one field over. It is now in scope alongside the issue's
  original six.
- **Second gap found, adjacent to requirement 6 — the natural-key branch's Modify staging is
  simplified in a way unrelated to #190, but blocks a clean general-mechanism swap**: the natural-key
  branch (`ImportActionPlanner.cs` lines 420-479) never calls `FieldMergeResolver.Resolve` for
  `MergeOurs`/`MergeTheirs` at all — it unconditionally takes `keyIncomingPayload` for every policy
  except `Skip` (line 472-473). Under `MergeOurs`, this means an existing Series link would be silently
  overwritten by an incoming one even though `MergeOurs`'s own contract is "existing wins on a genuine
  conflict" — a pre-existing gap in #180's implementation, not something #190 introduces. Retiring the
  hard-coded `Date` carry-through (requirement 6) means restructuring this branch to build its
  `changedFields` set the same way the explicit-id branch and `PlanPeopleAsync` already do; doing that
  naturally brings this branch onto the same `FieldMergeResolver.Resolve` call the other paths use,
  fixing the merge-policy gap as a byproduct rather than as separately-scoped work. Flagged here as a
  drive-by fix, matching this milestone's existing precedent (#196 fixed a missing `Conversations` tag
  description the same way) — not a reason to file a separate issue, since it only becomes reachable
  code once this branch is rewritten anyway.
- **Decision, resolved by the issue's own text**: requirement 6 says the `Date` carry-through "becomes
  redundant and should be removed in favour of the general mechanism" — taken literally. After this
  change, a natural-key-shaped (`id`-omitting) `sources[]` entry that explicitly sets `"date"` will
  actually take effect, where today it is silently ignored no matter what the file says. This is a
  liberalization, not a behaviour restriction being lifted by accident: nothing in code or schema today
  actually *rejects* a natural-key entry that sets `date`, it is simply overwritten in memory before the
  diff ever runs. `schemas/source-extended.schema.json`'s own `date` description ("Only meaningful
  alongside an explicit `id`... omit entirely unless you intend to set it") already told authors not to
  rely on the old silent-ignore behaviour, so no existing bundled file's meaning changes.
- **Confirmed**: no `docs/architecture-decisions/` or `docs/decisions/` note governs an
  absent-vs-null JSON convention already — checked both directories directly. No existing
  `Optional<T>`-shaped type or `JsonExtensionData`/property-presence idiom exists anywhere in the
  codebase (grepped `src/` for `Optional<`, `JsonExtensionData`, `IsSet`, `WasPresent`) — this is a new
  pattern, not a retrofit of an existing one.
- **Confirmed**: `SourceQuoteFileReader.cs`'s `Options` (`PropertyNameCaseInsensitive = true`,
  line 9) is the single shared `JsonSerializerOptions` used to deserialize every entry DTO — a global
  `Converters.Add(new OptionalJsonConverterFactory())` there covers all 9 properties with no per-file
  attribute repetition needed (though the attribute form still works identically).
- **Placement, corrected during review**: `Optional<T>` is not Quotinator-domain-specific — nothing
  about "was this JSON property present" depends on quotes, sources, or people. `src/Quotinator.Data/
  Import/` already houses exactly this category of domain-agnostic, reusable import infrastructure:
  `FieldMergeResolver`, `CompletenessGuard`, `DuplicateResolutionPolicy` and its own
  `DuplicateResolutionPolicyJsonConverter` (`JsonStringEnumConverter<DuplicateResolutionPolicy>` with
  kebab-case naming — the closest existing precedent for a converter type living flat in this same
  folder, not a `QuoteTypeJsonConverter`-style Core-domain converter). `Optional<T>`/
  `OptionalJsonConverter`/`OptionalExtensions` belong in `Quotinator.Data.Import` alongside them, not
  in `Quotinator.Core.Import` — the same reasoning the issue itself gives for the mechanism ("used for
  all imports") is exactly the test this project already applies to justify Data ownership. `Core`
  already depends on `Data` (since #206) and `ImportActionPlanner.cs` already has
  `using Quotinator.Data.Import;` — no new cross-project reference pattern is introduced.
- **Confirmed no other consumer**: grepped `src/` for all five DTO type names — the only files
  referencing them are their own definitions, `ParsedSourceFile.cs`, `SourceQuoteFileReader.cs`, and
  `ImportActionPlanner.cs`. `SqliteImportActionService.cs`'s own `ToFieldMap` overloads (the ones its
  XML doc says "must stay in sync" with `ImportActionPlanner`'s) operate on already-resolved
  `*ActionPayload` records read back from a staged `SystemImportAction`'s stored JSON — never on the
  raw entry DTOs — so they need no change; the "stay in sync" comment is about the field-name
  vocabulary (the dictionary keys), not about `Optional<T>` awareness.

---

## Approach: `Optional<T>` wrapper, resolved to a plain value before the planner ever sees it

```csharp
namespace Quotinator.Data.Import;

/// Distinguishes "this JSON property was absent" from "present with value null" — the two are
/// semantically different in an import file (absent = leave alone; present-null = reset). Domain-
/// agnostic (see this project's Data/Core boundary) — usable by any import shape, not just Quotinator's.
public readonly struct Optional<T>
{
    public bool HasValue { get; }
    public T? Value { get; }

    public static Optional<T> Absent { get; } = default;
    public static Optional<T> Of(T? value) => new(true, value);
}
```

`System.Text.Json` never invokes a property's converter when the JSON key is absent — the property
simply keeps its type's default. For a `readonly struct`, `default(Optional<T>)` is `HasValue: false`,
which is exactly `Absent`. So a plain `[JsonConverter(typeof(OptionalJsonConverterFactory))]`-annotated
(or globally-registered-factory) `Optional<string>` property needs no extra plumbing to get this for
free — the converter's `Read` only ever runs when the key is genuinely present (null or not), and
returns `Optional<T>.Of(value)`.

The critical design choice: **the planner's `ToFieldMap`/diff/merge/serialize machinery does not
change at all.** `SourceActionPayload`/`PersonActionPayload`/etc. keep their plain `string?` fields —
they represent already-resolved values (what would actually be written), not file-presence state. Only
the handful of call sites that build an `incomingPayload`/`incomingFields` map *from* an entry DTO
change, via one new extension method:

```csharp
public static class OptionalExtensions
{
    /// The entry's own value if the property was present, or the existing row's current value if it
    /// was absent — the single mechanism that makes "absent = never a change" true under every
    /// DuplicateResolutionPolicy, not just the merge policies' own empty-side-loses rule.
    public static T? ResolveAgainst<T>(this Optional<T> optional, T? existingValue) =>
        optional.HasValue ? optional.Value : existingValue;
}
```

On the Add path (no existing row), `existingValue` is passed as `null` — `ResolveAgainst(null)` for an
absent property returns `null`, which is exactly today's behaviour for a brand-new row, so the Add path
needs no behavioural change, only the call-site type change.

---

## Steps

### 1. `Optional<T>` + `OptionalJsonConverter`/`OptionalJsonConverterFactory`

**Status:** Not started.

New files, flat in `src/Quotinator.Data/Import/` — namespace `Quotinator.Data.Import`, alongside
`FieldMergeResolver`/`DuplicateResolutionPolicy`/`DuplicateResolutionPolicyJsonConverter` (see the
Background placement finding: this is domain-agnostic import infrastructure, not Core-domain code):
- `Optional.cs` — the struct above.
- `OptionalJsonConverter.cs` — `OptionalJsonConverter<T> : JsonConverter<Optional<T>>` (`Read` deserializes
  `T` and wraps it in `Optional<T>.Of(...)`; `Write` unwraps or writes `null`) plus
  `OptionalJsonConverterFactory : JsonConverterFactory` (matches `typeof(Optional<>)`, constructs the
  generic converter via `Activator.CreateInstance`).
- `OptionalExtensions.cs` — `ResolveAgainst<T>`.

`Quotinator.Core`'s entry DTOs (Step 3) reference these via `using Quotinator.Data.Import;` — the same
cross-project reference `ImportActionPlanner.cs` already has for `DuplicateResolutionPolicy`.

### 2. Register the converter factory

**Status:** Not started.

`SourceQuoteFileReader.cs`'s shared `Options` field gets `Converters = { new OptionalJsonConverterFactory() }`
(or an equivalent `Converters.Add(...)` at static-init) — one registration covers every `Optional<T>`
property across all five DTOs.

### 3. Convert the 9 properties (5 DTOs)

**Status:** Not started.

`string? Date/SeriesName/DateOfBirth/DateOfDeath/ImageUrl/SoundFileUrl/Description` →
`Optional<string> Date/SeriesName/...` on `SourceEntry` (2), `PersonEntry` (2),
`SourceStageDirection` (1), `SourceSoundCue` (2), `SourceConversation` (1). `SourceEntry.Id` stays
`string?` unchanged (matching-mode discriminator, not a correctable field — see Spec requirement 2).

### 4. `PlanSourcesAsync` — explicit-id branch

**Status:** Not started.

Replace the eager, unconditional `incomingSeriesId` computation (today's lines 329-335, which runs
before either branch) with a lazy `Optional<string?>` resolution — `Absent` stays `Absent`; `Of(null)`
stays `Of(null)`; `Of(name)` resolves the name to an id exactly as today (same-batch index, then DB
lookup, dangling reference still silently drops to `null` — unchanged, #180's existing precedent). In
the explicit-id branch: `var incomingDate = s.Date.ResolveAgainst(row.Date); var incomingSeriesId =
resolvedSeriesId.ResolveAgainst(row.SeriesId);` then build `incomingPayload` from those instead of the
raw `s.Date`/eagerly-computed `incomingSeriesId`. No other change to this branch — `changedFields`,
`ShouldBlock`, merge-policy handling, and Pending/Decided/Blocked staging are all already correct once
fed a properly-resolved `incomingPayload`.

### 5. `PlanSourcesAsync` — natural-key branch (retires the #180 workaround)

**Status:** Not started.

Per Spec requirement 6 and the Background findings above, rewrite this branch onto the same
`changedFields`-set pattern the explicit-id branch and `PlanPeopleAsync` already use, rather than its
own bespoke single-field early-continue:

```csharp
var incomingDate      = s.Date.ResolveAgainst(keyRow.Date);
var incomingSeriesId  = resolvedSeriesId.ResolveAgainst(keyRow.SeriesId);
var keyExistingPayload = new SourceActionPayload(s.Title, typeStr, keyRow.Date, keyRow.SeriesId);
var keyIncomingPayload = new SourceActionPayload(s.Title, typeStr, incomingDate, incomingSeriesId);
var keyExistingFields  = ToFieldMap(keyExistingPayload);
var keyIncomingFields  = ToFieldMap(keyIncomingPayload);

var changedFields = new HashSet<string>(
    keyExistingFields.Where(kv => !FieldMergeResolver.ValuesEqual(kv.Value, keyIncomingFields.GetValueOrDefault(kv.Key))).Select(kv => kv.Key));
if (changedFields.Count == 0) continue;
```

followed by the same `isMerge`/`FieldMergeResolver.Resolve`/`ShouldBlock`(now against the real
`changedFields`, not the hardcoded `{"seriesId"}`)/Pending-Decided-Blocked shape the explicit-id branch
uses. This removes the hard-coded `Date` carry-through (requirement 6) and, as a drive-by, fixes the
pre-existing `MergeOurs`/`MergeTheirs` gap identified in the Background (this branch previously never
called `FieldMergeResolver.Resolve` at all). `Title`/`Type` still cannot differ on this path by
construction — they remain the lookup key, unaffected by this change.

### 6. `PlanPeopleAsync`

**Status:** Not started.

Same shape as Step 4: `var incomingDob = p.DateOfBirth.ResolveAgainst(row.DateOfBirth); var incomingDod
= p.DateOfDeath.ResolveAgainst(row.DateOfDeath);` feeding `incomingPayload`. No other change — this
path has no natural-key branch to retire (a not-yet-migrated Person is Add-only per #173's existing
scope boundary, untouched by this issue).

### 7. `PlanStageDirectionsAsync`

**Status:** Not started.

`var incomingImageUrl = sd.ImageUrl.ResolveAgainst(row.ImageUrl);` feeding `incomingPayload`. The Add
path's `IncomingValue` serialization (`sd.ImageUrl` today) becomes `sd.ImageUrl.ResolveAgainst(null)`
for consistency (behaviourally a no-op, since there is no existing row to preserve).

### 8. `PlanSoundCuesAsync`

**Status:** Not started.

Same shape for both `SoundFileUrl` and `ImageUrl`: `sc.SoundFileUrl.ResolveAgainst(row.SoundFileUrl)`,
`sc.ImageUrl.ResolveAgainst(row.ImageUrl)`.

### 9. `PlanConversationsAsync`

**Status:** Not started.

`c.Description.ResolveAgainst(row.Description)` feeding `incomingPayload`.

### 10. Schema wording

**Status:** Not started.

`schemas/source-extended.schema.json`'s `source`/`stageDirection`/`soundCue`/`conversation` entity-level
descriptions already state the absent-vs-null distinction (added by #162/#180). Tighten the remaining
per-field descriptions that don't yet say it explicitly — `dateOfBirth`/`dateOfDeath` (currently no
description at all beyond `type`), `imageUrl` (StageDirection and SoundCue), `soundFileUrl`,
`description` (Conversation) — each gets a one-line addition consistent with `date`'s existing wording
("Omit entirely unless you intend to set it.").

### 11. Tests (red first, per project rule)

**Status:** Not started.

| Test class | Test method |
|---|---|
| `Quotinator.Data.Tests.Import.OptionalJsonConverterTests` (new file — matches `FieldMergeResolverTests.cs`'s location) | `Read_PropertyAbsent_ReturnsAbsentOptional` |
| " | `Read_PropertyPresentNull_ReturnsOfNull` |
| " | `Read_PropertyPresentValue_ReturnsOfValue` |
| `Quotinator.Core.Tests.Import.SourceQuoteFileReaderTests` | `TryParseExtended_SourceDateAbsent_IsDistinguishableFromExplicitNull` |
| " | `TryParseExtended_SourceSeriesNameAbsent_IsDistinguishableFromExplicitNull` |
| " | `TryParseExtended_PersonDateOfBirthAbsent_IsDistinguishableFromExplicitNull` |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanSourcesAsync_ExplicitId_DateAbsent_NoActionStaged` |
| " | `PlanSourcesAsync_ExplicitId_DateExplicitlyNull_StagesModifyResettingDate` |
| " | `PlanSourcesAsync_ExplicitId_SeriesNameAbsent_NoActionStaged` |
| " | `PlanSourcesAsync_ExplicitId_SeriesNameExplicitlyNull_StagesModifyClearingSeries` |
| " | `PlanSourcesAsync_NaturalKey_DateExplicitlySet_NowTakesEffect` (the requirement-6 liberalization) |
| " | `PlanSourcesAsync_NaturalKey_MergeOurs_ExistingSeriesWins` (the drive-by merge-policy fix) |
| " | `PlanPeopleAsync_DateOfBirthAbsent_NoActionStaged` |
| " | `PlanPeopleAsync_DateOfDeathExplicitlyNull_StagesModifyResettingDate` |
| " | `PlanStageDirectionsAsync_ImageUrlAbsent_NoActionStaged` |
| " | `PlanSoundCuesAsync_SoundFileUrlAbsent_NoActionStaged` |
| " | `PlanConversationsAsync_DescriptionAbsent_NoActionStaged` |
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `ApplyBatchAsync_SourceModifyWithAbsentDate_LeavesExistingDateIntact` |
| " | `ApplyBatchAsync_SourceModifyWithExplicitNullDate_ClearsDate` |

(Corrects the issue's own table, which referenced the now-nonexistent `Quotinator.Engine.Tests.*`
namespaces — see Background.)

### 12. Verify

**Status:** Not started.

`dotnet build --configuration Release` (0 warnings/errors), `dotnet test --configuration Release
--verbosity normal` (full suite green), T1 (developer's own Visual Studio run), T2 (Docker smoke test —
this touches a core `/import` write path, so beyond the baseline checks: import a fixture with an
explicit-id Source correction that omits `date` against a row with an existing date, confirm the date
survives; re-import the same id with `"date": null` explicit, confirm it clears).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `Optional<T>` distinguishes absent vs. present-null vs. present-value on read | Unit test | `OptionalJsonConverterTests` (3 cases) |
| 2 | ⬜ | A Source explicit-id Modify with `date` absent stages no action | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_ExplicitId_DateAbsent_NoActionStaged` |
| 3 | ⬜ | A Source explicit-id Modify with `date: null` stages a Modify resetting Date | Unit test | `ImportActionPlannerTests.PlanSourcesAsync_ExplicitId_DateExplicitlyNull_StagesModifyResettingDate` |
| 4 | ⬜ | `SeriesName` absent vs. explicit-null behaves the same as `date` (scope-expansion finding) | Unit test | `PlanSourcesAsync_ExplicitId_SeriesNameAbsent_NoActionStaged` + `...SeriesNameExplicitlyNull_StagesModifyClearingSeries` |
| 5 | ⬜ | Natural-key branch: absent `date` never changes the row; explicit `date` now takes effect | Unit test | `PlanSourcesAsync_NaturalKey_DateExplicitlySet_NowTakesEffect` |
| 6 | ⬜ | Natural-key branch: `MergeOurs` keeps the existing Series on a genuine conflict (drive-by fix) | Unit test | `PlanSourcesAsync_NaturalKey_MergeOurs_ExistingSeriesWins` |
| 7 | ⬜ | Person `dateOfBirth`/`dateOfDeath` absent-vs-null both work | Unit test | `PlanPeopleAsync_DateOfBirthAbsent_NoActionStaged` + `PlanPeopleAsync_DateOfDeathExplicitlyNull_StagesModifyResettingDate` |
| 8 | ⬜ | StageDirection/SoundCue/Conversation absent optional fields never stage an action | Unit test | `PlanStageDirectionsAsync_ImageUrlAbsent_NoActionStaged`, `PlanSoundCuesAsync_SoundFileUrlAbsent_NoActionStaged`, `PlanConversationsAsync_DescriptionAbsent_NoActionStaged` |
| 9 | ⬜ | Applying a Modify with an absent field leaves the DB value untouched; explicit null clears it | Unit test | `SqliteImportActionServiceTests.ApplyBatchAsync_SourceModifyWithAbsentDate_LeavesExistingDateIntact` + `...ExplicitNullDate_ClearsDate` |
| 10 | ⬜ | `schemas/source-extended.schema.json` documents the distinction on every affected field | Doc review | Field-level descriptions on `dateOfBirth`/`dateOfDeath`/`imageUrl`/`soundFileUrl`/`description` |
| 11 | ⬜ | No regression | Unit test | Full `dotnet test --configuration Release --verbosity normal` |
| 12 | ⬜ | T1 — app starts in Visual Studio | Live (T1) | Developer confirms |
| 13 | ⬜ | T2 — a real import exercises absent-vs-null end to end | Live (T2) | Docker smoke test: explicit-id Source correction omitting `date` preserves it; a follow-up import with `date: null` clears it |

---

## Notes

None yet — this is a planning-only pass; implementation has not started.

---

## Corrected issue text (for a future `gh issue edit`)

```markdown
## Background

An import file cannot currently express "leave this property alone." Every optional field on every
entry DTO is a plain nullable — `SourceEntry.Date`/`.SeriesName` (#162/#180), `PersonEntry.DateOfBirth`/
`DateOfDeath` (#173), `SourceStageDirection.ImageUrl` (#171), `SourceSoundCue.SoundFileUrl`/`ImageUrl`
(#172), `SourceConversation.Description` (#176) — so an absent property and an explicit `"date": null`
both deserialize to `null`, indistinguishable.

Those two must mean different things: import files should not provide values for properties they do
not intend to set. Setting a field to `null` explicitly implies a deliberate reset.

Today the planner can only see `null` and treats it as a value like any other. This is pre-existing and
predates any single issue. It was found while implementing #180, whose curated overlay file is the
first bundled file that legitimately wants to set one property (`seriesName`) and touch nothing else.
#180 sidesteps it on its own enrichment path (Date is carried through unconditionally) rather than
widening its own scope — that workaround does not help the explicit-id correction path, where the
ambiguity remains live for every field above, **including `SourceEntry.SeriesName` itself on the
explicit-id path** (found during this issue's planning review — the natural-key path's workaround only
covers the enrichment shape, not the correction shape).

## What needs to be done

1. Give the entry DTOs a way to distinguish "property absent" from "property present and null" — an
   `Optional<T>`-style wrapper with a `JsonConverter`.
2. Apply it to every correctable optional field on every entry DTO: `SourceEntry.Date`/`.SeriesName`,
   `PersonEntry.DateOfBirth`/`.DateOfDeath`, `SourceStageDirection.ImageUrl`,
   `SourceSoundCue.SoundFileUrl`/`.ImageUrl`, `SourceConversation.Description` — 9 properties across 5
   DTOs, not one entity at a time.
3. `ImportActionPlanner`'s changed-fields gate must ignore any absent property entirely, under any
   `DuplicateResolutionPolicy`.
4. An explicitly-null property must still resolve to a genuine reset.
5. `schemas/source-extended.schema.json` documents the distinction on each affected field.
6. Once this lands, retire `PlanSourcesAsync`'s natural-key-branch workaround that hard-carries `Date`
   through from the existing row, in favour of the general mechanism — this also fixes a pre-existing,
   unrelated gap on that same branch where `MergeOurs`/`MergeTheirs` never actually consulted
   `FieldMergeResolver`.

## Expected tests

| Test class | Test method | Starts |
|---|---|---|
| `Quotinator.Data.Tests.Import.OptionalJsonConverterTests` | `Read_PropertyAbsent_ReturnsAbsentOptional` | ❌ |
| `Quotinator.Core.Tests.Import.SourceQuoteFileReaderTests` | `TryParseExtended_SourceDateAbsent_IsDistinguishableFromExplicitNull` | ❌ |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanSourcesAsync_ExplicitId_DateAbsent_NoActionStaged` | ❌ |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanSourcesAsync_ExplicitId_DateExplicitlyNull_StagesModifyResettingDate` | ❌ |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanSourcesAsync_ExplicitId_SeriesNameAbsent_NoActionStaged` | ❌ |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanSourcesAsync_NaturalKey_MergeOurs_ExistingSeriesWins` | ❌ |
| `Quotinator.Core.Tests.Database.ImportActionPlannerTests` | `PlanPeopleAsync_DateOfBirthAbsent_NoActionStaged` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `ApplyBatchAsync_SourceModifyWithAbsentDate_LeavesExistingDateIntact` | ❌ |
| `Quotinator.Core.Tests.Services.SqliteImportActionServiceTests` | `ApplyBatchAsync_SourceModifyWithExplicitNullDate_ClearsDate` | ❌ |

(Full table, including StageDirection/SoundCue/Conversation cases, in the plan doc.)

## Definition of done

- [ ] All expected tests listed above start red before implementation
- [ ] All requirements implemented
- [ ] All expected tests pass (green)
- [ ] No regression in related tests
- [ ] Findings summarised in a closing comment
```
