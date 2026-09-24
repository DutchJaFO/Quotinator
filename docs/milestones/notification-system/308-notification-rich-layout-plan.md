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
3. **The bodies are too long for a flat table row.** Presentation: **dialog on the `/notifications`
   page, collapsible in the startup popup** — the page is a long scannable history where rows growing
   and shrinking move everything below them, while the popup already has the reader's whole attention
   and a dialog over it would be a modal on a modal. **Revised 2026-09-02**: the first answer was the
   other way round, and was built and shipped that way before being swapped.
   Detail renders as a **table**, not a list — every entry has the same shape, so columns line the
   numbers up where a bulleted sentence per row does not.
4. **`Run` does not say what it will do**, and for a multi-outcome action the choices are hidden behind
   it. The available actions should be named on their own buttons — **reusing the names the actions
   already have, not new ones** (developer, 2026-09-02).

   This took two corrections, both the same mistake. The first attempt wrote *Reload the quotes* while
   the body in that very row read "Run a reseed to load the bundled sources" — a synonym for the
   domain's own word, two lines apart. The second wrote *Run a reseed*, still invented. The endpoints
   have carried the answer all along: `.WithSummary("Reseed the database")` and
   `.WithSummary("Reset the database")` on `AdminEndpoints`, and `ImportReviewDecideColumn` —
   *"Decide"* — is already the review page's own heading for these very controls.

   Labels are now those strings verbatim: **Reseed the database**, **Reset the database**, **Decide**.
   The resolution lines follow the same rule: *Database reseeded*, *Database reset*. **Before writing a
   button label, look for the name the operation already has** — an endpoint summary, a column heading,
   an existing UI key.

**A shared `ModalDialog` control, extracted after building the same shell three times** (developer,
2026-09-02: *"this implies you did not use a control despite having at least 2 situations where a modal
popup dialog was needed"*). `StartupSuccessModal`, `StartupErrorModal` and the notification detail popup
each hand-rolled a backdrop, a centred dialog, and a header/body/footer layout.

The cost was already measurable before the point was made: the two startup modals were given a 95vh cap
in one commit, and the detail popup needed the identical fix **one message later**, because each copy
had to be found and corrected separately — precisely the drift CLAUDE.md's duplication rule describes.
The third copy is what made it obvious; the second should have.

`ModalDialog` now owns the backdrop, the cap and the scroll region, and the cap is deliberately **not**
a parameter: a dialog taller than the viewport puts its own footer off-screen, which on the startup
modal means Continue cannot be reached at all. No caller has a reason to opt out.

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

## Reopened, 2026-09-22 — the per-type layout was never implemented

Step 4 says each type gets a layout decision across both surfaces. What exists is
`NotificationTable.LayoutFor(kind) → NotificationLayout(BodyIsMultiLine, PayloadParts)`, and **no
renderer reads it.** The markup calls `PayloadDetail(notification, Text)`, which switches on the
deserialized payload type and consults no layout at all. Verified by searching the repository: the only
references to `LayoutFor` outside its own declaration are in `NotificationTableTests`.

**The commits show how it happened, and none of it is a later refactor orphaning the map:**

| Commit | What it did |
|---|---|
| `69c01776` | `LayoutFor` born as `BodyIsMultiLine` alone — a line-wrapping boolean |
| `040a14d6` | `PayloadParts` added to the record with every arm empty, plus the tests that assert it |
| `a799e91a` | The rendering built as `PayloadLines(notification, text)`, switching on the payload type — never reading the map |
| `288ccb3a` | `PayloadLines` replaced by today's `PayloadDetail` table: same type switch, same absence |

**The red/green sequence certified a table edit, not a feature** (developer, 2026-09-22).
`LayoutFor_AcrossKinds_PayloadDetailVaries` asserts the map's own contents: red while every arm read
`[]`, green once two arms said `["counts"]`. Neither state required a renderer to exist. There was never
a test of the feature, so there was never a red the feature could turn green — which is why row 23 is
ticked for something that was not built. `BodyIsMultiLine` is the same defect one step earlier: nothing
reads it, and every kind keeps its line breaks regardless because `.notification-body` is
`white-space: pre-line`.

**Why no analyzer reported it:** the tests reference `LayoutFor`, and `Quotinator.Api.Tests` has
`InternalsVisibleTo`, so the member counts as used. A test can keep dead production code alive, and here
it did.

**How this is finished** (developer direction, 2026-09-22): complete #308 by proving every feature it
defines is useful and testable, across both surfaces and every variant. The map's fate follows from that
evidence instead of being decided up front — if the per-variant assertions can be written without it, it
is deleted; if any of them needs a declared expectation the payload type cannot supply, the map becomes
that source and the renderer reads it. **Coverage is derived from `NotificationMetadataKind`, never from
a written list, so defining a new content variant fails these tests until it is handled** — the property
step 4 already claimed and row 6 already has.

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

### 5. Pin the column set, so storage changes only by decision

**Status:** ✅ Done — turns row 8 green. **Retitled 2026-09-02**; see below.

`System_Notification`'s column set is pinned by name, replacing a diff-reading instruction with an
assertion that re-runs.

**This step originally read "Prove no storage change is introduced", and the reopening reversed it.**
Finding 1 cannot be rendered without storing the resolving choice, so the issue now adds exactly one
column (step 8). The pin did its job at that moment: adding `Resolution` failed row 8 until the list
was updated in the migration's own commit, which is the difference between a decision and a drift.

#312 still owns the schema and #319 the translation shape; this issue consumes both and extends
neither beyond that single column. A second column would be a finding to raise, not something to add.

### 6. Run the T2 document green, and confirm both call sites

**Status:** ✅ Done — turns rows 9–11 green

`NotificationSummary` (the startup modal) and the `/notifications` page both consume
`NotificationTable`. A change that looks right in one can be wrong in the other — the modal is
size-constrained in a way the page is not.

---

### 7. Write the tests for findings 1–4, and run them red

**Status:** ✅ Done — eight tests red across two opposing-stub runs

Same exit condition and the same two mechanics as step 1: every unit test fails on its own assertion,
and the T2 document's new steps fail against the current build. The document already exists, so this
run is not a fresh canary — it is the same proof applied to the rows being added, at the only moment it
is free.

**Absence assertions need the opposing stub, not just the wrong one.** Step 1 measured this: four tests
went red against `ShowsTitle => false` while the two asserting an absence needed `=> true`. Expect the
same split here and plan two runs rather than reporting the first as complete.


**Row 23 was mis-specified twice, and both were planning failures — not findings** (developer,
2026-09-02).

**First**, it asserted that payload rendering never replaces the one-line body — true, but expressed as
a non-mutation of `NotificationEntity.Body`, which is `init`-only, so the compiler already guaranteed
it. The row could never go red. Deleting it was reported as a finding; it was the plan being wrong.

**Second**, the correction over-reached. Replacing it with a `BodyOnly`/`PayloadOnly`/`BodyAndPayload`
choice assumed some type might show detail *instead of* its summary. **The body is always relevant —
it is the summary of the payload wherever there is one** (developer). So `PayloadOnly` describes
nothing real, and the original premise was right after all; only its expression had been wrong.

What actually varies is whether a type has structured detail *beneath* the summary. `NotificationLayout`
is therefore `BodyIsMultiLine` plus `PayloadParts`, with no content-mode enum, and the two rows became:
row 21, every type leads with its body; row 23, whether payload detail exists varies by type.

**The lesson, recorded rather than filed away:** a requirement written without checking it is
expressible is not a requirement — and a correction made without re-checking the premise is how one
wrong row becomes two.

**A storage-backed test cannot reach its assertion before its column exists** — recorded 2026-09-02
as a limit of step 1's mechanic. `DismissedAsResolved_RecordsTheResolution` and
`DismissedByUser_RecordsNoResolution` fail with *"table System_Notification has no column named
Resolution"*, not on an `Assert`. That is a genuine red — the behaviour is absent — but it is weaker
than an assertion failure, because it would look identical for a test asserting the opposite. Both
therefore need a mutation once the column lands: the negative especially, since it passes the moment
nothing writes the field.

### 8. Record how an action was resolved

**Status:** ✅ Done — rows 17–20 green

**The schema half was pulled forward out of order, and that is a correction to step 7's mechanic rather
than a shortcut** (2026-09-02). Step 7 added `NotificationEntity.Resolution` as though it were a stub.
It is not: `ReflectedColumnMetadata` builds every INSERT's column list from the entity's properties, so
the property without its column broke **76 tests** — every notification write in the solution, not the
two this row is about. An entity property is a schema contract, so the column, the baseline, the two
drift tests and the pinned column list all landed with it.

What remains is the behaviour: `NotificationWriter` storing the value, and `NotificationActionExecutor`
passing the `FieldResolutionChoice` it currently drops after `DecideBatchAsync`. A reseed and a reset
each supply their own member.

**The rendered line is a translated key, never a stored sentence** — #319 owns the translation shape,
and a stored English string would be unreadable for a Dutch or German reader.

A nullable, enum-backed `Resolution` column on `System_Notification`, written alongside `DismissReason`
whenever a notification is dismissed as `Resolved`. Enum-backed means a `CHECK` constraint in the same
migration per ADR 008, the baseline updated to match in the same commit, and both drift tests extended —
the checklist CLAUDE.md sets out for exactly this shape.

`NotificationActionExecutor` currently receives a `FieldResolutionChoice` and drops it after
`DecideBatchAsync`. That value is the resolution for an import-review alert; a reseed's is its own
member. **The rendered line is a translated key, never a stored sentence** — #319 owns the translation
shape, and a stored English string would be unreadable for a Dutch or German reader.

### 9. Render the payload rather than only the frozen sentence

**Status:** ✅ Done — rows 21–23 green

`LayoutFor` grows from a boolean into a per-type description of *which parts of the payload that type
shows* — the per-entity breakdown for `ReseedFileApplied`, the per-status counts and batch for
`ImportReviewPending`, the highlight list for `WhatsNew`.

**The one-sentence body stays.** It is what the collapsed row and the API response show, it is already
translated, and replacing it would break every consumer reading `body`. The payload rendering is
*additional* detail, shown when expanded.

### 10. Collapse on the page, dialog in the modal

**Status:** ✅ Done — rows 24–26 green

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

**Status:** ✅ Done — rows 27–29 green

A per-trigger label — reseed, reset — and for a multi-outcome action, both choices offered directly
rather than behind a generic button. Labels are translated keys in all three files.

**The confirmation step stays.** #367's T1 confirmed it works and it is what makes an irreversible
action deliberate; naming the button is not a reason to remove the second step.

### 12. Run the T2 document green across both surfaces

**Status:** ✅ Done — rows 30–32 green

**Running it found three defects the unit tests had all passed over**, each recorded in the document:
`Reseeded`/`Reset` were defined, translated and never written (only the by-batch dismissal was wired);
the read query's explicit column list omitted `Resolution`, so the value was stored and invisible to
every consumer; and the first fix set the resolution *after* `ReseedAsync`, by which point
`ApplyBatchAsync` had already dismissed the row. Rows 17–18 missed all three because both read with raw
`SELECT *` and both exercised only the by-batch path.

**All three were fixed at the time and none got a test, which is its own finding** (2026-09-02). The
plan recorded them in prose and moved on, so nothing re-ran to catch a regression — against
`docs/testing-policy.md`'s rule that a bug fix ships with the test that would have caught it. Rows
34–36 close that, each proven red by reproducing the original defect: the resolution argument dropped,
and `n.Resolution` removed from the read query.

**The root cause of the third was in the test doubles, not the executor.** `FakeNotificationWriter` and
`SqliteImportActionServiceTests`' own recording writer both accepted `NotificationResolution?` and
recorded only the trigger — so *every* assertion about a dismissal passed while the caller sent
nothing. A fake that accepts a parameter and stores only part of it reports a partial call as a
complete one; both now record the whole call.

### 13. Extract the modal shell all three surfaces were duplicating

**Status:** ✅ Done — rows 33–34 green

Developer instruction, 2026-09-02. Not a refactor found by looking: this issue's own detail popup was
the third hand-built copy of the same backdrop-plus-centred-dialog, and the second and third had each
been given the `95vh` cap separately, one message apart.

`ModalDialog` owns the backdrop, the cap and the scroll region; `StartupSuccessModal`,
`StartupErrorModal` and the detail popup pass content into it. **The cap is not a parameter** — a
dialog taller than the viewport puts its own footer off-screen, which on the startup modal means
Continue cannot be reached, so no caller has a reason to opt out.

**Row 34 is the part that lasts.** Row 33 proves the cap holds today; a fourth modal built by copying
the markup would satisfy it while being the same defect, so row 34 asserts the copied class names are
absent instead.

**This step is recorded after the fact, and that is the finding.** The extraction has no red-first test
sequence of its own because it was not planned — it was the third copy being noticed. Step 4's
under-delivery is the same shape one layer up: both are the plan's steps describing an outcome without
naming the structure that produces it.

### 14. Assert what each variant renders, on both surfaces

**Status:** ✅ Done — written as
[`14-every-kind-renders-what-its-layout-promises.md`](../../automated-testing/notifications-and-changelog/14-every-kind-renders-what-its-layout-promises.md),
added to `Quotinator.slnx` and the suite index, and run green on 2026-09-23 against an image built from
this branch. Its *Observed effect* carries the per-kind results.

**Each kind arrives through its own trigger, not a constructed row** (developer direction,
2026-09-23) — a constructed row proves rendering and nothing about the producer. A cold start with the
conflict fixture produces four kinds, a consumer version one past the build produces the fifth, and a
reset produces the sixth.

**Three instrument defects were found and fixed while running it**, each of the class this suite keeps
recording: `@($p.counts).Count` reads `1` for a payload with no `counts` at all; an unfiltered
`tbody tr` counts the rows *inside* an expander's detail table (`49` where there are `9`); and the
reseed recommendation is resolved by the restart that would have shown it in the popup, so step 8 gives
that kind a container with nothing to seed.

**One undocumented surface difference, recorded and worth a decision:** the startup popup passes
neither `ShowActionColumn` nor `ShowDismissAction`, so it is entirely read-only, where the page offers
both. The behaviour is coherent — the popup informs, the page acts — but unlike `DetailAsDialog` it
carries no comment saying so at the call site.

A new T2 document, *Every notification kind renders what its layout promises, on both surfaces*. One row
per kind, constructed with the container stopped — the technique
[`01-notification-system.md`](../../automated-testing/notifications-and-changelog/01-notification-system.md)
step 7 already uses for rows no producer creates — each with a payload valid for its kind, then read on
`/notifications` and, after a restart, in the startup popup:

- every kind renders a non-empty body, with its title as its own element;
- a detail control appears for exactly the kinds whose payload holds something their body does not, and
  for no others — a dialog on the page, an expander in the popup;
- the action button reads the operation's own name, and a kind with no action offers none;
- the status word for each row.

The second assertion is the one this reopening exists for: it is the per-type decision step 4 claimed,
stated as what a reader sees. **`ReseedRecommended` and `SchemaVersionOvershoot` have never been
rendered under assertion at all** — both are covered through the API only, by
`10-reseed-recommendation-and-action.md` and `startup-and-degradation/06`.

### 15. Make a new variant fail these tests until it is handled

**Status:** ✅ Done — `NotificationTableTests.EveryLiveNotificationKind_IsNamedInTheVariantDocument`
reads the document and asserts it names every `NotificationMetadataKind` member. Proven wired by
mutation, 2026-09-23: red with the document renamed away (*"the per-kind rendering document is
missing"*), red again with one kind's name replaced in it (*"SchemaVersionOvershoot is never named in
the per-kind rendering document"*), green with the document intact — 61 tests in the group.

**The unit half already exists, and it did not when step 4 was written.** #373 added
`NotificationTableTests.RendersDetail` — a per-kind expectation declared in the tests — with
`EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares` asserting `PayloadDetail`'s real output for
each kind in both directions, and `EveryMetadataKind_DeclaresWhetherItRendersDetail` failing when a kind
is added and not declared. That decision is derived from the payload type; `LayoutFor` plays no part in
it, which is the evidence step 16 weighs.

What is missing is the same guarantee for the live document: a document cannot enumerate a C# enum, so
a guard test reads it and asserts it names every `NotificationMetadataKind` member — the shape
`RepositoryStructureTests` already uses to hold documents to a rule. Defining a new kind then fails the
build until the document covers it too.

### 16. Resolve the map from what steps 14 and 15 needed

**Status:** ✅ Done — **deleted**, by the criterion below: not one assertion in steps 14 or 15 needed it.
Every per-kind expectation is derived from the payload's own type — the live document reads each row's
`counts`, and `EveryMetadataKind_WithItsOwnPayload_RendersWhatItDeclares` reads what `PayloadDetail`
returns. `NotificationLayout`, `LayoutFor` and `BodyIsMultiLine` are gone from
`NotificationTable.razor.cs`.

**Red first, by its own guard.** `NotificationTableTests.TheRenderingDecision_HasExactlyOneSource`
asserts the component's source names `PayloadDetail` and neither `LayoutFor` nor `BodyIsMultiLine`: red
before the deletion (*"LayoutFor declares a per-kind layout no renderer reads"*), green after. It
forbids the reintroduction rather than the values, because the defect was the second source existing at
all.

**Two tests went with it, their intent kept:** `EveryMetadataKind_HasALayout` asserted every kind had a
map *entry* — a property of the map, replaced by
`EveryMetadataKind_DeclaresWhetherItRendersDetail` plus the per-kind rendering assertion;
`NoMetadataKind_FallsBackToADefinedLayout` became `NoMetadataKind_RendersItsBodyAndNoDetail`, which
reads the renderer instead of the map. `LayoutFor_AcrossKinds_PayloadDetailVaries` became
`PayloadDetail_AcrossKinds_Varies`, asserting the same claim over real output.

### 17. Re-run every document the change touched

**Status:** ✅ Done — 2026-09-23, against an image rebuilt with the map removed. Deleting it changed
nothing a reader sees, which is the claim that had to be measured rather than assumed:

| Document | Result |
|---|---|
| *Every notification kind is produced by its own trigger…* | Page and popup byte-for-byte as before the deletion: 8 rows, 6 detail dialogs, 0 expanders on the page; 6 expanders, 0 dialogs, no controls in the popup; `pre-line` honoured |
| *A notification renders its title and body as separate things* | Every step: 14 rows, 1 untitled, 0 empty bodies, `4 > 2` line boxes, buttons `Details`/`Dismiss`/`Decide` and no `Run`, 12 open buttons, 0 expanders; dialog `364` in a `420` viewport, within it, body scrolling, footer visible, no bespoke markup; `Decide` asks first (`1` pending), then *Took the values from the file* with `0`; modal with 11 expanders, collapsed by default, 8 headings, `744` in `800`; degraded `503`/`500`/`200` |
| *Notifications list, dismiss, render, and drive their action* | 7 rows, `401`/`404`, the tag declared with a description, page renders the announcement body; every row dismissed → `0` rows and *No notifications match this filter*; four constructed rows reading **Active**, **Expired**, **Dismissed**, **No longer applicable**, the action button only on the actionable row |
| *A running notification action says so…* | `Reseed the database` → `Confirm`/`Cancel` → spinner with both controls withdrawn → **Done** with *Database reseeded*, 795 quotes |
| *A file left awaiting review raises an alert…* | 2 pending / 2 active; alert names its file, origin and batch; both files in the startup modal; discard → `resolved`; two reseeds → three alerts, 2 active, 2 pending; the resolved one reads **Done**; both surfaces' *Take incoming* leave their batch `Applied` with the stored text replaced and `0` active alerts; degraded `503`/`500`/`500`/`200`/`200` |

**One instrument defect found and recorded in the index:** a fit assertion measured against a
zero-height pane reports every element off-screen. The popup read `withinViewport: false` and
`footerVisible: false` with `window.innerHeight: 0`; at an explicit `1100x800` the same popup with all
eleven rows expanded was `744` tall, inside the viewport, scrolling, footer visible. Both this document
and *A notification renders its title and body* now say to read `window.innerHeight` alongside any such
assertion.

Full suite afterwards: **4158 tests, 0 failures, 0 warnings**.

Decided by evidence, against a stated criterion rather than preference:

- **If every assertion in steps 14 and 15 can be written from the payload's own type**, `LayoutFor`,
  `NotificationLayout` and `BodyIsMultiLine` are deleted, and `PayloadDetail` remains the single source.
  Row 6's property moves to `PayloadDetail_ForEveryKind_IsSelfDescribing`, which already iterates every
  kind.
- **If any assertion needs an expectation the payload type cannot supply**, the map is that expectation:
  `PayloadDetail` reads it, and a unit test asserts the rendered output matches what the map declares —
  so the declaration can never again be green while unread.

Either way `BodyIsMultiLine` goes: no renderer can read it without changing what line breaks do, and
`white-space: pre-line` already gives every kind the behaviour it claims to request.

### 18. Capture every kind, because row 16 was asking T1 for something T1 cannot do

**Status:** ✅ Done — 2026-09-24, row 43 green.

Row 16 read *"Every layout renders correctly on the developer's own machine — one confirmed rendering
per type, on both surfaces"*, and the developer could not run it: four of the six kinds cannot be
produced in a working database without rolling its schema version forward, resetting it, or staging a
conflict against it. That is not a gap in the developer's environment. It is a row written against the
wrong tier — `docs/release-verification.md` says **"T1 confirms the thing still starts. That is its
whole job,"** and **"T2 verifies what this issue actually targeted."** A six-kind, two-surface sweep is
the second sentence, not the first.

So row 16 keeps T1's actual job and the sweep moves to T2 as row 43. Nothing is verified less: the
document already produced every kind through its own trigger and asserted what each one renders. What
it did not do was let anyone *see* it, which is what row 16 was really asking for — and the index
already allows for that, requiring only that the assertion beside a screenshot be machine-checkable.

**The suite had no way to write a screenshot to a file.** The index has allowed one as evidence since
#339, provided the assertion beside it is machine-checkable — but the picture only ever existed inside
whichever tool was driving the browser, so it could not be attached to a run, compared with a later one,
or shown to anyone. A capture that cannot be repeated is the same problem as a screenshot nobody can
see, and this suite is re-run whole at every milestone close.

So the capability came first: `scripts/testing/capture-page.csx` drives headless Edge over the DevTools
Protocol — no NuGet package, no driver binary. It loads a URL, evaluates the step's own JavaScript,
prints what that returns, and writes the PNG; a `{x, y, width, height}` return value crops the shot to
that element, so a row's own `getBoundingClientRect()` is what bounds its image. One call produces both
halves, which is what stops the assertion and the picture describing different moments.

Fifteen images, and four things they established that no assertion in this plan had stated:

- The dialog's counts table carries a bold **Total** in its `tfoot`, so a step counting `tbody tr`
  reads `2` where three lines render — and would keep passing if the totals stopped adding up. The
  document now reads `tfoot` separately.
- The page's review row carries a *Review each change* link beside its `Decide` button; the same kind
  in the popup carries neither.
- The startup popup is per browser session, not per application run — two fresh sessions against one
  running container both rendered it, so the per-kind popup captures need no restart between them.
- **A capture that killed its browser left every Blazor circuit half-open**: thirteen captures produced
  72 `WebSocketException`s against a container that had logged `0`. The script now closes the browser
  over the protocol; re-measured, `0` before three captures and `0` after. An instrument that adds
  exceptions to the log a document reads for exceptions is a defect in the instrument, not a cost of
  capturing.

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
| 8 | ✅ | `System_Notification`'s column set changes only by decision | Unit test | `DatabaseInitializerOwnershipTests.SystemNotification_ColumnSet_IsPinned` — replaces "unchanged in the diff", which nothing re-runs. **Requirement revised 2026-09-02**: it read "adds no column", which the step 5 reversal overturned. The list now includes `Resolution` and was updated in the migration's own commit, which is what makes the addition deliberate |
| 9 | ✅ | Title and body render distinctly on `/notifications` | Automated (T2) + screenshot | `13-notification-layout.md` — the title is its own element in the DOM, not a prefix inside the body text |
| 10 | ✅ | The same holds in the startup modal | Automated (T2) + screenshot | same document — restart required, per #302's finding that the modal shows once per process run |
| 11 | ✅ | Rendering survives a degraded startup | Automated (T2) | same document — degraded container, parity with `/notifications`'s current behaviour rather than a bare `200` (see #303 row 36) |
| 12 | ✅ | The T2 document goes red before it goes green | Canary run | run at step 1 against `52071f24`, no worktree needed: `bodyCells: 0`, `titleElements: 0`, `whiteSpace: normal`, `lineBoxes: 1` — and step 1 itself failed on a wrong fixture assumption, corrected before implementing |
| 13 | ✅ | Every unit test above is wired to behaviour | Mutation | proven at step 1 by two opposing stubs: `ShowsTitle => false` fails rows 1, 4, 6, 7; `ShowsTitle => true` fails rows 2 and 3, which assert an absence and cannot fail against the first. Row 8 went red on a wrong column list before going green |
| 14 | ✅ | Build is clean | Build | `dotnet build --configuration Release` → 0 warnings, 0 errors |
| 15 | ✅ | No regression | Test run | `dotnet test --configuration Release -m:1` all green |
| 16 | ❌ | The application starts in Visual Studio and the notifications page renders | Live (T1) | developer confirms startup without error and the page rendering whichever kinds that database holds — the per-type sweep is row 43, see step 18 |
| 17 | ✅ | A notification resolved by an action records which resolution it was | Unit test | `NotificationWriterTests.DismissedAsResolved_RecordsTheResolution` — proven by mutation (hard-coding a resolution fails it) |
| 18 | ✅ | A notification dismissed by the user records no resolution | Unit test | `NotificationWriterTests.DismissedByUser_RecordsNoResolution` — negative; the field means "how the action settled it", not "how it went inactive". Wired via `Sql.Notifications.UpdateDismissById`: the by-batch mutation does **not** reach this path, so proving it needed the by-id query mutated instead |
| 19 | ✅ | The migration and the baseline accept the same `Resolution` values | Unit test | `DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues` — extended with all four members and a rejected value, on both paths |
| 20 | ✅ | Every `NotificationResolution` member has a label in all three locales | Unit test | `NotificationTableTests.EveryResolution_HasATranslationKey` — derived from the enum, like `EveryDisplayStatus_HasATranslationKey`; `TranslationCompletenessTests` covers the other two locales |
| 21 | ✅ | A type that renders detail also names its columns | Unit test | `NotificationTableTests.PayloadDetail_ForEveryKind_IsSelfDescribing`. **Un-ticked 2026-09-02, found during #373: it could not fail.** Two independent causes, either sufficient on its own — the `WithTitle` helper never set `MetadataKind`, which `PayloadDetail` dispatches on; and `MetadataFor` never emitted `releaseState`, which `NotificationMetadataDto` declares `required`, so deserialization threw and `TryDeserialize` swallowed it. Every fixture yielded an empty table, and the assertion `AreEqual(rows > 0, headers > 0)` held as `false == false` for every kind. The rendering was correct throughout; the test measured nothing. #373 fixes both fixtures and adds enum-derived positive and negative coverage per kind — headers exist exactly when rows do, and every row's cell count matches. Replaced `ContentLines_ForEveryKind_LeadWithTheBody` when detail became a table: the body is now unconditional markup, so its precedence is proven by the T2 document, not by a unit assertion that cannot fail. **Re-ticked 2026-09-23**, proven by mutation: returning the reseed breakdown's rows with no headings fails it — *"ReseedFileApplied has 1 row(s) and 0 column heading(s)"* |
| 22 | ✅ | A payload that cannot be deserialised renders the plain body rather than throwing | Unit test | `NotificationTableTests.UnreadablePayload_FallsBackToTheBody` — negative; a row written by an older build must still render. Proven by mutation: making the fall-through arm return a line fails it |
| 23 | ✅ | Whether a type has structured detail beneath the summary varies | Unit test | **Un-ticked 2026-09-22.** `NotificationTableTests.LayoutFor_AcrossKinds_PayloadDetailVaries` asserts the contents of `LayoutFor`'s own map, which no renderer reads — red while every arm was empty, green once two arms were filled in, neither state requiring the feature. Row 38 replaces it with what a reader sees. **Superseded and closed 2026-09-23**: the claim is asserted over rendered output by rows 38 and 40, and over `PayloadDetail`'s own result by `PayloadDetail_AcrossKinds_Varies`; the map it read is deleted |
| 24 | ✅ | The page opens payload detail in a dialog | Automated (T2) + screenshot | `13-notification-layout.md` step 7 — `expandersOnPage: 0`, `openButtonsOnPage: 4`; the dialog carries the table. **Swapped 2026-09-02**: the page originally collapsed and the popup opened a dialog |
| 25 | ✅ | The startup popup expands detail in place | Automated (T2) | same document step 8 — `expandersInModal: 4`, `openButtonsInModal: 0`, `collapsedByDefault: true` |
| 26 | ✅ | Detail renders as a table, never a list | Automated (T2) | same document step 7 — headings `Entity / Added / Updated` over `Quote \| 63 \| 0`; `listsAnywhere: 0` guards the regression |
| 27 | ✅ | Each executable trigger's button is named for what it does | Unit test | `NotificationTableTests.ActionLabelFor_EachExecutableTrigger_IsNamed` — derived from `NotificationDismissTrigger`, so a new one fails here. Confirmed live: the button reads *Reseed the database* — the endpoint summary verbatim. **Corrected twice on 2026-09-02**: *Reload the quotes*, then *Run a reseed*, both invented while the operation already had a name |
| 28 | ✅ | A multi-outcome action offers both choices without an intermediate click | Unit test | `NotificationTableTests.ImportReviewResolved_OffersBothChoicesDirectly` |
| 29 | ✅ | The confirmation step still stands | Automated (T2) | same document step 9 — clicking the named button yields `Confirm`/`Cancel`, and nothing runs until Confirm |
| 30 | ✅ | Every layout renders on both surfaces | Automated (T2) + screenshot | **Un-ticked 2026-09-22.** The document's steps 7–10 read whichever kinds its fixture happened to produce — `ReseedRecommended`, `SchemaVersionOvershoot` and a row with no metadata kind have never been rendered under assertion on either surface. Rows 39–40 cover every kind, derived from the enum. **Re-ticked 2026-09-23** on that basis: all six kinds reached both surfaces through their own triggers, and *A notification renders its title and body* was re-run whole against the same build |
| 31 | ✅ | The new T2 steps go red before they go green | Canary run | `quotinator:canary308b` at `7bc5bacc`: `0` expanders, `0` open buttons, a button reading `Run`, `0` resolution lines. Step 9 needed an actionable row first — it passed vacuously without one |
| 32 | ✅ | A resolved notification says how it was resolved | Automated (T2) | same document step 10 — the resolved row reads *Database reseeded*, and `GET /notifications` returns `resolution: reseeded` |
| 33 | ✅ | No modal grows past the viewport, on any surface | Automated (T2) + screenshot | same document steps 7 and 8 — every modal renders through `ModalDialog`, capped at `95vh` with a scrolling body. Measured on a deliberately short `420px` viewport: detail popup `364`, startup popup `364`, both scrolling with header and footer reachable. **Found twice in T1 (developer, 2026-09-02)**: once on the startup popup, then again on the detail popup — which is what forced the shared control |
| 34 | ✅ | The reseed and reset paths record their resolution, not only their trigger | Unit test | `NotificationActionExecutorTests.ExecuteAsync_Reseed_...` and `..._DatabaseReset_...` — proven by mutation: dropping the resolution argument, as the shipped code originally did, fails both. **Added 2026-09-02** to close the gap step 12 exposed |
| 35 | ✅ | The stored resolution survives the read path | Unit test | `NotificationWriterTests.DismissedAsResolved_TheResolutionSurvivesTheReadPath` — reads through `NotificationReader`, not `SELECT *`. Proven by mutation: removing `n.Resolution` from the read query's column list fails it |
| 36 | ✅ | The by-trigger dismissal writes the resolution, not only the by-batch one | Unit test | `NotificationWriterTests.DismissedByTrigger_RecordsTheResolution` — the path reseed and reset actually take, which rows 17–18 did not exercise |
| 37 | ✅ | A fourth modal cannot reintroduce the duplication unnoticed | Automated (T2) | same document step 7 — `usesOldBespokeMarkup: false`, asserting the copied class names absent. **A regression guard, not a proven-red row**: the markup it forbids existed while the assertion did not, so unlike row 31 it has no canary run behind it, and the document says so |
| 38 | ✅ | A detail control appears for exactly the kinds whose payload adds to their body | Automated (T2) | *Every notification kind renders what its layout promises*, step 3 — one row per kind; a dialog on the page for those kinds and none for the rest, asserted per kind rather than by a count |
| 39 | ✅ | Every kind renders its body and its title on the page | Automated (T2) | same document, step 2 — for each kind a non-empty `.notification-body`, and a `.notification-title` that is not part of it |
| 40 | ✅ | Every kind renders the same way in the startup popup | Automated (T2) | same document, step 4, after a restart — an expander rather than a dialog, and the same per-kind detail decision as row 38 |
| 41 | ✅ | A kind defined later fails these tests until it is covered | Unit test | `NotificationTableTests.EveryMetadataKind_DeclaresWhetherItRendersDetail` (exists, #373) plus a new guard asserting the live document names every `NotificationMetadataKind` member |
| 42 | ✅ | The rendering decision has exactly one source | Unit test | `NotificationTableTests.TheRenderingDecision_HasExactlyOneSource` — the component's source names `PayloadDetail` and neither `LayoutFor` nor `BodyIsMultiLine`; red before the deletion, green after |
| 43 | ✅ | Every kind's rendering can be seen, not only asserted, on both surfaces | Automated (T2) + screenshot | *Every notification kind is produced by its own trigger…*, 2026-09-24 — fifteen images: each surface whole, each of the six kinds on each of the two surfaces, the dialog's detail and the popup's two expanders |
| 44 | ✅ | A capture is a command the next run repeats, not an act of driving a browser | Automated (T2) | `scripts/testing/capture-page.csx` — every image in that document comes from a `Capture-Row` call or an explicit `dotnet script` line, each printing the assertion its own shot was taken against. Its teardown proven by measurement: `72` `WebSocketException`s before the graceful close, `0` after |

**Rows 5, 9 and 10 cannot be replaced by unit tests.** A unit test can prove the markup and the
stylesheet name the same class; only a rendered page proves the rule reaches the element. #303's nav
icon is the standing example — the class was present the whole time the icon was missing.

**Row 3 exists because row 2 asserts an absence.** Without it, a cell that renders nothing at all would
satisfy row 2 perfectly, which is the same trap `11-clean-reseed-confirmation.md`'s canary found in its
own step 1.
