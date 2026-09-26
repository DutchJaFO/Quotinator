namespace Quotinator.Api.Startup;

/// <summary>
/// The English text of #279's operation-id-rename announcement, held here so the producer in
/// <c>Program.cs</c> and the tests that hold Data-owned migration 24 to it read one copy.
/// <para>
/// Its content hash is part of the announcement's identity, so a change to this text announces it again
/// on every installation unless a migration rewrites the stored row to match, as migration 24 did when
/// the text was reworded (#348).
/// </para>
/// </summary>
internal static class OperationIdRenameAnnouncement
{
    /// <summary>The announcement's body, in its original language.</summary>
    internal const string Body =
        "Two REST API operation IDs were renamed for naming consistency (issue #279): " +
        "GetImportBatches → GetAllImportBatches, and GetFileResources → GetAllFileResources. " +
        "This only affects a generated API client keyed by operation ID; routes and behaviour are unchanged.";
}
