# #307 — Changelog highlights: mark specific entries as notification-worthy

**Status:** In progress
**GitHub issue:** #307
**Tiers required:** T1, T2
**Depends on:** #80

---

## Background

#81's what's-new producer needs to know which of a release's highlights (if any) are worth surfacing as
a startup notification, distinct from the full changelog page content.

## Authoritative-source cross-check

Checked `schemas/changelog.schema.json` and `scripts/changelog.csx` before designing anything, per the
standard Planning step.

**Finding: an existing mechanism already fits this shape.** `ChangelogUnreleased.AudienceHighlights`
(`Dictionary<string, List<string>>`, `Quotinator.Changelog.Models`, shipped in #80) currently gives the
`ha-addon` markdown output a different highlight subset than the main changelog. The schema places no
restriction on which string keys `audienceHighlights` may use (`"additionalProperties": { "type":
"array", ... }`), and the C# model is a bare dictionary with no fallback logic of its own — the only
fallback behaviour (absent key → use standard `highlights`) lives entirely in
`scripts/changelog.csx`'s `GetAudienceHighlights` helper, gated by that script's own
`--audience`/`--fallback` CLI flags.

**Confirmed with the developer (2026-08-12):** reuse `audienceHighlights["notification"]` rather than
add a dedicated new field. #81's own C# producer reads the dictionary directly with its own semantics
(absent or empty → no notification — never falling back to the full `highlights` list the way the
generator's CLI flag can), independent of the generator's fallback logic, which only ever runs when
`scripts/changelog.csx` is explicitly invoked with `--audience notification` — nothing does, since this
key exists purely for runtime consumption, not markdown generation.

This eliminates the schema/generator changes the issue originally scoped — no new field, no generator
change. A real model addition remains, though — **revised again (2026-08-12), per developer pushback**:
a bare string constant is a weak fix when the model is being touched anyway, and #309 will store this
same content in a database later. Per CLAUDE.md's String centralisation policy and general DRY/SOLID
practice, this issue introduces a proper enum plus a single lookup method that owns both the
enum-to-key mapping and the fallback-to-empty behaviour — not a constant callers must combine with
`GetValueOrDefault` themselves at every call site.

`scripts/changelog.csx` never references `Quotinator.Changelog`'s compiled types — it works directly on
raw `JsonElement` (confirmed by reading it; its own `"ha-addon"` default is a pre-existing magic string,
out of scope here) — so this enum only reaches compiled C# consumers (this issue's own test, #81's
future producer), not the generator script.

**Positioned for #309's reuse.** ADR 018 permits `Quotinator.Data` → `Quotinator.Changelog` specifically
so `System_Changelog`'s eventual entity design can depend on this project's models. Defining
`ChangelogReservedAudience` here now, rather than as a stringly-typed value, means #309 has a real enum
ready to back a `CHECK`-constrained column (per ADR 008) if its own design calls for one, instead of
inventing a second, possibly-drifting representation of the same concept later.

**ADR check:** no new table, entity, or migration — ADR 018/005 (system-level content, changelog
storage) govern *where* changelog content is served from, not this field-level convention; no conflict.

---

## Design

`"notification"` becomes a documented, reserved `audienceHighlights` key:

- **Ordinary audience keys** (e.g. `ha-addon`) are markdown-generation-time targets, passed to
  `scripts/changelog.csx --audience <name>` to select that audience's highlight subset for a generated
  file.
- **The `notification` key is runtime-consumed** — #81's producer reads it directly via
  `IChangelogService`, never via the generator script. No generated markdown file is ever produced for
  it, and no `--audience notification` invocation exists anywhere in this project's release tooling.

Nothing about how `audienceHighlights` itself works changes — this is purely a documented convention
for how one specific key is *used*, not a structural change.

### `ChangelogReservedAudience` enum + `ChangelogUnreleased.GetHighlightsFor(...)`

New `src/Quotinator.Changelog/Enums/ChangelogReservedAudience.cs` — matching this project's own
`Enums/`-folder convention (used throughout `Quotinator.Data`/`Quotinator.Core` per ADR 016;
`Quotinator.Changelog` has no `Enums/` folder yet, this is its first):

```
namespace Quotinator.Changelog.Enums;

/// <summary>
/// Reserved <see cref="Models.ChangelogUnreleased.AudienceHighlights"/> keys with application-level
/// runtime meaning, distinct from the free-form audience names <c>scripts/changelog.csx</c> renders
/// markdown for (e.g. <c>ha-addon</c>), which stay open string values outside this project's own
/// knowledge.
/// </summary>
public enum ChangelogReservedAudience
{
    /// <summary>Highlights surfaced as a startup notification (#81), not a markdown-generation audience.</summary>
    Notification
}
```

A new method on `ChangelogUnreleased` (`Models/ChangelogUnreleased.cs`) owns both the enum-to-key
mapping and the empty-when-absent fallback in one place, so no call site touches the dictionary or the
key string directly:

```
public List<string> GetHighlightsFor(ChangelogReservedAudience audience) =>
    AudienceHighlights.GetValueOrDefault(audience switch
    {
        ChangelogReservedAudience.Notification => "notification",
        _ => throw new ArgumentOutOfRangeException(nameof(audience))
    }, []);
```

Only `Notification` is added — `"ha-addon"` remains an unconverted, pre-existing magic string in
`scripts/changelog.csx` (that script has no reference to this project's compiled types, so it cannot
consume this enum regardless), out of scope for this issue. Placed on `ChangelogUnreleased` (not
`ChangelogRelease`) since that is where `AudienceHighlights` itself is already declared — both classes
get the method for free.

---

## Steps

### 1. Plan doc, slnx
**Status:** ✅ Done

### 2. `ChangelogReservedAudience` enum + `ChangelogUnreleased.GetHighlightsFor(...)`
**Status:** ✅ Done

`src/Quotinator.Changelog/Enums/ChangelogReservedAudience.cs` (new — this project's first `Enums/`
folder) and `ChangelogUnreleased.GetHighlightsFor(ChangelogReservedAudience)`.

### 3. Document the reserved key in `schemas/changelog.schema.json`
**Status:** ✅ Done

### 4. Note the convention in CLAUDE.md's Pre-Push Checklist
**Status:** ✅ Done

### 5. Tests
**Status:** ✅ Done

`ChangelogUnreleasedTests` (new file, 2 tests), plus one test each added to `ChangelogSchemaTests`
(`NotificationAudienceKey_IsSchemaValid`) and `ChangelogServiceTests`
(`NotificationAudienceKey_RoundTripsThroughGetHighlightsFor` — placed there rather than
`ChangelogSchemaTests` since it needs a real `ChangelogService` instance, matching that class's existing
responsibility). All written against `ChangelogReservedAudience.Notification`/`GetHighlightsFor(...)`,
never a bare string literal.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `GetHighlightsFor(ChangelogReservedAudience.Notification)` returns the `notification` audience's highlights when present | Unit test | `ChangelogUnreleasedTests.GetHighlightsFor_NotificationKeyPresent_ReturnsItems` |
| 2 | ✅ | `GetHighlightsFor(ChangelogReservedAudience.Notification)` returns an empty list (not null, no exception) when the key is absent | Unit test | `ChangelogUnreleasedTests.GetHighlightsFor_NotificationKeyAbsent_ReturnsEmptyList` |
| 3 | ✅ | A changelog entry using `audienceHighlights.notification` is schema-valid | Unit test | `ChangelogSchemaTests.NotificationAudienceKey_IsSchemaValid` |
| 4 | ✅ | The same entry's `notification` key round-trips through `IChangelogService.GetForCulture` into `GetHighlightsFor` | Unit test | `ChangelogServiceTests.NotificationAudienceKey_RoundTripsThroughGetHighlightsFor` |
| 5 | ❌ | `schemas/changelog.schema.json`'s `audienceHighlights` description documents the reserved `notification` key | Manual | Developer reads the updated schema file description — pending confirmation. The text is at `schemas/changelog.schema.json:73` |
| 6 | ❌ | CLAUDE.md's Pre-Push Checklist references the convention | Manual | Developer reads the updated CLAUDE.md section — pending confirmation. The text is at `CLAUDE.md:1186` |
| 9 | ❌ | A populated `notification` key appears as highlights in the startup dialog | Live | Populate `audienceHighlights.notification` in `changelog.en.json`'s release entry, start the app on an upgraded database, and read the startup notification: its highlights are the flagged items, and only those. **Not confirmable until #308 lands** — nothing renders more than one highlight before it, which is why this issue now sits after #308 in the order of operations rather than before it |
| 7 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s), confirmed |
| 8 | ✅ | Full test suite green | Build | `dotnet test --configuration Release` — all projects passed, confirmed |

No T1/T2/T3 tiers — this issue changes no runtime code path, only schema documentation and a test.

---

## Relationship to existing issues

- **#80** — the changelog system this issue's convention extends.
- **#81** — the sole current consumer; #81's own plan doc reads
  `release.GetHighlightsFor(ChangelogReservedAudience.Notification)`.
- **#309** — independent for build order (either can go first), but `ChangelogReservedAudience` is
  deliberately positioned as a real enum, per ADR 018, for #309's own entity design to reuse directly
  rather than invent a second representation.
