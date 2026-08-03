##### *GENERATED FILE [2026-08-02 23:10 UTC] — do not edit by hand.*

# Changelog

All notable changes to this add-on will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/).

## [1.8.2] - 2026-07-31

- Security: a SQLite library vulnerability (CVE-2025-6965) has now been fixed directly by upgrading the affected native library; no user data was affected.

---

## [1.8.1-beta] - 2026-07-30

- A separate Beta add-on is now available in Home Assistant, letting you try upcoming changes without affecting your stable installation — install both side by side.
- Changelogs shown in the app and the Home Assistant add-on now list only the most recent releases, with a link to the full history on GitHub.

---

## [1.8.0] - 2026-07-29

- Security: an OpenAPI documentation library vulnerability (CVE-2026-49451) was identified and fixed; the vulnerable code path was never reachable in Quotinator, and no user data was affected.
- Setting up Quotinator for the first time is now faster — a brand-new database is created directly, instead of stepping through years of internal upgrade history.
- Quotinator can now automatically refresh its bundled and imported quote sources from their original locations, keeping data up to date without needing a new release.
- Duplicate quotes found during import can now be merged field by field instead of only being kept or replaced outright, and every duplicate encountered is now recorded for future review.
- The default behaviour for duplicate quotes changed from keeping the first version seen to keeping the newest version — matching what most people would expect when a source file is corrected or updated.
- Quotes can now be imported one file at a time (JSON or CSV) through a new API endpoint, with a preview mode that shows exactly what would happen before anything is written.
- Duplicate quotes flagged for manual review can now be resolved field by field, choosing which side wins or supplying your own value — changes only take effect once every conflict from the same import file has been decided.
- An import that needs review can now be finished later by referencing its batch, without needing to re-upload the file.
- An applied import can now be undone through a new endpoint — reversing everything it added or changed, as long as no newer import has happened since.
- Adding a new quote source in CSV or JSON format usually no longer requires writing any code — a manifest entry with simple field-mapping options is now enough for most formats.
- Multi-line exchanges — a back-and-forth between characters, with stage directions and sound cues in between — can now be fetched as a single ordered conversation, and a random quote that's part of one no longer shows up on its own without the rest of the exchange.
- A source's title, type, or release date can now be corrected after the fact — for example fixing a typo in a film title, or filling in a missing year — without creating a duplicate entry.
- Stage directions and sound cues used in multi-line conversations can now be corrected after the fact too, the same way a source's details already could.
- A conversation's description can now be corrected after the fact too; the lines that make up the conversation itself are unaffected.
- A person's name, date of birth, and date of death can now be corrected after the fact too; date of birth and date of death can now actually be set for a person for the first time.
- Quotes are now correctly grouped by the film series or fictional universe they belong to, and can be filtered by series or universe when searching, listing, or requesting a random one.
- Sources, characters, people, series, universes, stage directions, and sound cues can now all be listed and looked up individually through new endpoints, the same way quotes already could.
- Correcting a source, person, stage direction, sound cue, or conversation by re-importing it now only changes the fields the file actually mentions — a field left out is no longer silently reset to blank.
- Pagination is now consistent across every list endpoint: requesting more than 500 items at once is rejected instead of silently limited, and requesting every matching item as one page (`pageSize=0`) works the same way everywhere.
- Most films, shows, and other sources shown by quotes now include a release date — previously this was almost always left blank.
- A character who appears across multiple entries in the same series (like a recurring character in a trilogy) is now recognised as the same character instead of being tracked separately for each entry, as long as they're the same type of media.
- A character's name can now be corrected after the fact too, the same way a source's, person's, stage direction's, sound cue's, and conversation's details already could.
- A series's or universe's name (and a series's linked universe) can now be corrected after the fact too, the same way a source's, person's, character's, stage direction's, sound cue's, and conversation's details already could.
- Quotes waiting for review after an import can now be resolved all at once by exporting them to a file, editing the decisions, and importing the file back — instead of deciding each one individually through the API.
- Known, recurring conflicts between bundled quote sources are now resolved automatically on import instead of requiring the same manual decision every time the data is reprocessed.
- A number of duplicate entries caused by inconsistent movie titles across bundled data sources have been merged into one accurate entry each — including every Star Wars film, several Marvel films, The Godfather Part II, and Creed II, among others.
- James Bond has been added as a supported film franchise, alongside The Matrix.
- Rule files that correct recurring, known data conflicts can now be viewed, generated from a decision already made, and removed through new endpoints, instead of only being hand-edited.
- A few more duplicate entries (Airplane!, When Harry Met Sally, and an Avengers film) have been merged into one accurate entry each, and several quotes that were missing which character said them now have that filled in.
- A data-correction feature meant to fill in a missing detail (like which character said a quote) had never actually worked the first time a quote was seen — only on a later re-check — so any such correction made so far silently had no effect; this is now fixed.
- Importing, previewing an import, and reseeding now report exactly what happened per file and per type of data (new, corrected, held for review, etc.) instead of one vague duplicates number.

---

Older releases are available in the full history on GitHub: https://github.com/DutchJaFO/Quotinator/releases
