# The log reports that an antiforgery token could not be decrypted

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** —

## Symptom

Two `Error` lines together, when a browser opens a page:

```
[Runtime - Exception] 7871ac63 thrown: CryptographicException
System.Security.Cryptography.CryptographicException: The key {8a6fb17f-643b-45d2-b8eb-b233ba807962} was not found in the key ring.
```

```
[Runtime - Exception] 8c9df215 thrown: AntiforgeryValidationException
Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException: The antiforgery token could not be decrypted.
```

The frames end in `DefaultAntiforgery.GetCookieTokenDoesNotThrow`.

## Does it prevent the app or API from functioning?

**No.** The web server discards the cookie it cannot read and issues a new one in the same response. The
page renders, and forms and buttons on it work.

## Cause

**The browser sent a cookie protected by a key the application no longer has.** The keys live in the
`keys/` folder of the data directory. When that folder is gone, a browser that visited before still holds
cookies it cannot read, and the first page it opens afterwards produces these lines once.

Known ways to lose the folder:

- **The data directory was recreated** — a new Docker volume, a reinstall, or a manually cleared data
  folder. Observed 2026-09-18: a test container recreated on the same port, with a browser that had
  visited the previous one.
- **The data directory is not persistent**, so the keys do not survive a restart. Whether that
  produces these lines has not been observed.

## Remedy

Nothing. The browser gets a new cookie on the same request, and the lines do not repeat for that
browser. If they reappear after every restart, check that the data directory is on persistent
storage.

## Notes

These lines only appear from 1.9.0-alpha, when every thrown exception started being logged
([#397](https://github.com/DutchJaFO/Quotinator/issues/397)). Earlier versions handled the same cookie
the same way and showed nothing.
