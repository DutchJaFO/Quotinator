# CVE handling workflow

This workflow applies whenever a CVE alert is raised against a dependency — by Dependabot, via the NVD, or discovered manually.

The template for all CVE documents is [`cve-template.md`](cve-template.md) (in this folder).

---

## Lifecycle

| Status | Meaning |
|--------|---------|
| **Investigating** | Alert received; impact not yet assessed |
| **Mitigated** | Workaround or guard in place; upstream fix not yet available |
| **Patch Available** | Upstream released a fix; package update in progress |
| **Closed** | Fix merged and verified; document moved to `archived/` |

---

## Steps

### 1. Identify affected projects

Check which projects reference the vulnerable package, directly or transitively. Every affected project gets its own CVE document.

### 2. Create the CVE folder

For each affected project:

```
src/Quotinator.ProjectName/CVE/
tests/Quotinator.ProjectName.Tests/CVE/
```

Copy `docs/workflow/cve-template.md` into each folder as `CVE-YYYY-NNNNN.md`.

### 3. Fill in the document

- Set status to **Investigating**
- Describe the vulnerability in plain English (non-technical reader)
- Note whether this project's usage actually triggers the vulnerable code path
- Add the GitHub issue number (create one if none exists; one issue can cover multiple projects)
- Leave the Dependabot dismissal section empty until the alert is dismissed

### 4. Assess and mitigate

Determine whether the project's queries or usage patterns trigger the vulnerability. Document the finding in the mitigation section.

If mitigation is possible (guards, query restructuring, centralisation):
- Implement and test
- Add `// CVE-YYYY-NNNNN` markers in code files that guard against or are affected by the CVE
- Reference the CVE in XML `<summary>` docs on the relevant classes/methods
- Update status to **Mitigated**

If an ADR is warranted (significant architectural decision), write one and link it from the CVE document.

### 5. Dismiss the Dependabot alert

Write the dismissal reason (280 characters max) and record it in the CVE document under **Dependabot > Dismissal reason**.

Use **Tolerated risk** as the dismissal category when the vulnerability is real but mitigated in-application. Use **False positive** only if the project provably cannot reach the vulnerable code path.

### 6. Update the summary

Add a row to [`docs/security/README.md`](../security/README.md) with the CVE ID, package, version range, status, affected projects (linked), and GitHub issue.

### 7. Update the solution

Add all new CVE documents and any new CVE folders to `Quotinator.slnx` as flat top-level `<Folder>` entries. See the CLAUDE.md slnx rules.

### 8. Update SECURITY.md (first CVE in a project only)

`SECURITY.md` at the repo root points to `docs/security/README.md`. No per-CVE detail lives there — the pointer is sufficient.

---

## When upstream patch ships

1. Merge the Dependabot PR that bumps the package
2. Remove any `NU1903` or similar suppressions that were added as workarounds
3. Update `SECURITY.md` and `docs/security/README.md` if referenced there
4. Update status to **Closed** in each per-project CVE document
5. Move each `CVE-YYYY-NNNNN.md` to the `archived/` subfolder within the same `CVE/` folder
6. Update `docs/security/README.md` — move the row from **Active** to **Archived**
7. Update the slnx to reflect the moved files

---

## Archiving

Closed CVE documents are moved to `CVE/archived/` — never deleted. The `CVE/` top-level folder holds only active (Investigating / Mitigated / Patch Available) documents.
