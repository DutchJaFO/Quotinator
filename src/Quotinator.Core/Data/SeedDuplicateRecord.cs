namespace Quotinator.Core.Data;

/// <summary>Records what happened when a duplicate entity was encountered during seeding.</summary>
/// <param name="EntityType">Entity category: <c>"quote"</c>, <c>"source"</c>, <c>"character"</c>, <c>"person"</c>, or <c>"translation"</c>.</param>
/// <param name="Id">Primary key of the duplicate entity.</param>
/// <param name="Label">Human-readable summary (truncated quote text, source title, etc.).</param>
/// <param name="FirstSeenInFile">File name where this entity was first encountered.</param>
/// <param name="ConflictFile">File name of the later occurrence that triggered the deduplication check.</param>
/// <param name="AppliedPolicy">Policy that was applied to resolve the conflict.</param>
public record SeedDuplicateRecord(
    string                    EntityType,
    string                    Id,
    string                    Label,
    string                    FirstSeenInFile,
    string                    ConflictFile,
    DuplicateResolutionPolicy AppliedPolicy);
