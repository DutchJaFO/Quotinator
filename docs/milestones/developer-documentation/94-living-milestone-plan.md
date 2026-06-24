# Plan: #94 — Define completeness criteria for living milestones

**Status:** Complete

## Problem

The workflow has no model for living milestones (continuous scope). The Developer Documentation milestone (#16) is the first example. Without a model, there is no defined cycle end or closure gate.

## Decision

**Time-boxed cycles (~30 days).** At cycle end, if at least one issue was closed, close the milestone and open a new cycle. If zero issues were closed, extend the due date by 30 days instead — a cycle with no progress produces no useful boundary.

## No scope changes

Spec implemented as written. Decision recorded as required.

## Changes

- `docs/workflow/process.md` — "Living milestones" section added after "Closing a milestone"
- GitHub milestone #16 description and due date updated via `gh api`

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Decision recorded on completeness model | Doc review | Time-boxed cycle model in process.md |
| 2 | ✅ | `process.md` updated with living milestone section | Doc review | Section present after "Closing a milestone" |
| 3 | ✅ | Developer Documentation milestone description and due date updated | Live | `gh api repos/DutchJaFO/Quotinator/milestones/16` reflects new description and due_on |
