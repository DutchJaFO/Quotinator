using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;
using Quotinator.Api.Logging;

namespace Quotinator.Api.Endpoints;

/// <summary>
/// Registers <c>/api/v1/import/rules/conflict</c> — view, generate, and remove a #153 override for a
/// bundled/user-imported source's <c>ConflictResolutionRule</c> file. The generated override lives
/// under the same persistent download-cache directories <see cref="ISourceCacheUpdater"/> already uses
/// (never the read-only bundled/image path), registered in <see cref="ISourceFileOverrideRegistry"/>
/// so the seeding pipeline can trust it — see <see cref="EffectiveRuleFileResolver"/>.
/// </summary>
internal static class ImportRuleEndpoints
{
    private sealed class Log { }

    internal static void MapImportRuleEndpoints(this WebApplication app)
    {
        var publicGroup = app.MapGroup("/api/v1/import/rules")
                             .WithTags(ApiTags.Import)
                             .RequireRateLimiting(RateLimitPolicies.Admin);

        var adminGroup = app.MapGroup("/api/v1/import/rules")
                            .WithTags(ApiTags.Import)
                            .RequireRateLimiting(RateLimitPolicies.Admin)
                            .AddEndpointFilter<AdminApiKeyFilter>()
                            .WithMetadata(AdminApiKeyRequiredMarker.Instance);

        publicGroup.MapGet("/conflict", async (
                string? fileName,
                string? origin,
                IRuleFileOverridePathResolver pathResolver,
                ISourceFileOverrideRegistry registry,
                IApiLocalizer localizer,
                ILogger<Log> logger) =>
            {
                if (!TryValidate(fileName, origin, localizer, out var parsedOrigin, out var validationError))
                    return validationError!;

                var content = await EffectiveRuleFileResolver.ReadEffectiveContentAsync(
                    fileName!, parsedOrigin, pathResolver, registry, logger, logPrefix: "[Api - Import]");
                if (content is null)
                    return Results.Problem(detail: localizer[ApiMessages.RuleFileNotFound], statusCode: StatusCodes.Status404NotFound);

                var isOverrideActive = await registry.FindAsync(fileName!, parsedOrigin) is not null;
                var ruleFile = ParseConflictRuleFile(content);

                return Results.Ok(new ConflictRuleFileResponse
                {
                    FileName         = fileName!,
                    Origin           = origin!,
                    IsOverrideActive = isOverrideActive,
                    Rules            = ruleFile.Rules,
                });
            })
            .Produces<ConflictRuleFileResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .WithName("GetConflictRuleFile")
            .WithSummary("View the currently effective conflict-resolution rule file")
            .WithDescription(
                "Returns the `ConflictResolutionRule`s (#181/#153) currently in effect for `fileName`/`origin` — a " +
                "registered, hash-verified override when one exists (`isOverrideActive: true`), otherwise the bundled/image " +
                "copy. Returns `404` if neither exists. No `X-Api-Key` required, matching `GET /import/actions`'s precedent.");

        adminGroup.MapPost("/conflict/generate", async (
                string? fileName,
                string? origin,
                string? batchId,
                IImportActionService actionService,
                IRuleFileOverridePathResolver pathResolver,
                ISourceFileOverrideRegistry registry,
                IApiLocalizer localizer,
                ILogger<Log> logger) =>
            {
                if (!TryValidate(fileName, origin, localizer, out var parsedOrigin, out var validationError))
                    return validationError!;

                if (string.IsNullOrWhiteSpace(batchId))
                    return Results.Problem(detail: localizer[ApiMessages.ImportActionBatchIdRequired], statusCode: StatusCodes.Status422UnprocessableEntity);

                var rows      = await actionService.ExportBatchAsync(batchId);
                var generated = ConflictRuleGenerator.Generate(rows);

                var existingContent = await EffectiveRuleFileResolver.ReadEffectiveContentAsync(
                    fileName!, parsedOrigin, pathResolver, registry, logger, logPrefix: "[Api - Import]");
                var existingFile = existingContent is null ? null : ParseConflictRuleFile(existingContent);

                var merged     = ConflictRuleGenerator.Merge(existingFile, generated);
                var rulesAdded = merged.Rules.Count - (existingFile?.Rules.Count ?? 0);

                var json         = System.Text.Json.JsonSerializer.Serialize(merged, RuleFileWriteOptions);
                var overridePath = pathResolver.Resolve(fileName!, parsedOrigin);
                Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
                await File.WriteAllTextAsync(overridePath, json);

                await registry.RegisterAsync(fileName!, parsedOrigin, EffectiveRuleFileResolver.ComputeContentHash(json), batchId);

                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogImportRuleOverrideGenerated(
                        LogSanitizer.ForLog(fileName!), LogSanitizer.ForLog(origin!), LogSanitizer.ForLog(batchId!), rulesAdded);

                return Results.Ok(new ConflictRuleFileResponse
                {
                    FileName         = fileName!,
                    Origin           = origin!,
                    IsOverrideActive = true,
                    Rules            = merged.Rules,
                    RulesAdded       = rulesAdded,
                });
            })
            .Produces<ConflictRuleFileResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .WithName("GenerateConflictRuleFile")
            .WithSummary("Generate a conflict-resolution override from a decided batch")
            .WithDescription(
                "Builds `ConflictResolutionRule`s from every decided field in `batchId` (via `GET /import/actions/export`'s " +
                "same row shape), merges them into the currently effective rule file for `fileName`/`origin` — never " +
                "overwriting an already-covered entity/field — writes the result to a persistent override location, and " +
                "registers its content hash so the seeding pipeline trusts it on the next reseed (see " +
                "`GET /import/actions/apply` for actually applying `batchId` itself; generating an override does not " +
                "require the batch to be applied). Returns the merged rule file and `rulesAdded`. " +
                "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        adminGroup.MapDelete("/conflict", async (
                string? fileName,
                string? origin,
                ISourceFileOverrideRegistry registry,
                IApiLocalizer localizer) =>
            {
                if (!TryValidate(fileName, origin, localizer, out var parsedOrigin, out var validationError))
                    return validationError!;

                var removed = await registry.RemoveAsync(fileName!, parsedOrigin);
                return removed
                    ? Results.NoContent()
                    : Results.Problem(detail: localizer[ApiMessages.RuleFileOverrideNotFound], statusCode: StatusCodes.Status404NotFound);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .WithName("RemoveConflictRuleFileOverride")
            .WithSummary("Remove a registered conflict-resolution override")
            .WithDescription(
                "Un-registers `fileName`/`origin`'s override — the seeding pipeline falls back to the bundled/image copy " +
                "on the next reseed. The override file itself is left on disk (harmless — it is never trusted without a " +
                "matching registration) so it can still be inspected or re-registered later. Returns `404` if no override " +
                "is currently registered for `fileName`/`origin`. " +
                "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        publicGroup.MapGet("/alias", async (
                string? fileName,
                string? origin,
                IListableRepository<SourceEntity> sourceRepository,
                IRuleFileOverridePathResolver pathResolver,
                ISourceFileOverrideRegistry registry,
                IApiLocalizer localizer,
                ILogger<Log> logger) =>
            {
                if (!TryValidate(fileName, origin, localizer, out var parsedOrigin, out var validationError))
                    return validationError!;

                var content = await EffectiveRuleFileResolver.ReadEffectiveContentAsync(
                    fileName!, parsedOrigin, pathResolver, registry, logger, logPrefix: "[Api - Import]");
                var existingAliases = content is null
                    ? SourceAliasLookup.Empty
                    : new SourceAliasLookup(ParseSourceAliasFile(content).Aliases);

                var allSources = await sourceRepository.GetPageAsync(page: 1, pageSize: 0);
                var tuples = allSources.Items.Select(s => (s.Id.ToCanonicalId(), s.Title, s.Type.Raw));

                var candidates = SourceAliasCandidateGenerator.Generate(tuples, existingAliases);

                return Results.Ok(new SourceAliasCandidateResponse
                {
                    FileName   = fileName!,
                    Origin     = origin!,
                    Candidates = candidates,
                });
            })
            .Produces<SourceAliasCandidateResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .WithName("GetSourceAliasCandidates")
            .WithSummary("Suggest likely duplicate Source titles not yet covered by an alias")
            .WithDescription(
                "Scans every Source in the database for near-duplicate `(Title, Type)` pairs (punctuation/whitespace " +
                "differences — e.g. a trailing `!`, a curly vs. straight apostrophe — not plain casing, which #175's " +
                "natural-key matching already prevents from ever coexisting) not already covered by `fileName`/`origin`'s " +
                "own `SourceAliasRule` file. Detect-and-suggest only — never writes an alias entry; confirming a genuine " +
                "duplicate requires research per `docs/workflow/source-verification.md` before hand-editing the alias " +
                "file. No `X-Api-Key` required, matching `GET /import/actions`'s precedent for a read-only endpoint.");
    }

    private static readonly System.Text.Json.JsonSerializerOptions RuleFileWriteOptions = new() { WriteIndented = true };
    private static readonly System.Text.Json.JsonSerializerOptions RuleFileReadOptions  = new() { PropertyNameCaseInsensitive = true };

    private static ConflictResolutionRuleFileDto ParseConflictRuleFile(string json)
        => System.Text.Json.JsonSerializer.Deserialize<ConflictResolutionRuleFileDto>(json, RuleFileReadOptions) ?? new ConflictResolutionRuleFileDto();

    private static SourceAliasRuleFileDto ParseSourceAliasFile(string json)
        => System.Text.Json.JsonSerializer.Deserialize<SourceAliasRuleFileDto>(json, RuleFileReadOptions) ?? new SourceAliasRuleFileDto();

    private static bool TryValidate(
        string? fileName, string? origin, IApiLocalizer localizer,
        out SeedBatchOrigin parsedOrigin, out IResult? error)
    {
        parsedOrigin = default;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = Results.Problem(detail: localizer[ApiMessages.RuleFileNameRequired], statusCode: StatusCodes.Status422UnprocessableEntity);
            return false;
        }

        if (!Enum.TryParse(origin, ignoreCase: true, out parsedOrigin) || !Enum.IsDefined(parsedOrigin))
        {
            error = Results.Problem(detail: localizer[ApiMessages.RuleFileOriginInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
            return false;
        }

        error = null;
        return true;
    }
}
