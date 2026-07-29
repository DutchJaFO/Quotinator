##### *GENERATED FILE [2026-07-29 04:25 UTC] — do not edit by hand.*

# Changelog

All notable changes to this add-on will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/).

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

## [1.7.2] - 2026-06-29

- Internal improvements — no user-facing changes.

---

## [1.7.1] - 2026-06-28

- Endpoint requests are now logged as a matched start and end pair — a short ID links both lines, making it easy to trace overlapping calls. Web page and asset requests are separated into their own log categories so the default output shows only API activity.
- Validation errors on quote endpoints now return the correct HTTP error status code — filters with invalid values return 422, structurally invalid requests return 400. Clients can now detect errors by HTTP status code alone without parsing the response body.
- Every write operation is now recorded in an audit log — see who did what, on which record, and when. Administrators can view and clear the log via the API.
- Security: a log injection vulnerability (CWE-117) in the request logging middleware was identified and fixed — crafted request paths and HTTP methods could no longer forge fake log entries.
- Security: the existing CVE-2025-6965 mitigation (SQLite aggregate query guard) was extended to four new projects added in this release; no user data was affected.

---

## [1.7.0] - 2026-06-27

- The search endpoint now explains when no quotes match your filters, instead of returning an empty list silently.

---

## [1.6.5] - 2026-06-24

- Internal improvements — no user-facing changes.

---

## [1.6.4] - 2026-06-24

- Internal improvements — no user-facing changes.

---

## [1.6.3] - 2026-06-23

- The startup log now shows a summary of server addresses, database statistics, and key configuration values — useful for verifying a deployment at a glance.

---

## [1.6.2] - 2026-06-22

- Reset Database no longer crashes the add-on on every restart after being used (issue #106).

---

## [1.6.1] - 2026-06-22

- The language selector has moved from the navbar into a new Settings section in the side menu, fixing an overlap with the hamburger button on mobile.

---

## [1.6.0] - 2026-06-22

- Unreleased changes are now shown at the top of the changelog page.
- Each release now shows which GitHub issues and CVEs it addresses in a collapsible section.

---

## [1.5.1] - 2026-06-20

- Internal improvements — no user-facing changes.

---

## [1.5.0] - 2026-06-20

- Admin endpoints (reseed, reset) now require an API key supplied via the `X-Api-Key` request header.
- The Scalar API reference now shows an Authentication panel at the top — enter your key once and it is sent automatically on all admin requests.
- The startup log now shows whether an admin API key is configured.
- The REST API page in the UI shows the admin endpoints when a key is active.

---

## [1.4.3] - 2026-06-20

- Internal improvements in preparation for upcoming data import features.

---

## [1.4.2] - 2026-06-20

- Fixed: the Docker image was incorrectly reporting version 1.0.0 — the actual version is now shown correctly.
- The REST API page now includes a direct link to the version endpoint.
- Internal improvements — no other user-facing changes.

---

## [1.4.1] - 2026-06-20

- Fixed: the changelog page now shows plain-English release summaries for all versions instead of technical details.
- Internal improvements in preparation for upcoming data import features.

---

## [1.4.0] - 2026-06-20

- Security: a database query vulnerability (CVE-2025-6965) was identified and mitigated; no user data was affected.

---

## [1.3.0] - 2026-06-17

- Quotes can now be loaded from multiple data sources — bundled datasets and your own custom files placed in the imports folder.
- New preview endpoint lets you see what would be imported before committing any changes.
- Configuration: the data directory is now set by pointing to a folder, not a file path — update `Quotinator__DataDir` if you have a custom setup.

---

## [1.2.2] - 2026-06-16

- Fixed: the GitHub changelog link in the UI opened inside the HA frame and was blocked by GitHub's security policy — it now opens in a new tab correctly

---

## [1.2.1] - 2026-06-16

- The database file is now named `quotinatordata.db` — on first startup after upgrading, the old `quotes.db` is renamed automatically with no data loss
- A backup of the database is created automatically before any schema migration
- Container log output is now single-line and easier to read; the startup banner shows all data paths at a glance

---

## [1.2.0] - 2026-06-16

- Admin endpoints (reseed, reset) are now protected by an API key — they return 401 by default and only accept requests with the correct `Authorization: Bearer <key>` header

---

## [1.1.0] - 2026-06-15

- You can now filter quotes by more than one genre or type at once — for example, get only sci-fi comedies or drama films
- Sci-fi and non-fiction quotes were missing from search and random results; both genres now work correctly
- Two new admin endpoints let you reseed or fully reset the quote database without restarting the container

---

## [1.0.15] - 2026-06-15

- Fixed a session issue in Home Assistant where the interface lost its state after the container restarted

---

## [1.0.14] - 2026-06-15

- Internal improvement — no user-facing changes

---

## [1.0.13] - 2026-06-15

- Bug fix — no user-facing changes

---

## [1.0.12] - 2026-06-15

- Quotes are now stored in a local database rather than a flat file — faster, more reliable, and ready for future write support ([#7](https://github.com/DutchJaFO/Quotinator/issues/7))
- The version endpoint now also reports the database schema version and record counts

---

## [1.0.11] - 2026-06-14

- Add-on configuration options now display translated names and descriptions in English, Dutch, and German

---

## [1.0.10] - 2026-06-14

- Startup log now prints as a single, readable block with a configuration summary

---

## [1.0.9] - 2026-06-14

- New option: log one line per API request — useful for confirming calls arrive without enabling full debug logging
- New option: choose how much detail appears in the supervisor log
- All log lines now show a UTC timestamp

---

## [1.0.8] - 2026-06-14

- Fixed: the API Reference, OpenAPI spec, and health check links on the home page did not work through the Home Assistant ingress ([#8](https://github.com/DutchJaFO/Quotinator/issues/8))

---

## [1.0.7] - 2026-06-14

- Quotes and session data now survive container restarts and add-on updates — no data loss on update
- Fixed: the Blazor page (including the "New quote" button) did not load correctly in the Home Assistant sidebar

---

## [1.0.6] - 2026-06-14

- Optional HTTPS on the direct access port — enable ssl in the add-on configuration to use your Let's Encrypt certificate
- Fixed: interactive elements such as the "New quote" button did not work in Docker or the HA add-on

---

## [1.0.5] - 2026-06-14

- Dependency updates — no user-facing changes

---

## [1.0.4] - 2026-06-14

- Language selector in the navigation bar — override your browser's language preference; the choice is remembered for a year
- AppArmor security profile added to the Home Assistant add-on

---

## [1.0.3] - 2026-06-14

- Documentation corrections — no user-facing changes

---

## [1.0.2] - 2026-06-14

- Bug fix — no user-facing changes

---

## [1.0.1] - 2026-06-13

- Bug fix — no user-facing changes

---

## [1.0.0] - 2026-06-13

- Initial release: 780 curated quotes from films, TV, books, and famous people
- REST API with random, list, search, and detail endpoints; multi-language support; rate limiting
- OpenAPI documentation at /scalar/v1

---

## [1.0.0-beta.1] - 2026-06-13

- Initial release: REST API, health check endpoint, Blazor UI placeholder
- 780 curated quotes from films, TV, books, and famous people
- Multi-arch Docker image (`linux/amd64` + `linux/aarch64`)
- Home Assistant ingress on port 8099; direct access on port 8080
