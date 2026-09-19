using Quotinator.Data.Enums;

namespace Quotinator.Data.Import;

/// <summary>
/// What staging a decision on one import action produced. Returned in place of throwing for the
/// conditions the decide path has already checked (ADR 022): a missing action, one already resolved, one
/// whose kind cannot be decided, one held for review until its file or a rule changes, and ambiguous
/// fields left without a decision.
/// </summary>
/// <param name="ActionId">The action the decision was staged on.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="UnresolvedFields">The ambiguous fields left without a decision; empty unless <see cref="Outcome"/> is <see cref="ImportActionDecideOutcome.UnresolvedFields"/>.</param>
/// <param name="EntityType">The action's entity type, when <see cref="Outcome"/> is <see cref="ImportActionDecideOutcome.NotDecidable"/>.</param>
/// <param name="CurrentStatus">The action's status, when <see cref="Outcome"/> is <see cref="ImportActionDecideOutcome.AlreadyResolved"/> or <see cref="ImportActionDecideOutcome.HeldForReview"/>.</param>
/// <param name="ActionType">The action's kind, when <see cref="Outcome"/> is <see cref="ImportActionDecideOutcome.NotDecidable"/>.</param>
public sealed record ImportActionDecideResult(
    Guid ActionId,
    ImportActionDecideOutcome Outcome,
    IReadOnlyList<string> UnresolvedFields,
    string? EntityType = null,
    string? CurrentStatus = null,
    string? ActionType = null)
{
    /// <summary>The decision was stored.</summary>
    /// <param name="actionId">The action decided.</param>
    public static ImportActionDecideResult Decided(Guid actionId) => new(actionId, ImportActionDecideOutcome.Decided, []);

    /// <summary>No action exists with that id.</summary>
    /// <param name="actionId">The id looked up.</param>
    public static ImportActionDecideResult NotFound(Guid actionId) => new(actionId, ImportActionDecideOutcome.NotFound, []);

    /// <summary>The action is already applied or discarded.</summary>
    /// <param name="actionId">The action.</param>
    /// <param name="currentStatus">Its status.</param>
    public static ImportActionDecideResult AlreadyResolved(Guid actionId, string currentStatus) =>
        new(actionId, ImportActionDecideOutcome.AlreadyResolved, [], CurrentStatus: currentStatus);

    /// <summary>The action's kind does not accept a manual decision.</summary>
    /// <param name="actionId">The action.</param>
    /// <param name="entityType">Its entity type.</param>
    /// <param name="actionType">Its kind — <c>Add</c>, <c>Modify</c> and so on.</param>
    public static ImportActionDecideResult NotDecidable(Guid actionId, string entityType, string actionType) =>
        new(actionId, ImportActionDecideOutcome.NotDecidable, [], EntityType: entityType, ActionType: actionType);

    /// <summary>The action is held because of its incoming content, and is resolved by correcting the file or adding a rule.</summary>
    /// <param name="actionId">The action.</param>
    /// <param name="currentStatus">Its status — <c>Pending</c>, <c>Stale</c> or <c>Blocked</c>.</param>
    public static ImportActionDecideResult HeldForReview(Guid actionId, string currentStatus) =>
        new(actionId, ImportActionDecideOutcome.HeldForReview, [], CurrentStatus: currentStatus);

    /// <summary>Ambiguous fields were left without a decision.</summary>
    /// <param name="actionId">The action.</param>
    /// <param name="unresolvedFields">The fields left undecided.</param>
    public static ImportActionDecideResult Unresolved(Guid actionId, IReadOnlyList<string> unresolvedFields) =>
        new(actionId, ImportActionDecideOutcome.UnresolvedFields, unresolvedFields);

    /// <summary>
    /// The plain-English text a bulk-decide row error carries for an outcome other than
    /// <see cref="ImportActionDecideOutcome.Decided"/>.
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe() => Outcome switch
    {
        ImportActionDecideOutcome.NotFound         => $"Import action '{ActionId}' does not exist.",
        ImportActionDecideOutcome.AlreadyResolved  => $"Import action '{ActionId}' is not in a valid state for this operation (current status: '{CurrentStatus}').",
        ImportActionDecideOutcome.NotDecidable     => $"Import action '{ActionId}' is a '{EntityType}' {ActionType} action and cannot be manually decided.",
        ImportActionDecideOutcome.UnresolvedFields => $"The following fields are ambiguous and need an explicit decision: {string.Join(", ", UnresolvedFields)}",
        ImportActionDecideOutcome.HeldForReview    => $"Import action '{ActionId}' is held for review (current status: '{CurrentStatus}') and cannot be decided: correct the imported file, or add a rule that resolves it.",
        _                                          => string.Empty,
    };
}
