# #422: A modal's 95vh height cap never applies; Bootstrap's centred min-height overrides it

**Status:** In progress
**GitHub issue:** #422
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Run T1, then the T2 scope in step 7.** Steps 1 to 5 are done; T1 is the developer's own action.

---

## What this fixes

`ModalDialog` declares `max-height: 95vh` inline on `.modal-dialog`, and three code comments and two
documents describe a 95vh cap. It cannot bind. Bootstrap's `.modal-dialog-centered` sets
`min-height: calc(100% - var(--bs-modal-margin) * 2)`, and a larger `min-height` beats `max-height`, so
the dialog is `viewport − 56px` whenever its content overflows. Measured: `1218` in a `1274` viewport
against a `95vh` of `1210.3`; `364` in `420`; `664` in `720`.

Nothing is unreachable, since the header and footer stay on screen and the body scrolls, so this is a
code-accuracy defect: the code states a cap it does not have, and has done since #308.

---

## Steps

### 1. Write the failing tests and confirm them red

**Status:** ✅ Done

Two, because the defect has two faces and each is satisfied by a different kind of evidence.

A unit-level assertion that `ModalDialog.razor` declares no height cap, following
`RepositoryStructureTests`'s existing precedent for asserting over a file's own text. Red now, because
the declaration is there.

A live assertion that the rendered dialog's declared `max-height` does not contradict its measured
height, added to *A notification renders its title and body as separate things, and the detail dialog
fits the viewport*. Red now at a tall viewport, where `1218` exceeds a declared `1210.3`.

Both confirmed red before the fix. The live one reported
`{"contradicts": true, "declaredMaxHeight": "1210.3px", "dialogHeight": 1218, "viewportHeight": 1274}`
against a real container, the same numbers the issue measured.

### 2. Remove the cap and every statement of it

**Status:** ✅ Done

The inline `max-height: 95vh` in `ModalDialog.razor`, and the wording in three places that describes a
cap the component will no longer claim: that file's own header comment, `ModalDialog.razor.cs`'s class
summary and its `MaxWidth` summary, which currently reads "Height is fixed at 95vh for every caller",
and `NotificationTable.razor.css`'s comment.

What replaces it is a statement of what actually governs the height: Bootstrap's centred and scrollable
classes size the dialog to the viewport less its margin, and the body scrolls within it.

### 3. Correct the two documents

**Status:** ✅ Done

*A notification renders its title and body as separate things, and the detail dialog fits the viewport*
already asserts fit to the viewport rather than a `95vh` figure, so its assertion is unchanged; what
changes is its explanation, which currently describes the inert cap as a live quirk to work around.
`docs/automated-testing/README.md` lists "a `95vh` cap" among the things a layout document measures.

### 4. Delete the knowledgebase entry

**Status:** ✅ Done

`docs/knowledgebase/a-dialog-fills-almost-the-whole-window.md`, and its row in the knowledgebase index.
Deleted rather than retired: no release carried the condition, which is `docs/knowledgebase.md`'s
retention rule, and the issue states the same.

### 5. Changelog

**Status:** ✅ Done

`changelog.en.json`'s `unreleased` with `422` in `unreleased.issues`, and matching translated entries in
`nl` and `de` in the same commit. No `highlights` entry: nothing a user can observe changes. A `fixed`
entry in each of the three files, `422` added to all three `unreleased.issues` arrays, and `CHANGELOG.md`
regenerated; the two add-on changelogs are deferred to the release's own follow-up PR.

### 6. T1

**Status:** ⬜ Not started

The application still starts. Nothing beyond startup.

### 7. T2

**Status:** ⬜ Not started

The smoke set, plus the document this changes: *A notification renders its title and body as separate
things, and the detail dialog fits the viewport*. A rendering change takes a real screenshot through
`scripts/testing/capture-page.csx`, as evidence beside the DOM reads rather than instead of them.

---

## Scope changes

**The cap is removed rather than made real** (decided 2026-10-10, on evidence). The issue leaves both
open: "Either the 95vh cap applies, or it is removed along with the comments and wording that describe
it." Three things point the same way.

The current behaviour is correct. The dialog fits the viewport, the header and footer stay put, the
body scrolls, and the issue itself records that nothing is unreachable. There is no defect to fix in
what the user sees.

The suite has already chosen. *A notification renders its title and body as separate things, and the
detail dialog fits the viewport* was rewritten during #411 to assert fit to the viewport, with a note
explaining that the `95vh` form "held below about `1120px` and failed above it". Making the cap bind
would move behaviour away from what the tests now codify as correct.

Making it bind would also mean fighting the framework: the cap would have to defeat both
`.modal-dialog-centered`'s `min-height` and `.modal-dialog-scrollable`'s `height`, to produce a dialog
that is slightly shorter on tall screens for no stated benefit.

**Both unit tests were narrowed after their first draft proved too blunt** (2026-10-10). The first
scanned the whole of `ModalDialog.razor` for `max-height`, which the replacement comment legitimately
names while explaining what governs the height instead; it now reads only the `style="…"` attributes,
which is where a cap the component cannot honour would actually be declared. The second covered the two
documents as well, but *A notification renders its title and body as separate things, and the detail
dialog fits the viewport* legitimately records `95vh` in its own history note, so the test is scoped to
the three component files, and renamed `NoComponent_DescribesTheInertHeightCap` to say so. Verification
row 2 follows the narrower scope.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The component declares no height cap it cannot honour | Unit test | `RepositoryStructureTests.ModalDialog_DeclaresNoHeightCapItCannotHonour` |
| 2 | ✅ | No component file describes a 95vh cap | Unit test | `RepositoryStructureTests.NoComponent_DescribesTheInertHeightCap`, scanning the three component files |
| 3 | ✅ | A rendered dialog's declared `max-height` does not contradict its measured height | Live | *A notification renders its title and body as separate things, and the detail dialog fits the viewport*, at a `1274` viewport: `{"contradicts": false, "declaredMaxHeight": "none", "dialogHeight": 1218, "withinViewport": true}` |
| 4 | ✅ | The dialog still fits a short viewport, unchanged | Live | The same document at `420`: `{"dialogHeight": 364, "bodyScrolls": true, "contradicts": false}` |
| 5 | ✅ | The header and footer stay reachable and the body still scrolls, on both surfaces | Live | The same document's existing steps: `headerVisible: true`, `footerVisible: true` at both viewports |
| 6 | ✅ | A screenshot records the rendered result | Live | `scripts/testing/capture-page.csx` against the detail dialog, at both viewports |
| 7 | ✅ | Build and full test run are clean | Live | `dotnet build --configuration Release` then `dotnet test --configuration Release --verbosity normal -m:1`: 4519 passed across 11 projects, both `0 Warning(s)  0 Error(s)` |
| 8 | ❌ | The application still starts | Live | T1, Visual Studio, developer's own action |
| 9 | ❌ | T2 scope passes | Live | The smoke set plus the document named above |
