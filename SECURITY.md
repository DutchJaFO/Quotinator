# Security Policy

## Scope

Quotinator is a self-hosted, read-only quote API backed by SQLite. The attack surface is intentionally minimal:

- All read endpoints are public — the entire dataset is intended to be served openly
- Write endpoints and authentication are not yet implemented (planned for v3)
- All SQL uses parameterised queries — user input is never concatenated into SQL strings
- Rate limiting is applied to all quote endpoints (100 requests/minute per IP)

## Supported versions

Only the latest release is supported.

## Known vulnerabilities

### CVE-2025-6965 — SQLite aggregate memory corruption

**Status: mitigated; upstream patch pending**

CVE-2025-6965 describes a SQLite bug where the number of aggregate terms in a query exceeds the number of output columns, causing memory corruption. The upstream fix will ship in a future `Microsoft.Data.Sqlite` release.

**Our mitigation (shipped in v1.4.0):**
- All SQL statements are centralised in `Sql.cs` and `RepositorySql.cs` — no inline SQL anywhere else in the codebase
- Automated guard tests scan every SQL constant and every dynamically-assembled query for the vulnerable pattern (`aggregate function + GROUP BY/HAVING`) on every build
- Audit confirmed that none of our queries use `GROUP BY` or `HAVING` with aggregate functions — the vulnerability cannot be triggered by any query we execute

The upstream patch will be applied as soon as it ships in a stable `Microsoft.Data.Sqlite` release.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Report vulnerabilities by emailing: **dutch.jafo@gmail.com**

Include:
- A description of the vulnerability and its potential impact
- Steps to reproduce or a proof of concept
- The version or commit you tested against

You can expect an acknowledgement within 7 days. This is a personal project maintained by one person — response times may vary.
