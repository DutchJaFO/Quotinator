# #308 — Notification: multi-line/rich message layout

**Status:** In progress
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

## Reopened, 2026-09-02 — four findings from T1

The first pass shipped the structure (title element, `pre-line` body, both surfaces) and stopped there.
Reviewing it live produced four findings, all accepted as this issue's own work (developer decision):

1. **A resolved notification does not say how it was resolved.** The Status column reads *Done* while
   the body still reads "1 changes need your decision before they can be applied" — which stopped being
   true the moment it was decided. `NotificationSeeding` takes a `string body`, so the text is **frozen
   at write time**, and `DismissReason` records only *that* it resolved. **Which choice was made is
   stored nowhere** — `DecideBatchAsync` receives the `FieldResolutionChoice` and discards it.
2. **The metadata is stored and never rendered.** `Metadata` carries `fileName`, `origin`, `counts` and
   `batchId`; the body is a separately-composed sentence with those values baked in as arguments at
   write time. The structured payload is used only for identity and dismissal matching.
3. **The bodies are too long for a flat table row.** Presentation: **collapsible on the
   `/notifications` page, dialog in the startup modal** — each surface gets what suits it, rather than
   a modal inside a modal or an accordion inside a size-constrained popup.
4. **`Run` does not say what it will do**, and for a multi-outcome action the choices are hidden behind
   it. The available actions should be named on their own buttons.

**Step 4 was delivered thinly, and findings 2 and 3 are the consequence.** Step 4 says "define the
per-type layout across both surfaces". What it produced was `LayoutFor(kind) → BodyIsMultiLine`, a
line-wrapping boolean — the minimum that satisfied its own test. A per-type layout is what decides
*which parts of a payload a type shows*, which is exactly what finding 2 asks for.

**Step 5 is reversed by developer decision (2026-09-02).** It said this issue takes no storage change
of its own, and finding 1 cannot be rendered without one: the resolving choice does not exist in the
database. The reversal is deliberate and scoped — one nullable, enum-backed column with its CHECK
constraint per ADR 008, not a redesign. **Verification row 8's pinned column list is updated in the
same commit as the migration**, which is what makes the addition a decision rather than a drift.

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

**Status:** ✅ Done — turns rows 1–3 green

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

### 7. Write the tests for findings 1–4, and run them red

**Status:** ✅ Done — nine tests red across two opposing-stub runs

Same exit condition and the same two mechanics as step 1: every unit test fails on its own assertion,
and the T2 document's new steps fail against the current build. The document already exists, so this
run is not a fresh canary — it is the same proof applied to the rows being added, at the only moment it
is free.

**Absence assertions need the opposing stub, not just the wrong one.** Step 1 measured this: four tests
went red against `ShowsTitle => false` while the two asserting an absence needed `=> true`. Expect the
same split here and plan two runs rather than reporting the first as complete.


**Row 23 was mis-specified, and that is a planning failure — not a finding** (developer, 2026-09-02).
It asserted that payload rendering never replaces the one-line body. Wrong twice over: `NotificationEntity.Body`
is `init`-only so the compiler already guaranteed it, and **the premise was false** — for some types the
structured payload says everything the sentence says and says it better, so rendering both is
duplication. Whether a type shows its body, its payload, or both **is** the per-type layout decision,
which makes it the most testable thing in this step rather than the least.

Row 21 inherited the same wrong premise and is corrected with it: it demanded every kind name payload
parts, which a body-only type legitimately does not. It now asserts the invariant instead — `Content`
names payload exactly when `PayloadParts` is non-empty.

The lesson recorded for the next plan: a requirement written without checking it is expressible is not
a requirement, and deleting it later is not a finding.

**A storage-backed test cannot reach its assertion before its column exists** — recorded 2026-09-02
as a limit of step 1's mechanic. `DismissedAsResolved_RecordsTheResolution` and
`DismissedByUser_RecordsNoResolution` fail with *"table System_Notification has no column named
Resolution"*, not on an `Assert`. That is a genuine red — the behaviour is absent — but it is weaker
than an assertion failure, because it would look identical for a test asserting the opposite. Both
therefore need a mutation once the column lands: the negative especially, since it passes the moment
nothing writes the field.
### 8. Record how an action was resolved

**Status:** ⬜ Not started — finding 1; turns rows 17–20 green

A nullable, enum-backed `Resolution` column on `System_Notification`, written alongside `DismissReason`
whenever a notification is dismissed as `Resolved`. Enum-backed means a `CHECK` constraint in the same
migration per ADR 008, the baseline updated to match in the same commit, and both drift tests extended —
the checklist CLAUDE.md sets out for exactly this shape.

`NotificationActionExecutor` currently receives a `FieldResolutionChoice` and drops it after
`DecideBatchAsync`. That value is the resolution for an import-review alert; a reseed's is its own
member. **The rendered line is a translated key, never a stored sentence** — #319 owns the translation
shape, and a stored English string would be unreadable for a Dutch or German reader.

### 9. Render the payload rather than only the frozen sentence

**Status:** ⬜ Not started — finding 2; turns rows 21–23 green

`LayoutFor` grows from a boolean into a per-type description of *which parts of the payload that type
shows* — the per-entity breakdown for `ReseedFileApplied`, the per-status counts and batch for
`ImportReviewPending`, the highlight list for `WhatsNew`.

**The one-sentence body stays.** It is what the collapsed row and the API response show, it is already
translated, and replacing it would break every consumer reading `body`. The payload rendering is
*additional* detail, shown when expanded.

### 10. Collapse on the page, dialog in the modal

**Status:** ⬜ Not started — finding 3; turns rows 24–26 green

Developer decision, 2026-09-02. Collapsed state shows title plus the one-line body; expanded shows
step 9's payload detail. The modal opens the same content in a dialog instead, because a collapse inside
a size-constrained popup fights the popup.

**A circuit-free expander was considered and rejected as a false requirement** (developer, 2026-09-02).
The page is `@rendermode InteractiveServer`, so the filter buttons, Dismiss and Run already need the
circuit — an expander needing it adds nothing. The degraded case argues the other way too: on a
read-only data directory `/notifications` returns `500` because `InteractiveServer` cannot get
DataProtection to write `/data/keys`, so there is no page to expand. The original note here confused
"the database is degraded" with "there is no interactivity"; #326's exemption serves the route, and a
restricted set of allowed actions is not the same as a lost circuit.

### 11. Name the actions instead of "Run"

**Status:** ⬜ Not started — finding 4; turns rows 27–29 green

A per-trigger label — reseed, reset — and for a multi-outcome action, both choices offered directly
rather than behind a generic button. Labels are translated keys in all three files.

**The confirmation step stays.** #367's T1 confirmed it works and it is what makes an irreversible
action deliberate; naming the button is not a reason to remove the second step.

### 12. Run the T2 document green across both surfaces

**Status:** ⬜ Not started — turns rows 30–31 green


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
| 17 | ❌ | A notification resolved by an action records which resolution it was | Unit test | `NotificationWriterTests.DismissedAsResolved_RecordsTheResolution` |
| 18 | ❌ | A notification dismissed by the user records no resolution | Unit test | `NotificationWriterTests.DismissedByUser_RecordsNoResolution` — negative; the field means "how the action settled it", not "how it went inactive" |
| 19 | ❌ | The migration and the baseline accept the same `Resolution` values | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` (extended) |
| 20 | ❌ | Every `NotificationResolution` member has a label in all three locales | Unit test | `NotificationTableTests.EveryResolution_HasATranslationKey` — derived from the enum, like `EveryDisplayStatus_HasATranslationKey` |
| 21 | ❌ | Each type's layout is internally consistent about what it renders | Unit test | `NotificationTableTests.LayoutFor_EachKind_NamesWhatItRenders` — `Content` names payload iff `PayloadParts` is non-empty; proven by mutation, since an invariant passes while it holds |
| 22 | ❌ | A payload that cannot be deserialised renders the plain body rather than throwing | Unit test | `NotificationTableTests.UnreadablePayload_FallsBackToTheBody` — negative; a row written by an older build must still render |
| 23 | ❌ | The choice of body, payload, or both actually varies by type | Unit test | `NotificationTableTests.LayoutFor_AcrossKinds_ActuallyVariesWhatIsRendered` — replaces the mis-specified row, asserting what genuinely varies |
| 24 | ❌ | The page collapses and expands | Automated (T2) | `13-notification-layout.md` — click the expander, assert the payload detail appears |
| 25 | ❌ | The modal opens a dialog rather than expanding in place | Automated (T2) + screenshot | same document — restart, then open one notification's detail |
| 26 | ❌ | Collapsed state shows the title and the one-line body, never the payload detail | Automated (T2) | same document — the detail is absent from the DOM or hidden, asserted before expanding |
| 27 | ❌ | Each executable trigger's button is named for what it does | Unit test | `NotificationTableTests.ActionLabelFor_EachExecutableTrigger_IsNamed` — derived from `NotificationDismissTrigger`, so a new one fails here |
| 28 | ❌ | A multi-outcome action offers both choices without an intermediate click | Unit test | `NotificationTableTests.ImportReviewResolved_OffersBothChoicesDirectly` |
| 29 | ❌ | The confirmation step still stands | Automated (T2) | same document — a named button still requires confirming before it executes, per #367's T1 |
| 30 | ❌ | Every layout renders on both surfaces | Automated (T2) + screenshot | same document, run whole after implementation |
| 31 | ❌ | The new T2 steps go red before they go green | Canary run | run at step 7 against the pre-implementation build, recorded in the document's *Canary* section |

**Rows 5, 9 and 10 cannot be replaced by unit tests.** A unit test can prove the markup and the
stylesheet name the same class; only a rendered page proves the rule reaches the element. #303's nav
icon is the standing example — the class was present the whole time the icon was missing.

**Row 3 exists because row 2 asserts an absence.** Without it, a cell that renders nothing at all would
satisfy row 2 perfectly, which is the same trap `11-clean-reseed-confirmation.md`'s canary found in its
own step 1.
