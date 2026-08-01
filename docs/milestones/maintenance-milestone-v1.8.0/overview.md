# v1.8.0 — Milestone Overview

**GitHub milestone:** #18
**Type:** Maintenance milestone (catch-all for bugs and minor improvements, targeting v1.8.x releases)
**Previous maintenance milestone:** v1.7.0 (#17) — closed 2026-06-28

Unlike a feature milestone, issues here are not necessarily related to each other. That does not
change the branching rule — see `docs/workflow/process.md`'s "Step 2 — Create the feature branch": a
milestone always gets exactly one branch, covering every issue in it.

**This milestone's own history (2026-07-31):** #166, #197, #159, and #146 each got their own
branch/PR before that rule was written down — a direct cause of the GitHub Ruleset `BEHIND` friction
that prompted writing it. From #208 onward, every remaining issue (#151, #156, #222, #227, #232,
#236, #244, #245, #249, and #208 itself) shares `feature/v1.8.0-maintenance-batch`, per the corrected
rule. #249 was filed 2026-08-01 while planning #151 (see ADR 014) — a new dependency, not a
pre-existing part of this list. #227 was resequenced to run immediately next, ahead of every other
not-yet-implemented issue, the same day — see the Dependency map below for why.

---

## Issue list

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#166](https://github.com/DutchJaFO/Quotinator/issues/166) | HA add-on: split into separate stable and beta sub-add-ons | Waiting for release | T1 ✅ T2 ✅ T3 ✅ | [166-ha-addon-stable-beta-split-plan.md](166-ha-addon-stable-beta-split-plan.md) |
| [#197](https://github.com/DutchJaFO/Quotinator/issues/197) | MSTest analyzer diagnostics (e.g. MSTEST0068) are invisible to the 0-warnings build policy — no .editorconfig exists | Waiting for release | N/A for T1 (test/build-tooling only — a VS boot never rebuilds `tests/`) T2 ✅ | No plan doc yet |
| [#159](https://github.com/DutchJaFO/Quotinator/issues/159) | Document repository-is-C#-only tooling policy as an ADR | Released | T1 ✅ | No plan doc — pure content fix, no implementation decisions required |
| [#146](https://github.com/DutchJaFO/Quotinator/issues/146) | Audit memory-only project conventions and move genuine ones into CLAUDE.md/docs | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — pure content fix, no implementation decisions required |
| [#208](https://github.com/DutchJaFO/Quotinator/issues/208) | Issue-creation process: always propose label + milestone in the same draft-review pass | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — pure content fix, no implementation decisions required |
| [#150](https://github.com/DutchJaFO/Quotinator/issues/150) | Audit: ensure all enum-valued POCO properties have matching DB CHECK constraints | Waiting for release | T1 ✅ T2 ✅ | No plan doc yet |
| [#151](https://github.com/DutchJaFO/Quotinator/issues/151) | Should System_-prefixed audit-trail tables purge rows referencing Reset-wiped entities? | Waiting for release | N/A (docs-only, no runtime-loaded content) | No plan doc — decision recorded in [ADR 014](../architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md) |
| [#249](https://github.com/DutchJaFO/Quotinator/issues/249) | Export audit-trail tables to a dedicated folder before a destructive Reset | Planning | Not yet determined | No plan doc yet |
| [#156](https://github.com/DutchJaFO/Quotinator/issues/156) | Reset: use the fresh-database baseline script instead of drop-all-user-tables + replay | Planning | Not yet determined | No plan doc yet |
| [#222](https://github.com/DutchJaFO/Quotinator/issues/222) | Unicode-aware case-insensitive LIKE matching (accented/non-ASCII characters) | Planning | T1, T2 | [222-unicode-like-matching-plan.md](222-unicode-like-matching-plan.md) |
| [#148](https://github.com/DutchJaFO/Quotinator/issues/148) | OpenAPI: document response models for existing quote/admin endpoints | Waiting for release | T1 ✅ T2 ✅ | No plan doc yet |
| [#227](https://github.com/DutchJaFO/Quotinator/issues/227) | Import-table naming standardization + general import-file content provenance (FileResource / FileResourceLine) | Planning | Not yet determined | No plan doc yet |
| [#178](https://github.com/DutchJaFO/Quotinator/issues/178) | Changelog: add an optional one-line quote to each release entry | Waiting for release | T1 ✅ T2 ✅ | No plan doc yet |
| [#232](https://github.com/DutchJaFO/Quotinator/issues/232) | Reduce OS-level vulnerabilities in Docker base image (Docker Scout scan) | Waiting for release | N/A (research, no code change) | No plan doc — findings recorded in `docs/security/README.md`'s "Docker base image (OS packages)" section |
| [#250](https://github.com/DutchJaFO/Quotinator/issues/250) | Periodically re-scan the Docker image with Docker Scout after fresh builds | Planning | Not yet determined | No plan doc yet |
| [#236](https://github.com/DutchJaFO/Quotinator/issues/236) | Release workflow: HA can see a config.yaml version bump before the matching Docker image is pushed | Waiting for release | N/A (process/documentation change only) | [236-release-workflow-version-race-plan.md](236-release-workflow-version-race-plan.md) |
| [#244](https://github.com/DutchJaFO/Quotinator/issues/244) | Hidden Roslyn code-style and .NET analyzer diagnostics are invisible to the 0-warnings build policy (IDE0xxx, CAxxxx) | Planning | Not yet determined | No plan doc yet |
| [#245](https://github.com/DutchJaFO/Quotinator/issues/245) | Sources.Date stays NULL when a Source's only sources[] entry omits date (gap in #191's scope) | Planning | Not yet determined | No plan doc yet |

---

## Dependency map

**#227 blocks the issues that actually touch renamed tables, entities, or SQL** — #249, #156, and
#245 — per [ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md)/
[ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md). Code written
against today's names in any of those three would need to be rewritten the moment #227 lands, so they
wait for it. This is an implementation-order dependency (#227 must actually be merged first for these
three), not just a release gate.

**#222 turned out independent too, on closer inspection while planning it (2026-08-01).** Its own SQL
changes touch only column aliases (`q.QuoteText`, `s.Title`, `c.Name`, `p.Name`) inside
`Sql.SearchField`, never the table names themselves — those live in a different query
(`Sql.Quotes.SelectSearch`'s `FROM`/`JOIN`) that #222 doesn't touch. No entity class involved is
`[Table]`-attributed either. Planned and implemented ahead of #227 like #232/#236 before it.

**#244 should follow #227 for conflict-avoidance, not a hard dependency** — its mechanical
IDE0xxx/CAxxxx fixes touch the same class definitions #227 renames; doing #244 first would mean #227
has to re-touch already-modified files, the same reasoning that sequenced #197 early for a different
set of files.

**#232 and #236 turned out not to depend on #227 at all** — #232 (Docker base image research) and
#236 (release-workflow process/CI change) touch neither the database schema nor any renamed class, and
both were confirmed independent and completed/planned out of order from the original blanket claim
here. Corrected 2026-08-01 after actually working both.

Separately, a release-level gate: **#249 must ship in the same release as #156**, per
[ADR 014](../architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md) — not
an implementation-order dependency between the two of them, so #249 and #156 can still be built/merged
in either order *relative to each other*, as long as both land after #227 and both ship in the same
release. None of the remaining issues block each other beyond these two relationships.

---

## Order of operations

1. **#166** — HA add-on stable/beta split (largest feature in this batch; started first at the
   maintainer's explicit direction, ahead of the smaller items below)
2. **#197** — .editorconfig / MSTest analyzer severities — broad mechanical change across 76 call
   sites in 25 test files; doing this early avoids conflicts with tests added by later issues
3. **#159** — ADR: repository-is-C#-only tooling policy (docs-only; shipped in v1.8.0, closed out
   2026-07-31 after this milestone's own review caught it was never marked released)
4. **#146** — Audit memory-only conventions → CLAUDE.md/docs (docs-only; six genuine gaps found and
   migrated 2026-07-31 — rate limiting's undocumented Admin concurrency-1 policy, GUID hex-letter test
   fixtures, DB-integration-test requirement for seeder code, the Ruleset BEHIND merge gotcha, no
   smoke-tests-on-dev-db, and import-file minimalism)
5. **#208** — Issue-creation process: label + milestone in the same draft pass (process/docs-only)
6. **#150** — Audit enum-valued POCO properties for missing CHECK constraints (one known gap already found)
7. **#151** — System_-prefixed audit-trail table purge-on-Reset policy decision (docs-only; resolved
   2026-08-01 via [ADR 014](../architecture-decisions/014-audit-trail-tables-do-not-purge-dangling-references.md) — dangling references are
   permanent by design; filed #249 as a release gate on #156)
8. **#227** — Domain-prefixed table naming + class-naming/enum-placement conventions (moved to first
   position among remaining work, 2026-08-01, per explicit developer direction — its rename touches
   every table, entity/response/DTO class, and SQL query string, so every issue below would otherwise
   be written against names #227 immediately invalidates. Naming decided via
   [ADR 015](../architecture-decisions/015-domain-prefixed-table-naming.md)/
   [ADR 016](../architecture-decisions/016-class-naming-suffixes-and-enum-placement.md); the
   implementation plan — full rename mapping and migration-squash inventory — is still to be written)
9. **#249** — Export audit-trail tables to a dedicated folder before a destructive Reset (must ship in
   the same release as #156, not necessarily built before it; filed while planning #151; targets the
   post-#227 table names)
10. **#156** — Reset: baseline script instead of drop-all-user-tables + replay (also targets the
    post-#227 table names, and its own `GetUserTables` exclusion-pattern question per ADR 015's
    Consequences is easier to resolve once #227 has landed)
11. **#222** — Unicode-aware case-insensitive LIKE matching (real correctness bug, medium effort;
    planned 2026-08-01 — confirmed independent of #227, worked out of order like #232/#236; opt-in
    `Quotinator:UnicodeAwareSearch` flag (default off) using a custom `CreateFunction`-registered
    `UNICODE_CONTAINS`, not `LIKE`/ICU, see
    [222-unicode-like-matching-plan.md](222-unicode-like-matching-plan.md))
12. **#148** — OpenAPI: document response models for quote/admin endpoints
13. **#178** — Changelog: optional one-line quote per release entry
14. **#232** — Docker Scout OS vulnerability research (resolved 2026-08-01: a fresh `--no-cache`
    rebuild alone dropped 23 reported vulnerabilities to 8, all in the base image, all currently
    unfixable upstream; documented as accepted residual risk in `docs/security/README.md`; chiseled
    base image evaluated and rejected for now — no code change)
15. **#236** — Release workflow config/image timing race (discovered live during #166's T3
    verification, 2026-07-30; appended here rather than reordered in since it has no dependency on
    the others; planned 2026-08-01 — split the version-bump PR into a before-tag PR and an after-tag,
    workflow-confirmed-green follow-up PR, see
    [236-release-workflow-version-race-plan.md](236-release-workflow-version-race-plan.md))
16. **#244** — Hidden IDE0xxx/CAxxxx analyzer diagnostics (discovered while reviewing #197's fix,
    2026-07-31; appended here rather than reordered in since it has no dependency on the others)
17. **#245** — Sources.Date gap for date-less explicit `sources[]` entries (discovered during the
    full T2 smoke-test pass, 2026-07-31; appended here rather than reordered in since it has no
    dependency on the others)
18. **#250** — Periodic Docker Scout re-scan added to the T2 smoke-test checklist (filed while closing
    #232, 2026-08-01; appended here rather than reordered in since it has no dependency on the others)

---

## PR merge plan

#166, #197, #159, and #146 each used their own branch/PR, merged independently as each completed its
own T1/T2 verification. From #208 onward, the remaining small/independent issues share
`feature/v1.8.0-maintenance-batch` and merge together — see the branching-policy note above for why.
