# ADR 019 — Every NuGet package version is declared once, centrally

**Status:** Accepted
**Date:** 2026-08-16
**GitHub issues:** #320, #317

---

## Context

Until #320, every `<PackageReference>` in the solution carried its own `Version` attribute. A package
used by *N* projects therefore had *N* independent pins, kept in agreement only by hand, with nothing
enforcing that they agreed.

Dependabot opens one PR per dependency and its update groups do not necessarily span every project
pinning a given package. When they don't, some pins move and the rest stay. NuGet then sees two
versions of the same package through a single dependency graph and fails restore with `NU1605`
(package downgrade) for every project that can see both.

This was not hypothetical, and not a one-off:

- **#317** bumped `Microsoft.Extensions.Logging.Abstractions` 10.0.10 → 10.0.11 in `Quotinator.Data`
  and `Quotinator.Logging`, leaving `Quotinator.Core`, `Quotinator.Changelog`, and
  `Quotinator.Core.Tests` at 10.0.10. Restore failed for four projects, the PR went red, and the
  bump had to be completed by hand across the remaining three.
- The `patch_branch` branch — "Update Logging.Abstractions to 10.0.10 in all projects" — is the same
  manual fix, for the same package, one version earlier.

Six packages were pinned in more than one project and could each reproduce it:

| Package | Projects pinning it |
|---|---|
| `MSTest` | 10 |
| `Microsoft.Data.Sqlite` | 7 |
| `Microsoft.Extensions.Logging.Abstractions` | 5 |
| `SQLitePCLRaw.lib.e_sqlite3` | 4 |
| `Dapper` | 3 |
| `Dapper.Contrib` | 2 |

**The drift was already wider than `NU1605` had revealed.** Taking inventory for #320 found `MSTest`
pinned at three different versions simultaneously — 4.2.3 in four projects, 4.3.0 in six, 4.3.3 in
one. This never failed a build because `MSTest` is a leaf test-framework package: each test project
resolves it independently, with no shared graph for the versions to collide in. The absence of a
restore error was therefore never evidence that versions were consistent — only that nothing had
happened to force them into contact yet.

A secondary cost: the `SQLitePCLRaw.lib.e_sqlite3` CVE-2025-6965 pin carried an explanatory comment
copy-pasted identically into eight `.csproj` files, with no mechanism keeping those copies in step.

---

## Decision

**NuGet Central Package Management is enabled solution-wide.** Every package version is declared
exactly once, as a `<PackageVersion>` in `Directory.Packages.props` at the repository root. A
`<PackageReference>` in any `.csproj` must never carry a `Version` attribute. Adding a package means
one central `<PackageVersion>` entry plus a version-less `<PackageReference>` in each consuming
project.

This is chosen over the alternative of keeping per-project pins and relying on review discipline to
catch partial bumps. Discipline had already failed twice on the same package, and the `MSTest` finding
showed it failing silently in a third place nobody had reason to look. Under CPM a partial bump is
not expressible, so the failure mode is removed structurally rather than guarded against.

**`RepositoryStructureTests.PackageReferences_DoNotCarryInlineVersions` enforces it mechanically.**
The test enumerates `.csproj` files under `src/`, `tests/`, and `tools/` specifically, rather than
walking the repository: git worktrees (e.g. `.claude/worktrees/`) are git-excluded but hold stale
copies of every project file, which would otherwise produce phantom failures.

**Security-driven pins live in their own `<ItemGroup>`** in `Directory.Packages.props`, each carrying
its full rationale as a comment — one authoritative copy rather than one per consuming project.

**`MSTest` is unified on 4.3.3**, the highest version already present, so no project downgrades.

**`VersionOverride` is not used anywhere and is not to be introduced casually.** It is the
per-project escape hatch that reopens precisely the divergence this ADR closes. A project genuinely
needing a different version is a decision to raise, not to encode silently.

---

## Consequences

- A dependency bump is a single edit. Dependabot supports CPM natively and edits that one file; its
  existing `directory: "/"` configuration needs no change (verified during #320).
- Adding a *new* package costs one extra edit versus before — the central `<PackageVersion>` entry
  alongside the project's reference. This is accepted deliberately as the price of the guarantee.
- A single shared version means analyzer changes bundled with a package land across every consuming
  project simultaneously, instead of arriving project-by-project. Unifying `MSTest` on 4.3.3
  immediately surfaced `MSTEST0068` at three call sites still using `CollectionAssert.AreEqual`,
  which the 4.2.3 projects had never been told about. Expect this on future test-framework bumps: it
  is the drift becoming visible, not new breakage.
- The absence of an `NU1605` error is no longer load-bearing as a consistency signal, because
  inconsistency is no longer representable.
- `Directory.Packages.props` becomes a merge point: two branches each adding a package touch the same
  file. This is a smaller cost than the divergence it replaces, but it is a real one.
