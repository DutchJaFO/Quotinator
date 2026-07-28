namespace Quotinator.Data.Models;

/// <summary>The mechanism that initiated a <see cref="Quotinator.Data.Entities.SystemChangeLog"/> row — the specific identifying detail (which batch, which route, which provider) lives in <see cref="Quotinator.Data.Repositories.IInitiatorContext.InitiatedById"/>, not here.</summary>
public enum InitiatorType
{
    /// <summary>Startup seeding from bundled or user-supplied source files.</summary>
    Seed,
    /// <summary>The live <c>POST /api/v1/import</c> endpoint.</summary>
    Import,
    /// <summary>A write endpoint (create/update/delete a single record).</summary>
    WriteEndpoint,
    /// <summary>An enrichment provider filling a missing field.</summary>
    Enrichment
}
