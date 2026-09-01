# #308 — Notification: multi-line/rich message layout

**Status:** Planning
**GitHub issue:** #308
**Tiers required:** T1, T2
**Depends on:** #312, #302, #303, #304, #367

---

## Description

`NotificationTable` renders its message as a single plain cell with no line-break handling — Razor
HTML-encodes the value, so any embedded newline collapses to whitespace. This issue renders the
`Title`/`Body` structure #312 introduced: a headline and a body, each displayed as what it is.

**This is the last producer-facing issue in the milestone's order, deliberately.** It defines how *each
notification type* is laid out across *both* surfaces — the startup/popup dialogs and the
`/notifications` view — and each remaining producer brings a type with its own payload. Designing those
layouts before the types exist is guessing.

## Scope revision — no longer rendering-only

**Recorded 2026-08-15, relocated here from `overview.md` 2026-08-22.** This issue was originally
scoped as a CSS fix, and placed second in the milestone order so that rendering would precede the
producers and their output would display correctly from the start.

Both were revised. The milestone's goal was restated — v1.8.0 shipped a *basic* notification system
and this milestone makes it complete — which made #278's flat `Message` the bottleneck rather than a
fixed constraint. #312 accordingly gave notifications a real `Title` and `Body`, so this issue's job
became rendering that structure rather than coaxing line breaks out of one flat string.

**The GitHub issue already carries this revision** (its own *Revised (2026-08-15)* section). It is not
stale; `overview.md`'s dependency map claimed otherwise and was corrected on 2026-09-01.

The position moved from second to last in the same revision, for the reason in the Description above.

## Compliance review, 2026-09-01

The verification table this plan carried before today declared **seven rows, every one of them `Live`,
and not a single unit test**. That fails `docs/release-verification.md`'s T2 definition on both axes:
T2 is unit tests *and* automated tests, each covering the positive *and* negative direction. Three
further problems:

- **Nothing negative anywhere.** All seven rows asserted that something renders correctly. None
  asserted that the wrong thing is refused, and none would have caught a check wired to nothing.
- **Row 6 was not a test.** "No migration added; `NotificationEntity` unchanged in the diff" is reading
  a diff, which nothing re-runs.
- **The issue's own *Expected tests* table hedged**: "a rendering/formatting-level test if one is
  feasible without bUnit — otherwise a Live/T1 verification step". That hedge is false. This project
  has a settled pattern for exactly this — `AwaitingReview`, `DecisionRows`, `FileNameFor`,
  `GetDisplayStatus`, `ShowsRunControl`, `ShowsDismissControl` are all `internal static` precisely so
  they can be tested without bUnit. Layout decisions go the same way.

---

## Steps

**The step order enforces red-first; it is not left to memory** (developer, 2026-09-01). Step 1 writes
every test — unit *and* automated — and runs them before any implementation exists. Steps 2–5 turn
named rows green. No implementation step precedes its own test.

### 1. Write every test first, and run them red

**Status:** ✅ Done — six unit tests red, T2 document written and run red

Exit condition: every unit test in the table below exists and **fails on its own assertion**, and the
new T2 document has been run against the current build and failed.

**Two mechanics make that honest rather than nominal:**

- **For code that does not exist yet, add the signature with a deliberately wrong body** — the
  technique #367 used for `ShowsDismissControl`. A compile error is not a red test; it proves only that
  a symbol is missing. The assertion must be the thing that fails.
- **Write the T2 document now, and run it now.** At this moment `HEAD` *is* the pre-work build, so the
  canary `docs/testing-policy.md` requires costs nothing — no worktree, no second image. Running the
  document first is the cheapest point in the issue's life at which it can be proven red, and it is the
  only point at which that proof is free. Record the result in the document's own *Canary* section.

### 2. Render `Title` as a distinct element with `Body` beneath it

**Status:** 🔄 In progress — turns rows 1–3 green

Its own heading or emphasis, not one undifferentiated cell. A notification with no title still renders
correctly — `Title` is nullable in #312's schema, and the two shipped producers (#279, #289) predate
it. The decision of *whether* a title element is rendered lives in `NotificationTable.ShowsTitle`,
`internal static` for the reason above.

### 3. Render embedded line breaks in `Body`

**Status:** ✅ Done — turns rows 4–5 green

CSS (`white-space: pre-line` on the body cell) rather than a markup or formatting change, so producers
keep writing a plain string with `\n` separators and no new serialisation concern.

**A unit test can only prove the markup and the stylesheet agree on a class name; it cannot prove the
rule applies.** That is the #303 trap exactly — the nav icon's class was present while the icon was
missing. The rendered proof is row 5, asserting the *computed* `white-space` and that a two-line body
occupies two client rects.

### 4. Define the per-type layout across both surfaces

**Status:** ✅ Done — turns rows 6–7 green

Each notification type gets a layout decision for the startup/popup dialog and for the
`/notifications` view. Six kinds exist as of 2026-09-01 — `Announcement`, `SchemaVersionOvershoot`,
`WhatsNew`, `ReseedRecommended`, `ReseedFileApplied`, `ImportReviewPending` — plus rows with no
metadata kind at all (#279, #289).

**Enumerate from the enum at implementation time, not from that list.** Row 6 derives its expectation
from `NotificationMetadataKind` itself, mirroring
`NotificationTableTests.EveryDisplayStatus_HasATranslationKey`, so a kind added later fails this test
rather than rendering unstyled.

### 5. Prove no storage change is introduced

**Status:** ✅ Done — turns row 8 green

#312 owns the schema; #319 owns the translation shape. This issue consumes both. If a layout need
appears to require a storage change, that is a finding to raise, not something to add here.

**Row 8 replaces a diff-reading instruction with an assertion.** `System_Notification`'s column set is
pinned by name, so a column added here fails a test rather than depending on a reviewer noticing.

### 6. Run the T2 document green, and confirm both call sites

**Status:** ✅ Done — turns rows 9–11 green

`NotificationSummary` (the startup modal) and the `/notifications` page both consume
`NotificationTable`. A change that looks right in one can be wrong in the other — the modal is
size-constrained in a way the page is not.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A notification with a title renders a title element | Unit test | `NotificationTableTests.ShowsTitle_WithATitle_IsTrue` |
| 2 | ✅ | A notification with no title renders none | Unit test | `NotificationTableTests.ShowsTitle_WithoutATitle_IsFalse` — `null`, `""` and whitespace, the shape #279/#289 rows have |
| 3 | ✅ | The title element is not rendered *instead of* the body | Unit test | `NotificationTableTests.ShowsTitle_WithoutATitle_StillRendersTheBody` — positive control, so row 2 cannot pass against a cell that renders nothing at all |
| 4 | ✅ | Markup and stylesheet agree on the body cell's class | Unit test | `NotificationTableTests.BodyCellClass_IsDefinedInTheStylesheet` — parses `NotificationTable.razor` and `.razor.css`; proves the two halves match, never that the rule applies |
| 5 | ✅ | A two-line body renders as two lines | Automated (T2) | new `13-notification-layout.md` — asserts computed `white-space: pre-line` **and** `getClientRects().length >= 2`, not the class name |
| 6 | ✅ | Every `NotificationMetadataKind` has a defined layout | Unit test | `NotificationTableTests.EveryMetadataKind_HasALayout` — derived from the enum, so a kind added later fails here |
| 7 | ✅ | A row with no metadata kind still has a layout | Unit test | `NotificationTableTests.NoMetadataKind_FallsBackToADefinedLayout` — negative case for row 6; #279/#289 rows carry none |
| 8 | ✅ | This issue adds no column to `System_Notification` | Unit test | `DatabaseInitializerOwnershipTests.SystemNotification_ColumnSet_IsPinned` — replaces "unchanged in the diff", which nothing re-runs |
| 9 | ✅ | Title and body render distinctly on `/notifications` | Automated (T2) + screenshot | `13-notification-layout.md` — the title is its own element in the DOM, not a prefix inside the body text |
| 10 | ✅ | The same holds in the startup modal | Automated (T2) + screenshot | same document — restart required, per #302's finding that the modal shows once per process run |
| 11 | ✅ | Rendering survives a degraded startup | Automated (T2) | same document — degraded container, parity with `/notifications`'s current behaviour rather than a bare `200` (see #303 row 36) |
| 12 | ✅ | The T2 document goes red before it goes green | Canary run | run at step 1 against `52071f24`, no worktree needed: `bodyCells: 0`, `titleElements: 0`, `whiteSpace: normal`, `lineBoxes: 1` — and step 1 itself failed on a wrong fixture assumption, corrected before implementing |
| 13 | ✅ | Every unit test above is wired to behaviour | Mutation | proven at step 1 by two opposing stubs: `ShowsTitle => false` fails rows 1, 4, 6, 7; `ShowsTitle => true` fails rows 2 and 3, which assert an absence and cannot fail against the first. Row 8 went red on a wrong column list before going green |
| 14 | ✅ | Build is clean | Build | `dotnet build --configuration Release` → 0 warnings, 0 errors |
| 15 | ✅ | No regression | Test run | `dotnet test --configuration Release -m:1` all green |
| 16 | ❌ | Every layout renders correctly on the developer's own machine | Live (T1) | one confirmed rendering per type, on both surfaces |

**Rows 5, 9 and 10 cannot be replaced by unit tests.** A unit test can prove the markup and the
stylesheet name the same class; only a rendered page proves the rule reaches the element. #303's nav
icon is the standing example — the class was present the whole time the icon was missing.

**Row 3 exists because row 2 asserts an absence.** Without it, a cell that renders nothing at all would
satisfy row 2 perfectly, which is the same trap `11-clean-reseed-confirmation.md`'s canary found in its
own step 1.
