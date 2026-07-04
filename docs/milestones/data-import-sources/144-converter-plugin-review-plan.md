# #144 — Converter plugins: generic naming, internal-only slots, configuration options

**Status:** Planning
**GitHub issue:** #144
**Depends on:** #140 (introduced `IQuoteSourceConverter`, `Quotinator.Converters.Vilaboim`, `Quotinator.Converters.NikhilNamal17`) — done, waiting for release

---

## Background

Flagged during #140 planning: the user accepted `vilaboim`/`nikhilnamal17` as plugin names only "for now." See #140's plan doc, "Follow-up work explicitly deferred" section, and #140 comment referencing this issue.

## Spec requirements

1. Review both plugins' actual parsing logic and decide: rename in place (name describes the format/transformation, not the origin repo) vs. split into a generic plugin + per-source configuration.
2. Decide whether to reserve some plugin names/slots for **internal-only** use — not selectable via a user-writable `imports/manifest.json`, only usable from the bundled `data/sources/manifest.json`.
3. If the generic-plugin direction is chosen, design how a manifest entry supplies plugin-specific configuration (e.g. field mappings, regex patterns) to a named converter, without hardcoding per-source logic into the plugin's C# code.
4. Update `schemas/manifest.schema.json`, `IQuoteSourceConverter`, and both plugin implementations to match the decision.
5. Update `data/sources/manifest.json`'s `converter` values if names change.
6. Update `scripts/SOURCES.md`'s converter-plugin workflow section to describe the new naming/config model.

## Non-goals

- Does not require adding new upstream sources.

---

## Step status

Not yet started — decisions in spec items 1-3 must be made before implementation steps can be written.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Plugin naming decision made and documented | N/A | Not yet decided |
| 2 | ❌ | Internal-only plugin slot decision made and documented | N/A | Not yet decided |
| 3 | ❌ | Plugin configuration model designed (if needed) | N/A | Not yet decided |
| 4 | ❌ | `schemas/manifest.schema.json` updated to match decision | Unit test | Not implemented |
| 5 | ❌ | Plugin implementations updated to match decision | Unit test | Not implemented |
| 6 | ❌ | `data/sources/manifest.json` `converter` values updated if renamed | Live | Not implemented |
| 7 | ❌ | `scripts/SOURCES.md` updated | Live | Not implemented |
