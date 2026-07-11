namespace Quotinator.Data.Entities;

/// <summary>Classifies how an import batch was introduced into the database.</summary>
public enum ImportBatchType
{
    /// <summary>A dataset bundled with the application, whether sourced externally with a URL reference (e.g. vilaboim, NikhilNamal17) or authored internally with no URL (e.g. quotinator-curated.json). Bundled content is always replaceable — it is re-seeded from its source file on every reseed/reset regardless of provenance.</summary>
    Seed,

    /// <summary>Records submitted via the bulk import endpoint by a user.</summary>
    Import,

    /// <summary>Records scanned from a file placed in the user's imports folder at startup, regardless of whether that file declares a URL.</summary>
    UserSeed,

    /// <summary>
    /// Reserved for import batches that populate or update a <c>System_</c>-prefixed infrastructure
    /// table (see <c>Sql.Schema.GetUserTables</c>'s naming convention) — not for quote content
    /// provenance. No current source produces this value; nothing today seeds a System_ table via
    /// the import batch mechanism. Kept available for when that need arises.
    /// </summary>
    System
}
