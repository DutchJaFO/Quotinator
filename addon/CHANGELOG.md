##### *GENERATED FILE [2026-06-22 21:06 UTC] — do not edit by hand.*

# Changelog

All notable changes to this add-on will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/).

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
