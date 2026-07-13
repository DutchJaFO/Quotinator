# #155 — Migration review: verify full incremental path from last-shipped v1.7.2 schema

**Status:** Planning
**GitHub issue:** #155
**Tiers required:** T1, T2
**Depends on:** none (independent of the other issues in this milestone; sequenced last, immediately before milestone close, per its own issue text and `overview.md`'s dependency map)

---

## Spec requirements (from the GitHub issue)

1. Obtain (or reconstruct) a database snapshot matching the actual v1.7.2 released schema.
2. Apply every migration this milestone has added, in order, against that snapshot.
3. Confirm the result matches what the incremental-replay-from-empty and baseline-from-empty paths
   already produce — no drift specific to *upgrading from a real prior release* that the from-empty
   tests wouldn't catch.
4. Pay particular attention to any migration edited after being applied locally during this
   milestone's development (the #56 `System_ImportConflicts` incident is the known example) —
   confirm the final on-disk migration text is what a genuine v1.7.2 → current upgrade would
   actually apply.
5. Decide whether a permanent test fixture (a checked-in v1.7.2 database snapshot, or a
   reconstructable script) should be added so this class of gap is caught automatically for future
   milestones, rather than requiring a manual audit at the end of every milestone.

---

## Investigation findings (current codebase/repo, as of this session)

**ADR 009 already fully specifies the mechanism this issue executes** — this issue is not a new
verification approach to design; it's a comprehensive *run* of ADR 009's already-decided process,
applied to this specific milestone's full, final set of migrations, plus item 5's own follow-on
decision (build a permanent fixture, or keep this as a manual per-milestone step). Read
`docs/architecture-decisions/009-verify-migrations-against-last-released-schema.md` in full before
starting — its "Decision" section's four numbered steps map directly onto this issue's own five
requirements (1-4 here mirror ADR 009's 1-4 verbatim; requirement 5 is this issue's own addition,
tracked in the ADR's "Consequences" section as "see #155 for where that decision will actually be
made").

**The `v1.7.2` tag exists in this repository** (confirmed via `git tag -l`) — obtaining a snapshot
per requirement 1 does not require reconstructing anything from scratch; it means checking out that
tag (into a separate worktree or a temporary clone, not the current feature branch) and running a
fresh `InitialiseAsync` against an empty database file, exactly as ADR 009's own reasoning describes
("checking out the release tag and running a fresh `InitialiseAsync` against an empty file").

**No existing v1.7.2 fixture or automated tooling exists yet** — grepped `tests/` and `scripts/` for
any reference to `v1.7.2`/"last-released"/"last-shipped"; the only hits are stale build-output XML
files (`bin/**/Quotinator.Data.xml`, doc-comment artifacts from an unrelated `<see cref>`, not
schema fixtures). Requirement 5's "should a permanent fixture be added" question is therefore
genuinely open — nothing today would make building one redundant.

**Current migration inventory to apply, in order:**
- **Engine-owned** (`QuotinatorMigrations.All`, `src/Quotinator.Engine/Database/QuotinatorMigrations.cs:20-27`):
  8 versions, `Migration001_InitialSchema` through `Migration008_Conversations`.
- **Data-owned** (`DataOwnedMigrations`, `src/Quotinator.Data/Database/DatabaseInitializer.cs:25-36`):
  10 versions, `AuditMigrations.CreateAuditEntriesTable` through
  `ImportActionMigrations.AddBlockedStatusAndMarkCompletenessAs` — tracked independently per
  CLAUDE.md's "Migration ownership split" section (`System_SchemaVersion` for Data-owned,
  `System_ConsumerSchemaVersion` for Engine-owned), so both lists need to be confirmed against
  v1.7.2, not just the Engine-owned list the issue's own prose focuses on.
- **This count is not final** — issues #170-#176 in this same milestone (StageDirection/SoundCue/
  Person/Character/Conversation Modify support, Character's global-identity redesign) are each
  expected to add their own new migrations before this milestone closes. Per this issue's own
  "Dependencies: None — this can start once all of this milestone's other migrations are
  finalized" note, this review's actual step 2 (apply every migration) cannot be executed to
  completion until those land — this plan doc's steps below describe the review *mechanism*, which
  is designable now, but the review itself is only meaningfully complete once the migration list is
  final.

**The #56 `System_ImportConflicts` incident** (`bcb59fb fix [#56]: correct in-place edit of
already-applied System_ImportConflicts migration`) is the one already-known instance of requirement
4's concern — confirmed via the issue text and ADR 009 both citing the same commit. This session's
own #168 work independently hit and fixed an analogous case (`ImportActionMigrations
.CreateImportActionsTable` had been applied locally despite never shipping — see
`168-quote-completeness-guard-plan.md`'s own history) — requirement 4's audit should treat both as
known precedent for what to look for (a migration edited after being locally applied, even if that
edit happened pre-milestone-close), not assume #56 is the only historical instance.

---

## Steps

### 1. Reconstruct the v1.7.2 schema snapshot

**Status:** Not started.

Check out the `v1.7.2` tag into an isolated worktree (`git worktree add <path> v1.7.2`, never the
current feature branch) and run the app (or a minimal harness calling
`QuotinatorDatabaseInitializer.InitialiseAsync` directly, matching the pattern
`ImportActionPlannerTests.cs`/`QuoteImportServiceTests.cs` already use for isolated
`[TestInitialize]` databases) against a fresh, empty SQLite file. The resulting `.db` file is the
v1.7.2 snapshot for step 2. Confirm its schema version markers (`System_SchemaVersion`,
`System_ConsumerSchemaVersion`) read exactly what v1.7.2 shipped — cross-check against that tag's
own `QuotinatorMigrations.All`/`DataOwnedMigrations` list lengths at that point in history.

### 2. Apply this milestone's full migration set against the snapshot

**Status:** Not started. Blocked on every other issue in this milestone finalizing its own
migrations first (see Investigation findings above) — this step cannot reach a final answer until
#170-#176 have landed, though the mechanism can be exercised incrementally against whatever subset
exists at any point.

Copy the v1.7.2 snapshot `.db` file, point a `QuotinatorDatabaseInitializer` at it (on the current
feature branch's code, not v1.7.2's), and run `InitialiseAsync` — this replays every migration
added since v1.7.2, in order, exactly as a real installation's upgrade would. Capture the resulting
schema (`PRAGMA table_info` per table, or reuse whatever introspection the existing schema-drift
tests already use) for step 3's comparison.

### 3. Compare against the from-empty incremental and baseline paths

**Status:** Not started.

Run the existing from-empty schema-drift tests
(`DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema`,
`Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`,
`Baseline_And_IncrementalReplay_ProduceIdenticalCharactersSchema` if #174 has landed by this point,
and any other sibling tests this milestone's other issues add) and diff their resulting schema
against step 2's from-v1.7.2 result. Any difference is a genuine "upgrading from a real release
behaves differently than starting fresh" bug — the exact class of issue ADR 009 exists to catch and
the from-empty tests structurally cannot.

### 4. Audit every migration for post-application edits

**Status:** Not started.

Cross-check `git log` for every migration constant in both `QuotinatorMigrations.cs` and the
`*Migrations.cs` files under `src/Quotinator.Data/Database/` against whether it was modified in a
commit *after* the commit that first introduced it — a legitimate append-only migration is written
once and never touched again; any migration with more than one commit touching its own SQL text is
a candidate for the #56-class incident and needs manual confirmation that the final on-disk text is
what v1.7.2 → current would actually receive (not an intermediate, already-superseded version this
audit would otherwise miss). Cross-reference #168's own already-documented instance
(`ImportActionMigrations.CreateImportActionsTable`) as a known example to confirm is handled
correctly (its replacement, migration 10, should be what a real upgrade applies — the original
in-place edit should never have existed in any commit that reached `main`).

### 5. Decide on a permanent fixture

**Status:** Not started.

Per requirement 5 and ADR 009's own "Consequences" section ("Consider, as a follow-on... whether a
checked-in released-schema snapshot or a reconstructable script should become a permanent, automated
test fixture... see #155 for where that decision will actually be made") — this is this issue's own
decision to make, not pre-decided by this plan doc. Options: (a) check in the v1.7.2 `.db` snapshot
itself as a test fixture (simplest, but a binary fixture that needs replacing/growing for the next
milestone's own "last released schema" baseline once a new version ships); (b) check in a
reconstructable script/tag reference so each future milestone's own review regenerates its own
starting snapshot on demand (matches how step 1 already works, just formalized); (c) keep this as a
fully manual, non-automated step repeated at the end of every milestone (status quo, explicitly
flagged by ADR 009 as the "not decided" default). Document whichever is chosen in the closing
comment on this issue, and if it changes ADR 009 in any way, add a Revision section to that ADR in
the same commit (matching this session's own ADR 006 revision precedent).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | A v1.7.2 schema snapshot is reconstructed and confirmed to match what that tag actually shipped | Live | `git worktree add` + fresh `InitialiseAsync` against an empty file; schema version markers cross-checked against v1.7.2's own migration list |
| 2 | ❌ | Every migration this milestone added applies cleanly, in order, against the v1.7.2 snapshot | Live | `InitialiseAsync` run against the copied snapshot on current feature-branch code; no exception, final schema version matches the current `QuotinatorMigrations.All`/`DataOwnedMigrations` count |
| 3 | ❌ | The from-v1.7.2 result matches the from-empty incremental-replay and baseline results exactly | Unit test / Live | Existing `Baseline_And_IncrementalReplay_Produce...` test family, diffed against step 2's captured schema — no drift |
| 4 | ❌ | No migration's final on-disk text differs from what it was at the commit that first introduced it, other than the one already-known #56/#168-class corrections | Live | `git log -p` audit per migration constant; each flagged instance manually confirmed correct |
| 5 | ❌ | A decision on a permanent fixture is made and documented | Live (review) | Closing comment on #155 states the decision and reasoning; ADR 009 revised if the decision changes its stated process |
| 6 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 7 | ❌ | T1 — app starts cleanly in Visual Studio against a database that went through the real v1.7.2 → current upgrade path (not just a from-empty dev database) | Live (T1) | Developer confirms clean startup against the step-2-produced database specifically, not their own long-lived local dev database |
| 8 | ❌ | T2 — Docker smoke test against a container whose database was seeded via the v1.7.2 upgrade path | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .`; mount/copy the step-2 database into the container's data directory before first start; confirm the smoke-test suite in CLAUDE.md's Pre-Push Checklist step 6 passes against it |

---

## Notes

T1 and T2 are both required — T1 specifically because this issue is entirely about
`DatabaseInitializer`/migration correctness (`docs/release-verification.md`'s explicit T1 "When
required" criteria: "any change to `DatabaseInitializer`/`QuotinatorDatabaseInitializer`, migration
SQL, or schema/table-wipe logic"), not only the blanket rule from #168.

This issue is fundamentally a verification/audit task, not a feature build — ADR 009 already
specifies the full mechanism; nothing new needs to be designed except requirement 5's permanent-
fixture decision. Its steps above describe *how* the review is performed, but step 2 (and therefore
the review as a whole) cannot reach a final, complete answer until every other issue in this
milestone has landed its own migrations — this issue is correctly sequenced last, immediately before
milestone close, exactly as `overview.md`'s dependency map and this issue's own "Dependencies: None
— ... should be one of the last things done" text already state.
