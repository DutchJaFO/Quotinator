# #183 — List-endpoint shared infrastructure for the masterdata and Conversations list endpoints

**Status:** Planning
**GitHub issue:** #183
**Depends on:** none

---

## Description

Parent (tracking) issue. Builds the shared foundation seven consumers need before any of them can add
a list endpoint: a generic paginated repository capability, one pagination contract, a generic page
response, and the routing/tag/filter conventions. Carries no implementation of its own — see
`issues.md` → "Splitting an issue into sub-issues" for why a parent's plan doc mirrors `overview.md`
rather than carrying Steps and a verification checklist.

---

## Sub-issue list

| # | Title | Status | Tiers | Plan doc |
|---|-------|--------|-------|----------|
| [#193](https://github.com/DutchJaFO/Quotinator/issues/193) | Generic listable repository capability + DI registrations for the six list entities | Planning | T1 ⬜ T2 ⬜ | [193-listable-repository-plan.md](193-listable-repository-plan.md) |
| [#194](https://github.com/DutchJaFO/Quotinator/issues/194) | Numeric query params published to the OpenAPI spec as string — transformer only covers year params | Planning | T1 ⬜ T2 ⬜ | [194-numeric-param-schema-plan.md](194-numeric-param-schema-plan.md) |
| [#195](https://github.com/DutchJaFO/Quotinator/issues/195) | Standard pagination contract: PageResponse&lt;T&gt;, shared parsing and not-found helpers | Planning | T1 ⬜ T2 ⬜ | [195-pagination-contract-plan.md](195-pagination-contract-plan.md) |
| [#196](https://github.com/DutchJaFO/Quotinator/issues/196) | Masterdata conventions: ApiTags.MasterData, /masterdata/ routing, filter-parameter shape | Planning | T1 ⬜ T2 ⬜ | [196-masterdata-conventions-plan.md](196-masterdata-conventions-plan.md) |

---

## Dependency map

```
#193 (listable repository + DI) → requires nothing; unblocks #184, #185, #186, #187, #188, #189
#194 (numeric params typed as string) → requires nothing; unblocks #195 — #195 converts /admin/audit and /import/actions to string? binding, which without #194's transformer fix would regress their published schema from integer|string to bare string
#195 (pagination contract + helpers) → requires #194; unblocks #184, #185, #186, #187, #188, #189
#196 (masterdata conventions) → requires nothing; unblocks #184, #185, #186, #187, #188 (routing + tag) and #192 (filter convention only)
```

#193 and #196 are independent of everything and of each other — either may run first, or both in
parallel with #194.

---

## Order of operations

| # | Issue | Title | Status |
|---|-------|-------|--------|
| 1 | #194 | Numeric query params published to the OpenAPI spec as string | Planning |
| 2 | #193 | Generic listable repository capability + DI registrations | Planning |
| 3 | #196 | Masterdata conventions: ApiTags.MasterData, /masterdata/ routing, filter shape | Planning |
| 4 | #195 | Standard pagination contract: PageResponse&lt;T&gt;, shared helpers | Planning |

#194 is sequenced first because it is #195's hard blocker and is self-contained. #193 and #196 sit
between them only because they are independent and can absorb any wait; neither blocks the other.
