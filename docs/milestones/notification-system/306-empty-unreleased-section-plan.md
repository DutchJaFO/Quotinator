# #306 — Changelog: empty 'Unreleased' section renders on the About page after a release tag

**Status:** Planning
**GitHub issue:** [#306](https://github.com/DutchJaFO/Quotinator/issues/306)
**Tiers required:** T1, T2
**Depends on:** none

---

## Description

The About page renders a collapsible "▶ Unreleased" entry even immediately after a release tag, when
nothing is pending — an empty, clickable section with nothing underneath it.

`About.razor`'s render guard is `@if (_document.Unreleased is not null)`. After a release, the
Pre-Push Checklist's "clear the unreleased section" step in practice leaves `{ "issues": [] }` in
`changelog.en.json` rather than removing the object. That deserializes to a non-null
`ChangelogUnreleased` whose `List<T>` properties all default to empty collections, so the guard passes
and the entry renders regardless of content.

**The fix is a content check, not a null check** — and it belongs on the model, not in markup. This
codebase has no bUnit render-testing infrastructure, so a guard expressed as a testable property is
the only shape that can be proven red-then-green; #278 set that precedent for a comparable
Razor-rendering requirement.

---

## Steps

### 1. Reproduce the bug and write the red test

**Status:** ⬜ Not started

Per `docs/testing-policy.md`, the bug is confirmed reproducible before any fix is written. The
reproduction is already documented in the issue: `{ "issues": [] }` in the changelog source, load
`/about`.

The red test asserts that a `ChangelogUnreleased` deserialized from `{ "issues": [] }` reports itself
as having no content. It fails today because no such property exists.

### 2. Add the content check to the model

**Status:** ⬜ Not started

A property on `ChangelogUnreleased` that is true only when every content collection is empty —
highlights, added, changed, fixed, removed, issues, and CVEs. Every collection, not a sample: a
section carrying only CVEs is still a section worth rendering.

Check this against `schemas/changelog.schema.json` before enumerating them by hand — the schema is
authoritative for which fields exist, and a field added there after this issue must not silently fall
outside the check. `audienceHighlights` in particular is a reserved-key structure (#307) that needs a
deliberate decision rather than an assumption.

### 3. Use it as the render guard

**Status:** ⬜ Not started

`About.razor`'s guard consumes the property instead of testing for null. The markup change is one
condition; the logic is in the model where it can be tested.

Check whether any other consumer renders the unreleased section — the same guard should apply
wherever it does, not only on About.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A `ChangelogUnreleased` with no content in any collection reports itself empty | Unit test | `ChangelogUnreleasedTests.AllCollectionsEmpty_ReportsNoContent` |
| 2 | ❌ | A `ChangelogUnreleased` with content in any single collection reports itself non-empty | Unit test | `ChangelogUnreleasedTests.AnySingleCollectionPopulated_ReportsContent` — one case per collection the schema defines |
| 3 | ❌ | No "Unreleased" entry renders when there is nothing pending | Live | T1: `/about` with `{ "issues": [] }` in the changelog shows no Unreleased entry |
| 4 | ❌ | The Unreleased entry still renders when content is pending | Live | T1: add one unreleased entry, confirm it appears — the fix must not hide real content |
| 5 | ❌ | Every consumer that renders the unreleased section uses the same guard | Live | Confirmed by inspection of all render sites, not About alone |
