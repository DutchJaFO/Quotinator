namespace Quotinator.Data.Notifications;

/// <summary>
/// Message keys for notifications written by producers inside <c>Quotinator.Data</c> and its consumers
/// below the Api layer (#304). Each names a title or body in <c>i18ntext/UI.*.json</c>, resolved through
/// <see cref="INotificationTextSource"/> at write time.
/// <para>
/// Separate from <c>Quotinator.Constants.Api.ApiMessages</c>, which holds the keys for producers that
/// run inside <c>Quotinator.Api</c> (#279's, #289's, #81's). The split is by which project writes the
/// notification, not by what the text says: <c>Quotinator.Core</c> — where the seeding producer lives —
/// does not reference <c>Quotinator.Constants</c>, and adding that edge to reach four string constants
/// would change the documented project graph for no benefit. Keys belong with the machinery that emits
/// them, which for notifications is this project (ADR 018).
/// </para>
/// <para>
/// The strings themselves still live in the same three <c>UI.*.json</c> files as every other
/// user-facing string, and <c>TranslationCompletenessTests</c> covers them exactly as it covers the
/// rest — this class holds keys, never text.
/// </para>
/// </summary>
public static class NotificationMessageKeys
{
    /// <summary>Title for a reseed recommended because source content changed upstream.</summary>
    public const string ReseedContentChangedTitle = "NotificationReseedContentChangedTitle";

    /// <summary>
    /// Body for a reseed recommended because source content changed upstream. Takes the changed file
    /// list as <c>{0}</c>.
    /// </summary>
    public const string ReseedContentChangedBody = "NotificationReseedContentChangedBody";

    /// <summary>Title for a reseed recommended because a Reset left the database with no content.</summary>
    public const string ReseedAfterResetTitle = "NotificationReseedAfterResetTitle";

    /// <summary>Body for a reseed recommended because a Reset left the database with no content.</summary>
    public const string ReseedAfterResetBody = "NotificationReseedAfterResetBody";
}
