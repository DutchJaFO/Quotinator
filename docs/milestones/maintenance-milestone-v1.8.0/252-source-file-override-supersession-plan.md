# #252 — Confirm whether SourceFileOverride should be superseded by FileResource

**Status:** Planning
**GitHub issue:** #252
**Tiers required:** N/A
**Depends on:** #251

---

## Background

`Import_SourceFileOverride` (post-#253's rename; today `System_SourceFileOverrides`,
`src/Quotinator.Data/Entities/SourceFileOverride.cs`) is #153's narrow registry recording whether a
bundled source's `ruleFile`/`sourceAliasFile` has been overridden by a generated copy on the persistent
volume. #251's `Import_FileResource` covers a much broader version of the same underlying question —
"what file actually produced this" — for every import, not just rule-file overrides. This issue exists
to make the supersession call explicitly rather than leave it as a lingering "should we" with no owner,
per its own Definition of Done.

This doc is deliberately small — the actual comparison work can't happen until #251 has a real,
implemented schema to compare against; see the shape note below for why the two already look similar
on paper.

## Shape comparison (confirmed 2026-08-01, before #251 is implemented)

`SourceFileOverride` today has: `FileName` (string), `Origin` (`SafeValue<SeedBatchOrigin?>`),
`ContentHash` (SHA-256 hex string), `SourceBatchId` (loose string reference, explicitly "no FK — this
project doesn't know the consumer's batch table name," per its own doc comment).

#251's proposed `Import_FileResource` has: `FileName`, `Origin` (same `SeedBatchOrigin` enum),
`ContentHash` (same SHA-256 hex format), plus `Content` (the actual file bytes, which
`SourceFileOverride` does not store) and a proper `Import_FileResourceBatch` join table (a real FK,
unlike `SourceFileOverride`'s loose string reference — possible because `Import_FileResourceBatch`
lives in the same project as `Import_Batch` and can reference it directly).

**On paper, `Import_FileResource` is a strict superset of what `SourceFileOverride` tracks** — same
identity fields, plus content storage and a real FK the older table couldn't have. This makes
supersession look likely, but the actual decision needs #251 implemented and exercised first: the
override-detection logic in #153's generate-rules endpoint needs a real trust boundary (only rows this
project's own generator actually wrote), and confirming `Import_FileResource`'s dedup-by-hash semantics
don't accidentally weaken that boundary is exactly the comparison this issue exists to do — not
something safe to conclude from the schema shape alone.

## What this issue's own plan doc will contain once #251 lands

A resolved comparison against #251's actual (not proposed) schema, one of:

- **Superseded**: a migration path moving `Import_SourceFileOverride`'s existing rows into
  `Import_FileResource`/`Import_FileResourceBatch`, the `SourceFileOverride` entity/table/registry
  (`ISourceFileOverrideRegistry`, `SourceFileOverrideRegistry`, `NoOpSourceFileOverrideRegistry`)
  removed, and #153's generate-rules endpoint updated to query `Import_FileResource` instead.
- **Kept separate**: reasoning recorded in a short ADR or this issue's own closing comment, so the
  question doesn't recur — likely candidate reasoning: the trust-boundary concern above (a
  general-purpose provenance table with looser insert conditions than a purpose-built override
  registry) or a difference in write frequency/lifecycle that makes sharing one table awkward.

---

## Steps

### 1. Wait for #251

**Status:** ⬜ Not started

No further planning work here until #251's schema is implemented and stable.

### 2. Compare against the real #251 schema and decide

**Status:** ⬜ Not started

Re-run the shape comparison above against #251's actual shipped schema (not the proposal). Confirm or
revise the "likely superseded" read above. Get explicit developer sign-off on the decision — this is a
removal-or-keep call on shipped, working code (#153), not a greenfield design choice.

### 3. Implement the decision

**Status:** ⬜ Not started

Either the migration-and-removal path or the kept-separate documentation, per whichever step 2
concludes.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Decision made: superseded, or kept as a separate permanent registry | Live | This plan doc updated with the resolved comparison (step 2), developer sign-off recorded |
| 2 | ❌ | If superseded: `SourceFileOverride` rows migrated, mechanism removed, #153's endpoint updated | Unit test | `Quotinator.Data.Tests` migration test proving no data loss; `Quotinator.Api.Tests` proving #153's generate-rules endpoint still correctly detects overrides via the new mechanism |
| 3 | ❌ | If kept: reasoning documented so the question doesn't recur | Live | Short ADR, or this issue's own closing comment, cited from `Import_SourceFileOverride`'s entity doc comment (replacing today's "temporary name pending #227" note, which is already stale post-#253) |

This table stays at two placeholder rows (2, 3 mutually exclusive — only one will actually apply) until
step 2 resolves which branch this issue takes.

---

## Scope changes

None.
