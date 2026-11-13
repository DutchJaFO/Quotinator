namespace Quotinator.Data.Notifications;

/// <summary>
/// Message keys for notifications written by producers inside <c>Quotinator.Data</c> and its consumers
/// below the Api layer (#304). Each names a title or body in <c>i18ntext/UI.*.json</c>, resolved through
/// <see cref="INotificationTextSource"/> at write time.
/// <para>
/// Separate from <c>Quotinator.Constants.Api.ApiMessages</c>, which holds the keys for producers that
/// run inside <c>Quotinator.Api</c> (#279's, #289's, #81's). The split is by which project writes the
/// notification, not by what the text says: <c>Quotinator.Core</c> (where the seeding producer lives)
/// does not reference <c>Quotinator.Constants</c>, and adding that edge to reach four string constants
/// would change the documented project graph for no benefit. Keys belong with the machinery that emits
/// them, which for notifications is this project (ADR 018).
/// </para>
/// <para>
/// The strings themselves still live in the same three <c>UI.*.json</c> files as every other
/// user-facing string, and <c>TranslationCompletenessTests</c> covers them exactly as it covers the
/// rest: this class holds keys, never text.
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

    /// <summary>Title for the confirmation that one file reseeded with nothing left to review (#302).</summary>
    public const string ReseedFileAppliedTitle = "NotificationReseedFileAppliedTitle";

    /// <summary>
    /// Body for the confirmation that one <b>bundled</b> file reseeded with nothing left to review.
    /// Takes the file name as <c>{0}</c>, the number of rows added as <c>{1}</c>, and the number
    /// modified as <c>{2}</c>. The per-entity-type breakdown behind those totals lives in the
    /// notification's metadata payload, not in this text.
    /// </summary>
    /// <remarks>
    /// Split by origin rather than taking the origin as a fourth argument: an argument is a single
    /// value applied to every language, so a localised "bundled"/"user" would appear in one language
    /// regardless of which one the reader asked for.
    /// </remarks>
    public const string ReseedFileAppliedBundledBody = "NotificationReseedFileAppliedBundledBody";

    /// <summary>
    /// The parts a reseed confirmation's summary sentence is composed from (#377). Only the outcomes
    /// that actually occurred are included, so the sentence stops listing a run of zeroes, but every
    /// count it can state remains visible per entity type in the notification's own detail table and in
    /// the seed log, which is what makes omitting a clause safe rather than lossy.
    /// </summary>
    public const string ReseedSummaryIncoming = "NotificationReseedSummaryIncoming";

    /// <inheritdoc cref="ReseedSummaryIncoming"/>
    public const string ReseedSummaryAdded = "NotificationReseedSummaryAdded";

    /// <inheritdoc cref="ReseedSummaryIncoming"/>
    public const string ReseedSummaryUpdated = "NotificationReseedSummaryUpdated";

    /// <inheritdoc cref="ReseedSummaryIncoming"/>
    public const string ReseedSummaryUnchanged = "NotificationReseedSummaryUnchanged";

    /// <inheritdoc cref="ReseedSummaryIncoming"/>
    public const string ReseedSummarySkipped = "NotificationReseedSummarySkipped";

    /// <inheritdoc cref="ReseedSummaryIncoming"/>
    public const string ReseedSummaryResolved = "NotificationReseedSummaryResolved";

    /// <summary>
    /// Said instead of an outcome list when items arrived and none of them produced an outcome (#377,
    /// developer 2026-09-09: "if N items came in and nothing happened then say so in text"). A sentence
    /// that simply stopped after the incoming count would read as truncated rather than as a finding.
    /// </summary>
    public const string ReseedSummaryNothingHappened = "NotificationReseedSummaryNothingHappened";

    /// <summary>
    /// Said when a file carried nothing at all. Its own wording rather than "0 items came in" because a
    /// reseed is expected to carry at least one item (developer, 2026-09-09), so this is an anomaly the
    /// reader should notice rather than a count to skim past.
    /// </summary>
    public const string ReseedSummaryNothingArrived = "NotificationReseedSummaryNothingArrived";

    /// <summary>Separator between all but the last two parts of the composed summary (#377).</summary>
    public const string ReseedSummarySeparator = "NotificationReseedSummarySeparator";

    /// <summary>Join before the composed summary's final part: language-specific, hence a key (#377).</summary>
    public const string ReseedSummaryFinalJoin = "NotificationReseedSummaryFinalJoin";

    /// <summary>
    /// Body for the confirmation that one file from the <b>user imports</b> folder reseeded with
    /// nothing left to review. Same arguments as <see cref="ReseedFileAppliedBundledBody"/>.
    /// </summary>
    public const string ReseedFileAppliedUserBody = "NotificationReseedFileAppliedUserBody";

    /// <summary>Title for the alert that one reseeded file left import actions awaiting review (#303).</summary>
    public const string ImportReviewPendingTitle = "NotificationImportReviewPendingTitle";

    /// <summary>
    /// Body for the alert that a <b>bundled</b> file left import actions awaiting review. Takes the
    /// file name as <c>{0}</c> and how many actions are waiting as <c>{1}</c>.
    /// </summary>
    /// <remarks>
    /// The total rather than a per-status list, for the same reason
    /// <see cref="ReseedFileAppliedBundledBody"/> carries totals: the number of reviewable states with
    /// rows varies per file, and a variable-length list cannot be assembled from arguments without
    /// composing localised words outside the translation files. The per-status breakdown lives in the
    /// notification's metadata payload, where the review page reads it.
    /// </remarks>
    public const string ImportReviewPendingBundledBody = "NotificationImportReviewPendingBundledBody";

    /// <summary>
    /// Body for the alert that a file from the <b>user imports</b> folder left import actions awaiting
    /// review. Same arguments as <see cref="ImportReviewPendingBundledBody"/>.
    /// </summary>
    public const string ImportReviewPendingUserBody = "NotificationImportReviewPendingUserBody";

    /// <summary>Title for the notice that startup did not load content because no backup could be taken (#348).</summary>
    public const string BackupRefusedContentLoadTitle = "NotificationBackupRefusedContentLoadTitle";

    /// <summary>
    /// Body for the same notice. Receives the obstacle's name as <c>{0}</c>: the name, not its
    /// explanation, because the explanation is written once in the Knowledgebase entry the notification
    /// links to, and a second copy here would drift from it.
    /// </summary>
    public const string BackupRefusedContentLoadBody = "NotificationBackupRefusedContentLoadBody";

    /// <summary>Title for the notice that a Reset set aside a database file it could not read (#482).</summary>
    public const string UnreadableDatabaseSetAsideTitle = "NotificationUnreadableDatabaseSetAsideTitle";

    /// <summary>
    /// Body for the same notice. Receives the set-aside file's name as <c>{0}</c> and the reserved folder's
    /// name as <c>{1}</c>. Static text per ADR 026: every sentence records what happened, so it stays
    /// true after the file is removed.
    /// </summary>
    public const string UnreadableDatabaseSetAsideBody = "NotificationUnreadableDatabaseSetAsideBody";

    // #348: the result of a Reset or a reseed run from within the application. Static text per ADR 026:
    // each records what the user was told when the action ended, so it stays true afterwards. A title per
    // action and ending; the bodies an action shares with the other are written once.

    /// <summary>Title: a Reset completed and took its backup first.</summary>
    public const string DatabaseResetCompletedTitle = "NotificationDatabaseResetCompletedTitle";

    /// <summary>Title: a Reset completed without a backup, by the user's permission.</summary>
    public const string DatabaseResetWithoutBackupTitle = "NotificationDatabaseResetWithoutBackupTitle";

    /// <summary>Title: a Reset completed and set aside a database file it could not read (#482).</summary>
    public const string DatabaseResetSetAsideTitle = "NotificationDatabaseResetSetAsideTitle";

    /// <summary>Title: a Reset was refused and changed nothing.</summary>
    public const string DatabaseResetRefusedTitle = "NotificationDatabaseResetRefusedTitle";

    /// <summary>Title: a Reset ran, but the rebuilt schema does not match (#305).</summary>
    public const string DatabaseResetNotIntactTitle = "NotificationDatabaseResetNotIntactTitle";

    /// <summary>Title: a reseed completed and took its backup first.</summary>
    public const string DatabaseReseedCompletedTitle = "NotificationDatabaseReseedCompletedTitle";

    /// <summary>Title: a reseed completed without a backup, by the user's permission.</summary>
    public const string DatabaseReseedWithoutBackupTitle = "NotificationDatabaseReseedWithoutBackupTitle";

    /// <summary>Title: a reseed was refused and changed nothing.</summary>
    public const string DatabaseReseedRefusedTitle = "NotificationDatabaseReseedRefusedTitle";

    /// <summary>Body: the backup the action took first. Receives the backup's file name as <c>{0}</c>.</summary>
    public const string DatabaseActionBackupTakenBody = "NotificationDatabaseActionBackupTakenBody";

    /// <summary>Body: a Reset ran without a backup. Receives the obstacle's name as <c>{0}</c>.</summary>
    public const string DatabaseResetWithoutBackupBody = "NotificationDatabaseResetWithoutBackupBody";

    /// <summary>Body: a reseed ran without a backup. Receives the obstacle's name as <c>{0}</c>.</summary>
    public const string DatabaseReseedWithoutBackupBody = "NotificationDatabaseReseedWithoutBackupBody";

    /// <summary>Body: a Reset set aside an unreadable file. Receives the file's name as <c>{0}</c>.</summary>
    public const string DatabaseResetSetAsideBody = "NotificationDatabaseResetSetAsideBody";

    /// <summary>Body: the action was refused because no backup could be taken. Receives the obstacle's name as <c>{0}</c>.</summary>
    public const string DatabaseActionRefusedForBackupBody = "NotificationDatabaseActionRefusedForBackupBody";

    /// <summary>Body: a Reset was refused because the database could not be replaced (#482). Receives the obstacle's name as <c>{0}</c>.</summary>
    public const string DatabaseResetRefusedForResetBody = "NotificationDatabaseResetRefusedForResetBody";

    /// <summary>Body: a Reset ran, but the rebuilt schema does not match (#305).</summary>
    public const string DatabaseResetNotIntactBody = "NotificationDatabaseResetNotIntactBody";

    /// <summary>Title for the warning that the backups folder has reached its operating quota (#348).</summary>
    public const string BackupQuotaReachedTitle = "NotificationBackupQuotaReachedTitle";

    /// <summary>
    /// Body for the same warning. Receives the bytes in use, the quota and the ceiling as <c>{0}</c>,
    /// <c>{1}</c> and <c>{2}</c>, each already formatted as a size for reading.
    /// </summary>
    public const string BackupQuotaReachedBody = "NotificationBackupQuotaReachedBody";

    /// <summary>Title for the error that the backups folder is at or above its maximum (#348).</summary>
    public const string BackupMaxExceededTitle = "NotificationBackupMaxExceededTitle";

    /// <summary>
    /// Body for the error that the backups folder is at or above its maximum (#348). Takes the bytes in
    /// use as <c>{0}</c> and that maximum as <c>{1}</c>, each already formatted as a size for reading.
    /// </summary>
    public const string BackupMaxExceededBody = "NotificationBackupMaxExceededBody";
}
