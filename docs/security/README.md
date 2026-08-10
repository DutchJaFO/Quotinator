# Security issues

Summary of all CVEs this project has been alerted to. See [`docs/workflow/cve.md`](../workflow/cve.md) for the handling process and [`docs/workflow/cve-template.md`](../workflow/cve-template.md) for the per-project document template — that workflow covers NuGet dependency CVEs only; see "Docker base image (OS packages)" below for the separate category of OS-level findings in the Docker runtime image.

## Active

None.

## Archived

| CVE | Package | Version range | Status | Projects | Issue |
|-----|---------|---------------|--------|----------|-------|
| CVE-2025-6965 | `SQLitePCLRaw.lib.e_sqlite3` | ≤ 2.1.11 | Closed | [Core](../../src/Quotinator.Core/CVE/archived/CVE-2025-6965.md), [Core.Tests](../../tests/Quotinator.Core.Tests/CVE/archived/CVE-2025-6965.md), [Data](../../src/Quotinator.Data/CVE/archived/CVE-2025-6965.md), [Data.Testing](../../src/Quotinator.Data.Testing/CVE/archived/CVE-2025-6965.md), [Data.Tests](../../tests/Quotinator.Data.Tests/CVE/archived/CVE-2025-6965.md), [Data.Testing.Tests](../../tests/Quotinator.Data.Testing.Tests/CVE/archived/CVE-2025-6965.md), [Tools.DbInspector](../../tools/Quotinator.Tools.DbInspector/CVE/archived/CVE-2025-6965.md), [Tools.DbInspector.Tests](../../tests/Quotinator.Tools.DbInspector.Tests/CVE/archived/CVE-2025-6965.md) | #72 |
| CVE-2026-49451 | `Microsoft.OpenApi` | >= 2.0.0-preview11, <= 2.7.4 | Closed | [Api](../../src/Quotinator.Api/CVE/archived/CVE-2026-49451.md) | N/A |

## Docker base image (OS packages)

A separate category from the two tables above: OS package vulnerabilities in the
`mcr.microsoft.com/dotnet/aspnet:10.0` runtime base image, found by [Docker Scout](https://docs.docker.com/scout/)
scanning the built image rather than Dependabot/NVD scanning a NuGet dependency. These don't go
through the per-project `docs/workflow/cve.md` workflow above — there's no NuGet package and no single
owning project, since the vulnerability is in the OS layer every project's Docker image shares (see
#232 for why the two are kept separate). Tracked here as accepted residual risk: every entry below had
no fix available anywhere (Docker Scout: "Fixed version: not fixed") as of the scan date, so nothing
is actionable until Ubuntu/Microsoft ships a patched package — re-checked periodically on rebuild
(#232's own follow-up tracks adding this to the T2 smoke-test checklist).

**Last scanned:** 2026-08-10, `quotinator:local` built fresh with `docker build --no-cache`, via
[`docs/release-verification.md`'s T4 tier](../release-verification.md#t4--docker-image-freshness-milestone-close)
(#250). 10 CVEs now, up from 8 on 2026-08-06 — two new `systemd` findings
(CVE-2026-16742, CVE-2026-15059, both Medium). Both report a fixed Ubuntu package version
(`255.4-1ubuntu8.17`), unlike every other entry in this table, but that fix has **not** reached
Microsoft's base image yet — confirmed by pulling `mcr.microsoft.com/dotnet/aspnet:10.0` fresh
(`docker pull`, not just `--no-cache`) and re-scanning it directly: still `systemd
255.4-1ubuntu8.16`, same two CVEs. Still nothing actionable from Quotinator's side today — tracked
here as the same accepted-residual-risk category as the rest, but re-check on the *next* rescan
whether Microsoft has picked up the Ubuntu fix (unlike the other 8, this one could resolve itself on
a routine rebuild once they do, no watching-for-a-CVE-fix required). All 10 originate in the base
image itself, confirmed by scanning `mcr.microsoft.com/dotnet/aspnet:10.0` directly and getting an
identical result — Quotinator's own application layer contributes none.

| CVE | Package | Severity | Fixable |
|---|---|---|---|
| CVE-2026-16742 | systemd | Medium | Not yet — fixed in Ubuntu, not yet in the Microsoft base image |
| CVE-2026-15059 | systemd | Medium | Not yet — fixed in Ubuntu, not yet in the Microsoft base image |
| CVE-2026-2219 | dpkg | Medium | No |
| CVE-2026-13757 | p11-kit | Medium | No |
| CVE-2026-27456 | util-linux | Medium | No |
| CVE-2026-27171 | zlib | Low (CVSS 5.5) | No |
| CVE-2026-40228 | systemd | Low (CVSS 3.3) | No |
| CVE-2025-5222 | icu | Low | No |
| CVE-2024-2236 | libgcrypt20 | Low | No |
| CVE-2024-56433 | shadow | Low | No |
