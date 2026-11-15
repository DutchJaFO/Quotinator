# SQL Safety — Aggregate Guard Design

This document covers the design of the SQL aggregate guard in `Quotinator.Data`,
the reasoning behind the implementation choices, and what the guardrail is and is not designed to catch.

---

## Context: CVE-2025-6965

SQLite versions before 3.50.2 contain a memory corruption bug triggered when the number of aggregate
terms in a query exceeds the number of columns available. `SQLitePCLRaw.lib.e_sqlite3` ≤ 2.1.11
bundles a vulnerable SQLite version. As of 2026-06-20 there is no patched NuGet version available.

The CVE has two distinct concerns:

1. **Memory safety** — the SQLite bug causes memory corruption at runtime. Fixed by upgrading SQLite.
   We cannot fix this today because no patched NuGet package exists yet.
2. **Query correctness** — a query where aggregate terms exceed available columns is a logic error
   regardless of SQLite version. It will produce incorrect results even on a patched build.
   This is what the guardrail defends against.

---

## What the guardrail detects

`SqlAggregateGuard.IsVulnerablePattern(string sql)` flags any SQL string that contains both:

- **At least one aggregate function call** — `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`, `GROUP_CONCAT`, `TOTAL`
- **A `GROUP BY` or `HAVING` clause**

Any flagged query requires manual review to confirm that the number of aggregate terms does not
exceed the number of output columns in the `SELECT` list.

### What it does NOT do

The guard does not count aggregate terms and compare them to output columns. That count-aware
approach was considered and rejected — see the [design decision](#design-decision) section below.
The guard's job is to surface queries that are *candidates* for the CVE pattern; the count comparison
is a human review step, not an automated assertion.

---

## SQLite aggregate functions covered

| Function | Standard SQL | T-SQL equivalent | Notes |
|---|---|---|---|
| `COUNT(expr)` / `COUNT(*)` | Yes | Yes | |
| `SUM(expr)` | Yes | Yes | |
| `AVG(expr)` | Yes | Yes | |
| `MIN(expr)` | Yes | Yes | |
| `MAX(expr)` | Yes | Yes | |
| `GROUP_CONCAT(expr)` | No — SQLite-specific | `STRING_AGG` | Must be included explicitly |
| `TOTAL(expr)` | No — SQLite-specific | No equivalent | Returns 0.0 for empty sets (SUM returns NULL) |

---

## Design decision

### Option considered: Microsoft T-SQL parser (`Microsoft.SqlServer.Management.SqlParser`)

A proper AST parser would allow counting aggregate terms vs output columns precisely — directly
addressing the CVE pattern rather than using a heuristic.

**Why it was rejected:**

1. **SQLite dialect gaps.** The T-SQL parser has no knowledge of `GROUP_CONCAT` or `TOTAL`. Both are
   parsed as user-defined function calls, not aggregates, producing false negatives.
2. **Package weight.** `Microsoft.SqlServer.Management.SqlParser` is part of SMO and pulls in
   ~15–20 MB of additional assemblies. This conflicts with the project's minimal-dependency principle.
3. **Over-precision for the problem.** Our queries are simple. The dangerous pattern arises when
   developers write complex aggregated queries without care. Flagging *any* aggregate + GROUP BY query
   for manual review is the right level of automation — the count comparison belongs in the code
   review, not a test assertion.

### Chosen approach: heuristic regex

Regex flags the combination of aggregates + GROUP BY/HAVING. Cost: near zero. Coverage: complete
across all SQLite aggregate functions including `GROUP_CONCAT` and `TOTAL`. A flagged query is not
automatically rejected — it requires a human to verify that aggregate term count ≤ column count.

---

## Test coverage

| Test class | Location | What it proves |
|---|---|---|
| `SqlAggregateGuardTests` | `tests/Quotinator.Data.Tests` | The detector flags known-dangerous SQL and passes known-safe SQL |
| `SqlSourceScanTests` | `tests/Quotinator.Core.Tests` | Every SQL string literal in `src/` passes the guard — codebase is currently clean |

If a future query legitimately uses GROUP BY with aggregates and has been reviewed and confirmed safe,
document the reasoning here and add it to the known-safe examples in `SqlAggregateGuardTests`.

---

## When a patched NuGet version ships

This has happened. On 2026-07-30 a direct `SQLitePCLRaw.lib.e_sqlite3` 3.50.3 override brought in a
patched SQLite (#72). Since 2026-11-15 no override is needed: Microsoft.Data.Sqlite 11 takes its native
library from the `SQLite` package, 3.53.4, which is above the patched 3.50.2. The per-project CVE
documents (`CVE/archived/CVE-2025-6965.md`) record each step.

The guardrail remains in place. It defends against query correctness bugs, whatever the SQLite version.
