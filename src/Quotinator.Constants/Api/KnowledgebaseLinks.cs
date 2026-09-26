namespace Quotinator.Constants.Api;

/// <summary>
/// Addresses of the Knowledgebase entries the application links to (#348). Held once, here, because the same
/// entry is named by an API reason, a notification and a degraded page, and three copies of a URL drift.
/// <para>
/// An entry is a file under <c>docs/knowledgebase/</c>; a repository test holds every address here to a file
/// that exists. They point at the repository on GitHub because that is where an operator can read them: the
/// application does not serve its own documentation.
/// </para>
/// </summary>
public static class KnowledgebaseLinks
{
    private const string Base = "https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/";

    /// <summary>Why no backup could be taken, which options that blocks, and how to resolve each obstacle.</summary>
    public const string NoBackupCouldBeTaken = Base + "no-backup-could-be-taken.md";
}
