# ADR 021 — Every file used as input is validated against the schema that defines it

**Status:** Accepted
**Date:** 2026-09-09
**GitHub issues:** #384

---

## Context

This project reads files from several places: an uploaded file on `POST /api/v1/import`, bundled seed
files under `data/sources/`, user-supplied ones under `{dataDir}/imports/`, the `manifest.json` beside
each, per-source conflict-rule, alias and exclusion files, raw upstream downloads before conversion,
the changelog, and the UI string tables.

Every one of them is accepted on the same basis: a deserialiser did not throw. Nothing is checked
against the schema that defines its format.

Nine schemas exist under `schemas/`. None is enforced at runtime — the only references to a schema file
anywhere in `src/` are XML doc comments naming one. Enforcement exists solely in the test suite
(`SourceDataIntegrityTests`, `ChangelogSchemaTests`), against content this repository ships, at build
time. A file that arrives from anywhere else is structurally unchecked.

"The deserialiser did not throw" is a weaker and different property than "conforms to the schema", and
the difference is not academic. `System.Text.Json` will happily accept a document that omits an entire
declared section, carries an unexpected shape in a field it maps loosely, or violates any constraint
the schema states but the C# type does not — `uniqueItems`, `pattern`, `minimum`, an enumerated set
wider in the DTO than in the schema. It throws only where the C# type system happens to object, most
visibly on a `required` property. Acceptance is therefore decided by an accident of how the DTO was
written rather than by the contract the format actually has.

The failure this produces is also misreported. A file that is valid JSON but violates the schema is
rejected with *"empty or not valid JSON"* — sending a curator to look for a syntax error that is not
there, in a file that may hold hundreds of entries, with nothing naming the section at fault.

Not every format has an official schema language. JSON has JSON Schema; XML has XSD, with a validator
in the framework. CSV has neither, and the project already consumes it through a converter.

## Decision

**Every file used as input is validated against the schema that defines it, before any of its content
is accepted — using the official validator for its format wherever one exists.** This binds every
endpoint and every method that takes a file as input, not any single reader.

Four rules follow.

1. **Validation is structural, not incidental.** Conformance is decided by a validator against a
   declared schema. "It deserialised" is not validation and does not satisfy this rule.

2. **A non-conforming file is rejected whole.** One malformed entry means the remainder cannot be
   assumed sound; importing the rest would be assuming exactly that. Partial acceptance is not an
   option this ADR leaves open, and a future reader must not reintroduce it as a convenience.

3. **A format failure is reported as a format failure.** Invalid syntax and schema non-conformance are
   different outcomes and every surface must let a reader tell them apart. Where the validator can name
   the failing section or property, the report carries it; where it cannot, naming the format failure
   is sufficient.

4. **A format with no official schema language gets a defined contract of its own.** The absence of a
   standard is not an exemption — it is the work. CSV is the first such case; anything added later
   (including XML, expected but not yet read) inherits the same requirement, using its official
   validator when it has one.

**Validation goes through one entry point, and that is the load-bearing part.** A per-reader check is
the same rule written a dozen times, and the dozenth is the one that gets forgotten — which is exactly
how nine schemas came to exist with none enforced. A reader that bypasses the entry point is a defect
the build should be able to detect, not a style preference.

## Consequences

**An official JSON Schema validator becomes a runtime dependency.** `JsonSchema.Net` already does this
job in `Quotinator.Core.Tests`; satisfying rule 1 means promoting it to a `src/` package. That is a
real cost against this project's "keep the dependency footprint small" priority, and it is accepted
deliberately: the alternative is hand-rolled per-DTO checks, which restate the schema in C# and then
drift from it — reproducing, in a more expensive form, the gap this ADR exists to close.

**Four inputs have no schema yet** — CSV converter input, the UI string tables, the bulk-decide import
file, and any format added later. Rule 4 makes writing those schemas part of the work rather than a
reason to skip validation for them.

**Two existing messages become wrong and must change.** `POST /import`'s `422` detail and
`SeedFileIssue.InvalidJson` / `ErrorSeedFileInvalidJson` both assert "not valid JSON" for files that
are valid JSON. They describe the mechanism that happened to reject the file, not the failure.

**Bundled content is validated at runtime too, not only in tests.** The build-time check stays useful —
it fails a bad commit before anything ships — but it is not the guarantee this ADR asks for, and a
runtime path that trusts `data/sources/` because "we wrote it" is the same unchecked acceptance in a
narrower place.

**A binary input is out of scope.** `POST /admin/backup/upload` takes a SQLite database; its integrity
check is a different mechanism and this ADR does not govern it.

Implemented by [#384](https://github.com/DutchJaFO/Quotinator/issues/384).
