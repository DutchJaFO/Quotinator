# #308 — Notification: multi-line/rich message layout

**Status:** Planning
**GitHub issue:** #308
**Tiers required:** T1, T2
**Depends on:** #312, #302, #303, #304

---

## Description

`NotificationTable` renders its message as a single plain cell with no line-break handling — Razor
HTML-encodes the value, so any embedded newline collapses to whitespace. This issue renders the
`Title`/`Body` structure #312 introduced: a headline and a body, each displayed as what it is.

**This is the last issue in the milestone's order, deliberately.** It defines how *each notification
type* is laid out across *both* surfaces — the startup/popup dialogs and the `/notifications` view —
and each remaining producer brings a type with its own payload. Designing those layouts before the
types exist is guessing.

## Scope revision — no longer rendering-only

**Recorded 2026-08-15, relocated here from `overview.md` 2026-08-22.** This issue was originally
scoped as a CSS fix, and placed second in the milestone order so that rendering would precede the
producers and their output would display correctly from the start.

Both were revised. The milestone's goal was restated — v1.8.0 shipped a *basic* notification system
and this milestone makes it complete — which made #278's flat `Message` the bottleneck rather than a
fixed constraint. #312 accordingly gave notifications a real `Title` and `Body`, so this issue's job
became rendering that structure rather than coaxing line breaks out of one flat string. The issue's
original step 3 asserted "no change to `NotificationEntity.Message`'s storage shape"; #312 supersedes
it.

The position moved from second to last in the same revision, for the reason in the Description above.

---

## Steps

### 1. Render `Title` as a distinct element with `Body` beneath it

**Status:** ⬜ Not started

Its own heading or emphasis, not one undifferentiated cell. A notification with no title still renders
correctly — `Title` is nullable in #312's schema, and the two shipped producers (#279, #289) predate
it.

### 2. Render embedded line breaks in `Body`

**Status:** ⬜ Not started

CSS (`white-space: pre-line` on the cell) rather than a markup or formatting change, so producers keep
writing a plain string with `\n` separators and no new serialisation concern.

### 3. Define the per-type layout across both surfaces

**Status:** ⬜ Not started

Each producer's payload shape gets a layout decision for the startup/popup dialog and for the
`/notifications` view. This is the work the position change exists to enable — it cannot start until
#302, #303 and #304 have landed their types.

Enumerate the types that actually exist at the time rather than working from this list: producers may
have been added or reshaped since.

### 4. Confirm both call sites

**Status:** ⬜ Not started

`NotificationSummary` (the startup modal) and the `/notifications` page both consume
`NotificationTable`. A change that looks right in one can be wrong in the other — the modal is
size-constrained in a way the page is not.

### 5. Take no storage change of this issue's own

**Status:** ⬜ Not started

#312 owns the schema; #319 owns the translation shape. This issue consumes both. If a layout need
appears to require a storage change, that is a finding to raise, not something to add here.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `Title` renders as a distinct element, `Body` beneath it | Live | T1: a notification with both renders as headline plus body in the popup and the page |
| 2 | ❌ | A notification with no `Title` still renders correctly | Live | T1: confirmed against a #279/#289 notification, which predates the field |
| 3 | ❌ | `Body` renders embedded line breaks | Live | T1: a multi-line body renders as multiple lines, not collapsed whitespace |
| 4 | ❌ | Every notification type in existence at implementation time has a defined layout for both surfaces | Live | T1: one confirmed rendering per type, on both surfaces |
| 5 | ❌ | Both call sites render correctly | Live | T1: `NotificationSummary` in the startup modal, and `/notifications` |
| 6 | ❌ | No storage change is introduced by this issue | Live | No migration added; `NotificationEntity` unchanged in the diff |
| 7 | ❌ | Rendering survives a degraded startup | Live | T2: degraded container, `/notifications` returns 200 and renders |
