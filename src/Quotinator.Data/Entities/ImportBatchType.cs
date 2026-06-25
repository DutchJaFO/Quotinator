namespace Quotinator.Data.Entities;

/// <summary>Classifies how an import batch was introduced into the database.</summary>
public enum ImportBatchType
{
    /// <summary>An external dataset bundled with a URL reference (e.g. vilaboim, NikhilNamal17).</summary>
    Seed,

    /// <summary>Records submitted via the bulk import endpoint by a user.</summary>
    Import,

    /// <summary>Records seeded from bundled source files at startup.</summary>
    System
}
