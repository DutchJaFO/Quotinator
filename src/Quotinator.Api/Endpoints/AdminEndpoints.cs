using Quotinator.Constants;
using Quotinator.Core.Data;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/admin</c> endpoints.</summary>
internal static class AdminEndpoints
{
    internal static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin")
                       .WithTags(ApiTags.Admin)
                       .RequireRateLimiting(RateLimitPolicies.Admin);

        group.MapPost("/database/reseed", async (IDatabaseInitializer db) =>
        {
            await db.ReseedAsync();
            return Results.Ok(new
            {
                quotes     = db.QuoteCount,
                sources    = db.SourceCount,
                characters = db.CharacterCount,
                people     = db.PeopleCount
            });
        })
        .WithName("ReseedDatabase")
        .WithSummary("Reseed the database")
        .WithDescription(
            "Clears all data tables and reimports every quote from the bundled `quotes.json`. " +
            "The schema version history is preserved — no migrations are re-applied. " +
            "Returns the row counts after the operation completes. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately.");

        group.MapPost("/database/reset", async (IDatabaseInitializer db) =>
        {
            await db.ResetAsync();
            return Results.Ok(new
            {
                quotes     = db.QuoteCount,
                sources    = db.SourceCount,
                characters = db.CharacterCount,
                people     = db.PeopleCount
            });
        })
        .WithName("ResetDatabase")
        .WithSummary("Reset the database")
        .WithDescription(
            "Clears all data and schema version history, reapplies all migrations from scratch, " +
            "then reimports every quote from the bundled `quotes.json`. " +
            "Equivalent to deleting the database file and restarting. " +
            "Returns the row counts after the operation completes. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately.");
    }
}
