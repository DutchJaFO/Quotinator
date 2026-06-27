# ADR 007 — CS1591 enforcement on test projects

**Date:** 2026-06-27  
**Status:** Pending — awaiting verification against official documentation  

---

## Context

CS1591 (`Missing XML comment for publicly visible type or member`) is enforced via `<GenerateDocumentationFile>true</GenerateDocumentationFile>` across all projects. The 0-warnings build policy makes any unresolved CS1591 a blocking failure.

Current suppression state:

| Project | CS1591 | Reason on record |
|---------|--------|-----------------|
| `Quotinator.Core` | Active | Required — library code |
| `Quotinator.Data` | Active | Required — library code |
| `Quotinator.Changelog` | Active | Required — library code |
| `Quotinator.Data.Testing` | Active (planned) | Required — library code, public API |
| `Quotinator.Api` | Suppressed project-wide | I18nText source generator produces `UI.g.cs` with no source file to annotate |
| `Quotinator.Constants` | Suppressed project-wide | No documented reason — identified as a gap 2026-06-27; zero warnings exist if suppression is removed |
| `Quotinator.Api.Tests` | Suppressed | Test methods |
| `Quotinator.Core.Tests` | Suppressed | Test methods — 114 warnings without suppression |
| `Quotinator.Data.Tests` | Suppressed | Test methods — 158 warnings without suppression |
| `Quotinator.Changelog.Tests` | Suppressed | Test methods — 70 warnings without suppression |

During the v1.7.0 milestone (#74 planning session, 2026-06-27), the question was raised: should XML doc comments be required on test classes and `[TestMethod]` methods?

The working assumption was that this is contrary to industry convention, since test method names are already self-documenting (`GetAudit_NoApiKey_Returns200` communicates intent without a `/// <summary>`). However, this assumption was **not verified against official C# or .NET documentation** before deferral.

---

## Decision deferred — what needs to be checked

Before closing this ADR either way, verify the following:

1. **Microsoft C# coding conventions** (`docs.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions`) — is there explicit guidance on XML docs for test methods?
2. **.NET Runtime repo coding style** (`github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md`) — does the runtime itself require XML docs on test methods?
3. **MSTest / xUnit project templates** — do official test project templates ship with CS1591 suppressed or active?

---

## Quick wins identified (independent of this decision)

These can be done without resolving the test-method question:

- **`Quotinator.Constants`**: suppression has no documented reason and produces zero warnings if removed. Remove it.
- **`Quotinator.Api`**: scope the suppression to `*.g.cs` files via `.editorconfig` instead of suppressing the whole project. Add `/// <summary>` to the `public partial class Program { }` test-accessibility stub.

---

## Options

**Option A — Require XML docs on test methods**  
Remove suppression from all test projects. Add `/// <summary>` to every test class (~15 classes) and every test method (~340 methods). Ensures total consistency but produces boilerplate comments that restate what the method name already says.

**Option B — Keep suppression on test projects (documented)**  
Keep `<NoWarn>$(NoWarn);1591</NoWarn>` on all four test projects. Apply the quick wins above. Accept that test code is not subject to the same documentation standard as library code. Close this ADR as "not required."

---

## References

- `docs/testing-policy.md` — test project standards
- CLAUDE.md § "Code comments" — XML summary policy for Core and Data
- [GitHub: DutchJaFO/Quotinator #74](https://github.com/DutchJaFO/Quotinator/issues/74) — session where this was raised
