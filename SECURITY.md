# Security Policy

## Scope

Quotinator v1 is a read-only API with no authentication, no write endpoints, and no database. The attack surface is intentionally minimal.

Known non-issues for v1:
- SQL injection — no database in v1 (flat-file JSON only)
- Authentication bypass — no auth to bypass
- Data exfiltration — the entire dataset is publicly served

## Supported versions

Only the latest release is supported.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Report vulnerabilities by emailing: **dutch.jafo@gmail.com**

Include:
- A description of the vulnerability and its potential impact
- Steps to reproduce or a proof of concept
- The version or commit you tested against

You can expect an acknowledgement within 7 days. This is a personal project maintained by one person — response times may vary.
