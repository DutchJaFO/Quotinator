# cache/

Local cache of upstream source files downloaded by `scripts/seed.csx`.

Running the seed script with `--no-fetch` reads from this folder instead of downloading from GitHub, which is useful when working offline or when avoiding repeated network requests during development.

```bash
dotnet-script scripts/seed.csx -- --no-fetch
```

## Contents

One JSON file per configured source, named to match the output file in `data/sources/`:

| File | Upstream source |
|---|---|
| `vilaboim_movie-quotes.json` | [vilaboim/movie-quotes](https://github.com/vilaboim/movie-quotes) |
| `NikhilNamal17_popular-movie-quotes.json` | [NikhilNamal17/popular-movie-quotes](https://github.com/NikhilNamal17/popular-movie-quotes) |

## Notes

- These files are gitignored — they are local working copies and are not committed.
- Refresh the cache by running the seed script without `--no-fetch`.
- The cache is not used in CI; CI always fetches from upstream.
