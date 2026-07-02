namespace Quotinator.Engine.Entities;

/// <summary>Classifies how an import batch was introduced into the database.</summary>
public enum ImportBatchType
{
    /// <summary>An external dataset bundled with a URL reference (e.g. vilaboim, NikhilNamal17).</summary>
    Seed,

    /// <summary>Records submitted via the bulk import endpoint by a user.</summary>
    Import,

    /// <summary>Fixed/predetermined data bundled with the application (e.g. curated quotes), conceptually distinct from replaceable seed content. Reseed/reset does not yet selectively preserve this category — see the follow-up issue tracking that behavior.</summary>
    System,

    /// <summary>Records scanned from a file placed in the user's imports folder at startup, regardless of whether that file declares a URL.</summary>
    UserSeed
}
