# #244 — Hidden Roslyn code-style and .NET analyzer diagnostics (IDE0xxx, CAxxxx)

**Status:** In progress (step 6)
**GitHub issue:** #244
**Tiers required:** T1, T2
**Depends on:** none

---

## Background

No `.editorconfig` exists in the repo, and neither `EnforceCodeStyleInBuild` nor `<AnalysisMode>` is
set anywhere — so `IDE0xxx` code-style analyzers and `CAxxxx` built-in analyzers run in Visual Studio's
live analysis only and are completely invisible to `dotnet build`, the same failure mode #197 found and
fixed for `MSTEST0xxx` specifically. The issue itself poses four open questions (severity, preset vs.
hand-picked rules, bulk-fix vs. incremental, split IDE/CA into separate issues) — none of them decided
in the filing.

**Developer instruction (2026-08-06):** turn on the configuration so these become visible, but do it
**one setting at a time**, not all at once — each step must land with the build still at 0 warnings,
matching #197's own precedent of enabling + fixing together rather than enabling and leaving the build
red.

**Current counts, measured 2026-08-06** (`dotnet format style --verify-no-changes --severity info
Quotinator.slnx` / `dotnet format analyzers --verify-no-changes --severity info Quotinator.slnx`,
dry run, no files changed — higher than the issue's own 2026-07-31 figures since the codebase has grown
significantly since then, e.g. #251/#253/#254/#255/#256/#249/#156):

**IDE (Style), 289 total:**

| Rule | Count | What it flags |
|---|---|---|
| IDE0305 | 135 | Collection initialization can be simplified (→ collection expression) |
| IDE0300 | 58 | Collection initialization can be simplified (array side) |
| IDE0290 | 40 | Use primary constructor |
| IDE0037 | 21 | Member name can be simplified |
| IDE0042 | 9 | Variable declaration can be deconstructed |
| IDE0028 | 8 | Collection initialization can be simplified |
| IDE0270 | 4 | Null check can be simplified |
| IDE0130 | 4 | Namespace does not match folder structure |
| IDE0060 | 3 | Unused parameter |
| IDE0059 | 2 | Unnecessary value assignment |
| IDE0039 | 2 | Use local function instead of lambda |
| IDE0301 | 1 | Collection initialization can be simplified (empty collection) |
| IDE0063 | 1 | Use simple `using` statement |
| IDE0044 | 1 | Make field readonly |

**CA (built-in analyzers), 183 total:**

| Rule | Count | What it flags |
|---|---|---|
| CA1861 | 53 | Constant array argument should be `static readonly` |
| CA1873 | 50 | Expensive logging argument evaluated even when the log level is disabled |
| CA1859 | 36 | Concrete types preferred for perf |
| CA1822 | 12 | Member can be marked static |
| CA1806 | 12 | Ignored method result (e.g. `TryParse`) |
| SYSLIB1045 | 9 | Use `GeneratedRegexAttribute` instead of `new Regex(...)` |
| CA2254 | 3 | Logging message template should not vary between calls |
| CA1869 | 3 | Cache and reuse `JsonSerializerOptions` instances |
| CA1507 | 2 | Use `nameof` instead of a string literal |
| CA1068 | 2 | `CancellationToken` parameters should come last |
| CA1826 | 1 | Use a property instead of a Linq method |

472 total, currently invisible to `dotnet build`.

## Decisions confirmed with the developer (2026-08-06)

**1. Enable incrementally, one setting/rule-family at a time, never all at once.** Each step below
lands with `dotnet build`/`dotnet test` at 0 warnings before moving to the next — mirrors #197's own
"enable + fix together" precedent rather than a single flag flip that breaks the build until a later
cleanup pass.

**2. Mechanical rules get bulk-fixed via `dotnet format`; rules that could mask a real bug or require a
genuine design decision get reviewed individually, never blind-applied.** The split below is not
arbitrary — see each step's own reasoning.

## Design details

**How visibility actually gets turned on — two independent knobs:**
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` in `Directory.Build.props` makes `IDE0xxx`
  analyzers *run* at build time (previously VS-only). This alone does not make them appear as
  `dotnet build` warnings — most `IDE0xxx` rules default to `suggestion`/`silent` severity, which is
  why the issue found them via `dotnet format ... --severity info`, not a plain `dotnet build`.
- An `.editorconfig` entry (`dotnet_analyzer_diagnostic.category-Style.severity = warning`) is what
  actually escalates them to visible build warnings — the bulk category-level knob, matching #197's
  "adopt a preset, don't hand-maintain individual rule severities" philosophy, rather than listing all
  14 `IDE0xxx` rule IDs by hand.
- `CAxxxx` analyzers already run by default in an SDK-style project (`EnableNETAnalyzers` defaults to
  `true`); `<AnalysisMode>Recommended</AnalysisMode>` is the equivalent bulk-preset escalation for their
  severities, mirroring #197's `MSTestAnalysisMode=Recommended`.

**Splitting mechanical vs. judgment-requiring rules:**
- *Mechanical* (`dotnet format --fix` auto-applies correctly, purely syntactic, no behaviour change):
  IDE0305/IDE0300/IDE0301/IDE0028 (collection expressions), IDE0037 (member name), IDE0042
  (deconstruction), IDE0270 (null check), IDE0059 (unnecessary assignment), IDE0039 (local function),
  IDE0063 (using statement), IDE0044 (readonly field); CA1861 (static readonly array), SYSLIB1045
  (GeneratedRegexAttribute), CA1507 (nameof), CA1826 (Linq → property), CA1859/CA1822 (spot-checked
  after auto-fix, since a concrete-type or static-member change can occasionally affect an interface
  or mocking assumption), CA1869 (JsonSerializerOptions caching — needs a static field added, light
  manual pass).
- *Needs individual review, never blind auto-fix:*
  - **IDE0290 (primary constructors, 40)** — the issue's own text flags this as "pure style preference"
    needing its own decision: adopt project-wide, or suppress the rule entirely. Not decided here.
  - **IDE0130 (namespace/folder mismatch, 4)** — all 4 hits are `Quotinator.Core.Tests` files declared
    under namespace `Quotinator.Core.Tests.Data` while living in `Services/`/`Database/` folders (a
    real violation of CLAUDE.md's "File placement rule," not just a style nit) — worth understanding
    before fixing, not blind-renaming.
  - **IDE0060 (unused parameter, 3)** — could be intentional (an interface/delegate signature
    requirement) or a genuine dead parameter; each needs its own look.
  - **CA1806 (ignored method result, 12)** — a silently-ignored `TryParse`-style result is a classic
    correctness-bug shape; each occurrence needs review for whether it's actually a bug.
  - **CA2254 (inconsistent logging template, 3)** — a real logging correctness issue, needs a genuine
    fix per call site.
  - **CA1068 (`CancellationToken` param order, 2)** — a signature change; needs checking whether either
    method is part of a public/tested contract before reordering.
  - **CA1873 (expensive logging argument evaluation, 50)** — by far the largest judgment-required
    item. Fixing this properly means adopting a project-wide pattern (`LoggerMessage` source-generated
    logging, or wrapping call sites in `Logger.IsEnabled(...)` checks) — a genuine architectural
    decision affecting every logging call site in the codebase, not a per-occurrence fix. Given the
    scale (50 occurrences) and that it's orthogonal to the IDE-style/CA-style visibility question this
    issue is actually about, **recommend splitting into its own follow-up issue** once the developer
    confirms — mirroring #227's own decomposition precedent — rather than growing #244 into a second,
    much larger logging-pattern redesign.

## Steps

### Step 1 — `EnforceCodeStyleInBuild=true` alone (no severity escalation)
**Status:** ✅ Done
Added to `Directory.Build.props`. Build confirmed 0 new warnings, as expected — the analyzers now run
at build time but stay at their default `suggestion` severity until Step 2 escalates them.

### Step 2 — Escalate + bulk-fix the mechanical `IDE0xxx` rules
**Status:** ✅ Done — landed as two commits (2a, 2b), one rule family per commit, not one combined pass
`.editorconfig` entries scoped to exactly the mechanical rule IDs listed above (not the whole Style
category yet, since IDE0290/IDE0130/IDE0060 aren't ready) → `warning`. Run `dotnet format style --fix`
restricted to those rule IDs; `dotnet format` does not always converge in one pass (a second occurrence
on the same line, e.g. a second `.ToArray()` argument to `Assert.AreSequenceEqual`, needs a second run
to catch) — re-run until `dotnet build` reports 0 new warnings. **The entire resulting diff is read by
hand before committing, not just trusted** — per explicit developer instruction (2026-08-06): mechanical
rewrite tools apply the fix, but verifying it's actually safe is a manual step, never skipped.

- **2a — Collection-expression family (IDE0305/IDE0300/IDE0301/IDE0028), 202 occurrences: ✅ Done.**
  All four rules flag the exact same transformation (`new[] {...}`/`new List<T> {...}`/`.ToList()`/
  `.ToArray()`/`.ToHashSet()`/`new()` → `[...]`/`[.. ...]`). Full diff (72 files, two `dotnet format`
  passes) read by hand — checked specifically for target-typing changes (none: return-type-annotated
  `HashSet<string>` locals still target `HashSet<string>`, `IReadOnlyList<string>`-cast values are
  read-only in every case, `Dictionary<K,V>` field initializers compile to the same constructor call).
  Full test suite green (0 failures) both before and after escalating severity to `warning`.
- **2b — IDE0037/IDE0042/IDE0270/IDE0059/IDE0039/IDE0063/IDE0044, 40 occurrences: ✅ Done.**
  Full diff (11 files) read by hand. **Found a real bug via manual review, exactly the kind
  `dotnet format --fix` cannot catch on its own**: the IDE0270 fix on
  `SqliteImportActionService.cs` correctly rewrote `if (sourceId is null) throw ...;` into
  `sourceId ?? throw ...`, which narrows `sourceId`'s static type from `Guid?` to `Guid` — but two
  downstream `sourceId.Value` accesses were left in place by the tool and no longer compiled
  (`.Value` doesn't exist on non-nullable `Guid`). Fixed by hand (`sourceId.Value` → `sourceId` at
  both sites). Every other file in the batch checked individually for the same
  "does a later line depend on the pre-fix type/shape" question — none did. Full test suite green
  before and after severity escalation.

### Step 3 — IDE0290 (primary constructors) — developer decision required
**Status:** ✅ Done

**Decision (2026-08-06):** primary constructors adopted with **no exceptions** — every one of the 40
occurrences (~33 classes) converts. Two candidate exceptions were investigated and evaluated first
(a class with individually-`<param>`-documented constructor parameters — `DatabaseInitializer`; a
class chaining to a base constructor with a large combined parameter count —
`QuotinatorDatabaseInitializer`, 18 own + 7 base = 25) and both rejected by the developer: "all
parameters of a constructor should be xml-documented. I see no reason to exclude either of them.
There appears to be no syntactical reason." This sets a **new, stricter standing requirement**:
every constructor parameter — primary or classic, on every class touched by this conversion — gets
its own `<param name="...">` XML doc tag on the class-level summary, not just a generic one-line
class summary. `dotnet format --fix` only performs the mechanical syntax conversion; it does not
write `<param>` docs, so each of the ~33 classes needs its documentation added/verified by hand as
part of this step. **Confirmed CS1591 cannot be relied on as the forcing function for this** — it
only requires *a* doc comment to exist on a public member, not that every parameter has its own
`<param>` tag, so a clean build alone does not prove per-parameter documentation is complete; the
full 1275-line mechanical-conversion diff was read by hand instead to catalogue exactly which
classes already retained adequate documentation (6, via `<inheritdoc/>`-adjacent or pre-existing
per-param docs) versus which needed it added (the remaining ~31, plus 3 private test-only nested
classes correctly exempt under CLAUDE.md's "non-private types only" XML-doc rule). Long combined
parameter lists (`QuotinatorDatabaseInitializer`, `StartupSummaryLogger`, `SqliteImportActionService`)
format one parameter per line, exactly as the existing classic constructor already does — no
readability loss, this was a misplaced concern. One real mistake caught by the build during this
step: a `<see cref="AuditEntryEntity"/>` added to `SqliteLinkRepository.cs`'s new param doc didn't
resolve (CS1574) because that file has no `using Quotinator.Data.Entities;` — fixed by qualifying
the cref as `Entities.AuditEntryEntity`. `.editorconfig` escalates `IDE0290` to `warning` as the
final part of this step; full build (0 warnings, 0 errors) and full test suite (609/609 passed)
verified after escalation.

### Step 4 — IDE0130 (namespace/folder mismatch) — investigate the 4 real cases
**Status:** ✅ Done

All 4 hits were `Quotinator.Core.Tests` files declared under `namespace Quotinator.Core.Tests.Data`
while living outside the `Data/` folder — a genuine violation of CLAUDE.md's file placement rule
(the `Data/` namespace already legitimately exists elsewhere in the project, for files that actually
live in `Data/`, e.g. `SourceDataIntegrityTests.cs`), not a bulk-rename target. Confirmed each of the
4 offending classes has no cross-file reference (no `using Quotinator.Core.Tests.Data;`, no other file
naming the class) before renaming, so each was a same-file, no-risk fix:
- `Database/QuotinatorMigrationsTests.cs` → `Quotinator.Core.Tests.Database`
- `Services/SqliteQuoteServiceConversationTests.cs` → `Quotinator.Core.Tests.Services`
- `Services/SqliteQuoteServiceSearchTests.cs` → `Quotinator.Core.Tests.Services`
- `Services/SqliteQuoteServiceUnicodeSearchTests.cs` → `Quotinator.Core.Tests.Services`

All 49 tests across the 4 affected files, and the full 609-test suite, pass after the rename. Build
0 warnings/0 errors after escalating `IDE0130` to `warning`.

### Step 5 — IDE0060 (unused parameter) — review each of the 3
**Status:** ✅ Done

Two of the three hits were genuinely dead parameters, safely removed (private methods, no interface
contract, all call sites updated):
- `SqliteQuoteService.ToResponse`'s `requestedLang` — never read in the method body; `Language`,
  `OriginalLanguage`, and the computed `IsTranslated` are all sourced from the already-resolved `row`.
- `SqliteImportActionService.ReverseQuoteActionAsync`'s `uow` — the only per-entity-type reverse
  handler extracted into its own method; every sibling entity type is handled inline in the
  enclosing switch where `uow` is already in scope directly, so this one just carried it through
  unused.

**The third — `BuildFilterWhere`'s `lang` — was not dead code, it was a real, live correctness bug**,
present since 2026-06-15 (`51c5ec3d`), unrelated to this issue's own work, found purely as a side
effect of investigating why IDE0060 flagged it. `BuildFilterWhere` hardcoded
`p.Add("lang", (string?)null)` regardless of the caller-supplied `lang` value. Since `Sql.Quotes.
SelectBase` (the shared projection behind `SelectPaged`/`SelectRandom`/`SelectSearch`) needs the real
`@lang` value bound to find and JOIN a translation row, this meant **`GetAll(...,lang:"nl")` and
`Search(...,lang:"nl")` silently ignored every `?lang=` request and always returned the original-
language content** — a direct contradiction of CLAUDE.md's own "API response language" contract.
`GetById` was unaffected (binds `lang` directly, already covered by
`GetById_UppercaseLang_StillMatchesLowercaseStoredTranslation`); `GetRandom` was unaffected in
practice (does its own correct per-row translation lookup after the bulk fetch, so the bulk fetch's
untranslated content was silently discarded and refetched anyway) — but `GetAll`/`Search` have no
such second path, so this was a complete, unconditional no-op for both.

Per explicit developer decision (2026-08-07, asked directly given the scope): **fixed inline as part
of this step**, following the standing red-green requirement — `GetAll_LangRequested_
ReturnsTranslatedContent` (`SqliteQuoteServiceTests.cs`) and `Search_LangRequested_
ReturnsTranslatedContent` (`SqliteQuoteServiceSearchTests.cs`) were added and confirmed to fail
against the pre-fix code (red), then `p.Add("lang", lang)` replaced the hardcoded `null` and both
tests were confirmed to pass (green). No other read path was found to depend on the previous
(incorrect) always-null behaviour. Full build (0 warnings/0 errors) and full test suite (1433 tests
across the whole solution, 0 failures) verified after the fix and after escalating `IDE0060` to
`warning`.

### Step 6 — Escalate + bulk-fix the mechanical `CAxxxx`/`SYSLIB` rules
**Status:** In progress (6a done, 6b in progress)

**`dotnet format analyzers` does not reliably discover the SDK's built-in CA analyzers** the way
`dotnet format style` discovers `IDE0xxx` rules — confirmed live: running it against this repo (with
severities escalated in `.editorconfig`) loaded only 1 analyzer per non-test project and made zero
fixes, even for the purely mechanical rules below. `dotnet build` after escalating each rule's
severity is the actual diagnostic source for this whole step; every fix in 6a/6b was applied by hand,
verified against the rebuilt warning list, per the same standing "verify by reading, don't trust
blindly" instruction as every other step.

**6a — Escalate + fix CA1861/CA1822/SYSLIB1045/CA1869/CA1507/CA1826 (targeted per-rule severities,
not a blanket `<AnalysisMode>Recommended</AnalysisMode>` — that would also pull in
CA1806/CA2254/CA1068/CA1873, each reserved for its own step): ✅ Done.**
- **CA1507 (nameof, 2 occurrences)** — `SqliteQuoteService.BuildFilterWhere`'s two `IdClauses.Equals("...", "seriesId"/"universeId")` calls now use `nameof(seriesId)`/`nameof(universeId)`.
- **CA1826 (Enumerable method on indexable collection, 1 occurrence)** — `QuoteCard.razor.cs`'s `Items.FirstOrDefault()` (an `IReadOnlyList<T>`) replaced with a direct `Count`/index check.
- **CA1869 (JsonSerializerOptions caching, 3 occurrences)** — `IndexedFieldMappingTests.cs`/`QuoteFieldDefaultsTests.cs` (2 call sites) now share one `private static readonly JsonSerializerOptions CaseInsensitive` field per class instead of allocating a new instance per call.
- **CA1822 (mark static, 12 occurrences)** — all 12 were private helper methods (8 in `SqliteImportActionService.cs`'s `Ensure*ExistsAsync`/`ReverseQuoteActionAsync`, 2 test helpers in `ImportActionPlannerTests.cs`) or a protected non-virtual helper (`DatabaseConfiguration.RegisterEnumHandler<TEnum>`) or a Blazor component property never reading instance state (`App.razor.cs`'s `HtmlLang`) — none implement an interface member, so marking `static` needed no call-site changes and carries no mocking-substitution risk (unlike CA1859 below).
- **SYSLIB1045 (`GeneratedRegexAttribute`, 9 occurrences)** — 8 converted to `[GeneratedRegex(...)]` partial methods (making the containing class `partial` where it wasn't already: `RepositorySql`, `ApiLocalizerFormatting`, `QuoteIdentity`, `ChangelogSchemaTests`, `ChangelogEntryTests`, `SqlSourceScanTests`; `ChangelogEntry.razor.cs` was already `partial`). The 9th (`GeneratedFileHeaderTests.TimestampPattern`) turned out to be genuinely dead code — declared but never referenced anywhere in the file — so it was deleted outright instead of converted, along with the now-unused `using System.Text.RegularExpressions;` and the class's now-unnecessary `partial` modifier.
- **CA1861 (`static readonly` over constant array arguments, 9 occurrences)** — `Program.cs`'s locally-scoped `supported` array hoisted to a `private static readonly string[] SupportedCultures` field on the existing `public partial class Program { }` block (top-level statements compile into that same partial class, so it's reachable unqualified from the top-level code); 8 test-fixture array literals (genre arrays passed into anonymous-object JSON fixtures or `CollectionAssert.AreEqual`) hoisted to named `private static readonly string[]` fields per class.
- Full build (0 warnings, 0 errors) and full solution test suite (1433 tests, 0 failures) verified after 6a's escalation.

**6b — CA1859 (prefer concrete types for perf), 32 occurrences: individual review, per explicit
developer decision (2026-08-08) — not bulk-escalated, not bulk-suppressed.** Every occurrence
suggests narrowing an interface- or abstract-typed field/parameter/return type
(`IReadOnlyDictionary`/`IReadOnlyList`/`IReadOnlySet`/`IEnumerable`, or a DI-registered interface
like `IImportActionReader`/`IImportActionWriter`/`IImportActionCoordinator`/`IDbConnectionFactory`/
`IChangelogService`/`IManifestSeedPlanner`) to a concrete class — the exact abstraction this
project's own DI policy and test-double conventions rely on for substitutability. In progress —
see the per-occurrence verdicts below as they're completed.

### Step 7 — CA1806 (ignored method result) — review each of the 12
**Status:** ⬜ Not started

### Step 8 — CA2254 (inconsistent logging template) — fix each of the 3
**Status:** ⬜ Not started

### Step 9 — CA1068 (`CancellationToken` param order) — review each of the 2
**Status:** ⬜ Not started

### Step 10 — CA1873 (expensive logging argument evaluation) — scope decision
**Status:** ⬜ Not started
Present the split-into-follow-up-issue recommendation to the developer; file the follow-up issue if
agreed, referencing #244 as the source. Do not fix the 50 occurrences inline in #244 unless the
developer explicitly says otherwise.

### Step 11 — Full verification
**Status:** ⬜ Not started
`dotnet build --configuration Release` / `dotnet test --configuration Release` — 0 warnings, 0 errors.
T1 (developer's own Visual Studio run), T2 (Docker) — no runtime behaviour changes expected, but both
tiers are always required per `docs/release-verification.md`.

### Step 12 — Docs sync
**Status:** ⬜ Not started
`CLAUDE.md` — document the primary-constructor decision from Step 3 as a house-style convention (either
direction) so it doesn't need re-deciding per file going forward. Changelog entry (internal-only,
mirroring #197's own "no user-facing changes").

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | `EnforceCodeStyleInBuild=true` added, 0 new warnings | Build | Step 1 |
| 2 | ⬜ | Mechanical `IDE0xxx` rules escalated to warning and fixed | Build | Step 2 |
| 3 | ✅ | IDE0290 decision made and applied consistently | Manual | Step 3 |
| 4 | ✅ | IDE0130's 4 real namespace/folder mismatches resolved | Manual | Step 4 |
| 5 | ✅ | IDE0060's 3 unused parameters reviewed and resolved | Manual | Step 5 |
| 6 | ⬜ | Mechanical `CAxxxx`/`SYSLIB` rules escalated and fixed | Build | Step 6 |
| 7 | ⬜ | CA1806's 12 ignored results reviewed, any real bugs fixed | Manual | Step 7 |
| 8 | ⬜ | CA2254's 3 logging template issues fixed | Manual | Step 8 |
| 9 | ⬜ | CA1068's 2 parameter-order issues reviewed | Manual | Step 9 |
| 10 | ⬜ | CA1873 scope decision made (split vs. inline) | Manual | Step 10 |
| 11 | ⬜ | Full build/test 0 warnings 0 errors; T1; T2 | Build + Live | Step 11 |
| 12 | ⬜ | CLAUDE.md updated with the primary-constructor convention | Manual | Step 12 |

---

## Relationship to existing issues

- **#197** — same root cause (analyzer diagnostics invisible to `dotnet build`), same fix pattern
  (preset-based severity escalation over hand-maintained per-rule overrides), scoped to
  `MSTestAnalysisMode` there vs. the general `IDE0xxx`/`CAxxxx` families here.
- **#227** — precedent for decomposing a large, multi-concern issue into sub-issues once its own scope
  became clear during planning; Step 10 may produce a similar decomposition for CA1873.
