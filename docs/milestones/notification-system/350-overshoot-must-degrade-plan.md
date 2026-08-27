# #350 — A schema-version overshoot runs healthy instead of degrading, on a schema whose shape is unknown

**Status:** Planning
**GitHub issue:** #350
**Tiers required:** T1, T2
**Depends on:** none — found by #327; owns the overshoot document and test that issue added

---

## Description

When a database's recorded schema version is ahead of the running build, the application reports itself
healthy and serves normally. That is a foot gun: an overshoot means this build does not know what the
missing migrations did — added, altered or removed — so the schema is of *unknown* shape, not a
known-good one.

#289 introduced the behaviour deliberately, on the premise that an overshoot is only ever a stale
counter on a complete schema, which is what a migration squash leaves behind. **A squash is one way to
reach an overshoot, not the only way**, and nothing distinguishes it from a database that ran migrations
this build has never seen. The shipped notification states the unprovable part outright — *"The schema
itself is complete and the app is working normally"* — which it has no way to know.

---

## Next action

**Refine this plan into a verification checklist, then write the red tests.**

The contract is settled (degrade, name both remedies, drop the unprovable claims); what is not yet
decided is where the overshoot marks the database unhealthy, and how the notification reaches an
operator once it can no longer depend on `dbHealth.IsHealthy`.

---

## Why this reverses a deliberate decision

Recorded here rather than argued with in code comments, so there is one account rather than two.

#289 reasoned from the squash case and generalised from it. The counter-argument is not that the squash
case is wrong — it is that the application cannot *detect* which case it is in, and the safe reading of
an unknown schema is not "assume complete". Degrading costs an operator a Reset or a restore; serving
from an unknown schema risks writing against a shape the build does not understand.

Developer direction, 2026-08-27:

> *"An overshoot is a foot gun. We don't know if the missing migrations added or removed things we did
> not expect. As such we treat it as a failure that has to be fixed before we allow anything other than
> degraded UX."*

---

## Design

### The contract

Same as every other degradation in `startup-and-degradation/`: process alive, `/health` unhealthy with a
reason naming the fault, Blazor pages rendering degraded UI rather than 500, OpenAPI reachable. The
overshoot stops being this category's one exception.

### The notification gate has to move

`Program.cs` currently raises the notification only when `dbHealth.IsHealthy && SchemaVersionOvershootDetected`.
Once an overshoot marks the database unhealthy, that gate suppresses the very notification that explains
why — so the gate is the first thing this issue changes, not an afterthought.

### Two remedies, not one

An overshoot is resolvable by a database Reset **or** by restoring an older backup (developer direction,
2026-08-27). The second matters when the operator wants their data back rather than a clean rebuild. The
shipped text names only the Reset. [#349](https://github.com/DutchJaFO/Quotinator/issues/349) records
restore as future work; naming it here is still correct, because an operator holding a pre-overshoot
backup can restore one by hand today.

### Claims the application cannot support are removed

"The schema itself is complete" and "the app is working normally" both go. What is true and provable:
the recorded version is ahead of this build, and the schema's actual shape is therefore unknown.

---

## Steps

### 1. Write the verification checklist

**Status:** ⬜ Not started

### 2. Write the red tests

**Status:** ⬜ Not started

The seven in the issue. `Startup_SchemaVersionAheadOfApplication_StaysHealthyAndSurfacesTheOvershoot`
(added by #327) is **removed, not edited** — its name states the old contract, and flipping an assertion
in place leaves a test whose name lies about what it checks.

### 3. Make the overshoot degrade

**Status:** ⬜ Not started

### 4. Move the notification off the health gate

**Status:** ⬜ Not started

### 5. Rewrite the messages

**Status:** ⬜ Not started

Both remedies named; no claim about schema completeness. Each states symptom, cause and remedy — the
property #333's sweep needs. No `QTN-` code allocated here, per #333 requirement 8's precedent.

### 6. Rewrite `startup-and-degradation/06`

**Status:** ⬜ Not started

It currently asserts `/health` `200` and tells the reader not to "fix" a `503` by relaxing the
expectation — advice that is exactly backwards once this lands. Its `Determinism` gains the reasoning
above: the unknown schema drift is the fault, the stale counter only its symptom.

---

## Verification checklist

**Not yet written — this is step 1, and implementation does not start until it exists.** Recorded as an
explicit gap rather than an empty table, so the doc's own state says what the next action is.
