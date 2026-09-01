# #370 — An expected import conflict is signalled by throwing, once per conflicted row per render

**Status:** Planning
**GitHub issue:** #370
**Tiers required:** T1, T2
**Depends on:** none — `FieldMergeResolver` predates this milestone. Sequenced against #369 only because
both touch `/import-review`.

---

## Description

`FieldMergeResolver.ResolveWithDecisions` reports the expected outcome — fields disagree and nobody has
decided — by throwing `UnresolvedFieldConflictException`. Seven call sites catch it and carry on, four
with a comment stating the throw is the normal path.

`SqliteImportActionService.ComputeAmbiguousFields` throws deliberately to answer a question: it calls
the resolver only so it can catch the exception and read `ex.FieldNames`, there being no non-throwing
way to ask which fields conflict. `/import-review` calls that once per row and Blazor renders twice, so
opening the page throws twice per conflicted row while nothing is wrong.

Measured in T1, 2026-09-01: 4 rows → 8 throws per visit, 8 rows → 16. Seeding adds one per conflicting
file.

## Impact

1. **It stops the debugger every time.** With break-on-thrown-exceptions on — the normal setting while
   investigating anything else — a T1 session is interrupted by an exception meaning "working as
   designed". That run's log shows one file's seeding spanning 54 seconds (19:59:03 → 19:59:57) for
   this reason.
2. **The log fills with `Exception thrown:` lines indicating no fault**, which is the noise that trains
   a reader to skip exception lines.

**Recorded because the first triage got it wrong.** #303's plan called this "expected, not a fault …
recorded rather than chased", on the grounds that the application still functions. That much is true;
"no impact" was not, and it was asserted without measuring. CLAUDE.md's own triage rule says a warning
whose impact you cannot determine is not harmless by default — the determination is the step that was
skipped, and the impact lands on whoever is debugging.

---

## Steps

### 1. Check what the existing coverage actually asserts

**Status:** ⬜ Not started — **do this before changing anything**

`ResolveWithDecisions` has 20 references outside its own file. Several are tests asserting the throw,
so they encode the behaviour under discussion and cannot be used unexamined as the regression net.

### 2. Add a non-throwing primary form

**Status:** ⬜ Not started

`TryResolveWithDecisions` returning the merged result plus the unresolved field names, with the
throwing form delegating to it. Behaviour must not change — this is a signalling change, not a semantic
one.

### 3. Convert the internal call sites

**Status:** ⬜ Not started

The seven that catch it as ordinary flow. `ComputeAmbiguousFields` especially: it should ask for the
conflicted fields, not provoke and catch a throw.

### 4. Decide what the API boundary does

**Status:** ⬜ Not started

`ImportEndpoints.cs:283` turns an unresolved conflict into an HTTP response — a genuine "refuse to
merge" boundary where an exception may still be the right shape. Keeping the throwing overload for it
is acceptable; expected flow being signalled by an exception is not.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | TBD — named once step 1 has established what the current tests hold | | |

**One row is already known.** The observable outcome is a count: opening `/import-review` with N
conflicted rows must throw zero `UnresolvedFieldConflictException`, where it currently throws 2N. That
is measurable in T2 against a container log, and it is the only row that proves the issue rather than
the refactor.
