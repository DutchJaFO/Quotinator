# Plan: #104 — Workflow: add changelog update step to issue closing checklist

**Status:** Complete

## Problem

No explicit step in `docs/workflow/checklist.md` requires a changelog entry when closing an issue. This led to issue #82 being missing from the v1.6.0 changelog until caught retrospectively.

## No scope changes

Spec implemented as written. Additionally incorporated the release issue-list rule (every release entry tracing back to an issue must carry that issue's number in `issues[]`, including hotfix releases) — this was identified during v1.6.4 work as a related gap.

## Changes

- `docs/workflow/checklist.md` — changelog step added to "Before closing an issue"; release issue-list rule added
- `CLAUDE.md` — Pre-Push Checklist step 3 updated to reference issue-close as the trigger for unreleased entries; release issue-list rule documented

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `checklist.md` contains changelog step in "Before closing an issue" | Doc review | Step present before "User confirms closure" item |
| 2 | ✅ | `CLAUDE.md` Pre-Push Checklist references adding unreleased entries at issue-close time | Doc review | Sentence added to step 3 |
| 3 | ✅ | Release issue-list rule documented in both files | Doc review | Rule present in checklist.md and CLAUDE.md |
| 4 | ✅ | Requirement is clear: changelog entry is part of closing, not a separate PR | Doc review | Confirmed by wording of checklist step |
