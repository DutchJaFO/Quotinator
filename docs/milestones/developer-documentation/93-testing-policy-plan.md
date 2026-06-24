# Plan: #93 — Update testing-policy.md: document infrastructure project test pattern

**Status:** Complete

## Problem

`docs/testing-policy.md` lists only `Quotinator.Api.Tests` and `Quotinator.Core.Tests`. `Quotinator.Data.Tests` and `Quotinator.Changelog.Tests` exist but are undocumented. The `[AssemblyInitialize]` rule is described only in Dapper terms. No CVE folder creation rule exists.

## No scope changes

Spec implemented as written.

## Changes

- `docs/testing-policy.md` — project structure section updated with all four test projects; pairing rule added; CVE folder creation rule added; `[AssemblyInitialize]` rule generalised beyond Dapper

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | All four test projects listed in structure section | Doc review | `Quotinator.Api.Tests`, `Quotinator.Changelog.Tests`, `Quotinator.Core.Tests`, `Quotinator.Data.Tests` all present |
| 2 | ✅ | Pairing rule documented: every `src/` project has a `.Tests` counterpart | Doc review | Rule present in project structure section |
| 3 | ✅ | CVE folder creation rule documented | Doc review | Rule present — created at project creation time, not CVE time |
| 4 | ✅ | `[AssemblyInitialize]` rule generalised beyond Dapper | Doc review | Wording covers any global static state; Dapper remains as example |
