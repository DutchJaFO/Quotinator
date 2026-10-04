using Quotinator.Data.Import;

namespace Quotinator.Core.Database;

/// <summary>
/// The outcome of <see cref="ConflictRuleGenerator.Merge"/> — either the merged rule file, or the entity
/// id an existing rule file names more than once. A rule file may name an entity at most once (ADR 023),
/// and a file that breaks it is reported here rather than thrown: the condition is checkable before the
/// per-entity dictionary is built, which is exactly what
/// <see href="https://github.com/DutchJaFO/Quotinator/blob/main/docs/architecture-decisions/022-exceptions-only-for-undetectable-conditions.md">ADR 022</see>
/// reserves an exception for not doing.
/// </summary>
public sealed class ConflictRuleMergeResult
{
    /// <summary>The merged rule file, or <see langword="null"/> when <see cref="DuplicateEntityId"/> is set.</summary>
    public ConflictResolutionRuleFileDto? File { get; init; }

    /// <summary>
    /// The entity id the existing file names more than once, or <see langword="null"/> when the merge
    /// succeeded. Carried so the caller can name the offending id in its own response rather than
    /// reporting only that something was wrong.
    /// </summary>
    public string? DuplicateEntityId { get; init; }

    /// <summary>Whether the merge produced a file. <see cref="File"/> is non-null exactly when this is <see langword="true"/>.</summary>
    public bool IsMerged => DuplicateEntityId is null;

    /// <summary>The successful outcome, carrying the merged file.</summary>
    /// <param name="file">The merged rule file.</param>
    public static ConflictRuleMergeResult Merged(ConflictResolutionRuleFileDto file) => new() { File = file };

    /// <summary>The refused outcome: the existing file names <paramref name="entityId"/> more than once.</summary>
    /// <param name="entityId">The repeated entity id.</param>
    public static ConflictRuleMergeResult DuplicateEntity(string entityId) => new() { DuplicateEntityId = entityId };
}
