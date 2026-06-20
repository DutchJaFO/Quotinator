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

See [`docs/security/README.md`](docs/security/README.md) for the current CVE tracking summary and per-project mitigation details.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Report vulnerabilities by emailing: **dutch.jafo@gmail.com**

Include:
- A description of the vulnerability and its potential impact
- Steps to reproduce or a proof of concept
- The version or commit you tested against

You can expect an acknowledgement within 7 days. This is a personal project maintained by one person — response times may vary.
