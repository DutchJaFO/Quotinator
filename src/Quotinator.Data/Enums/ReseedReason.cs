namespace Quotinator.Data.Enums;

/// <summary>
/// Why a reseed is being recommended (#304). Carried on the notification's metadata payload, where it
/// is also what identifies the notification — the same reason recurring is the same recommendation,
/// while a different reason is a different one.
/// <para>
/// One kind with a reason rather than a member per producer: both cases recommend the same action and
/// are executed by the same code path, so splitting them would mean two registry entries and two
/// payload types to express one difference that is already a value.
/// </para>
/// </summary>
public enum ReseedReason
{
    /// <summary>
    /// A source file's content changed upstream on a database that was not empty, so the stored content
    /// no longer reflects what the sources now say. The payload's changed-file list is populated.
    /// </summary>
    ContentChanged,

    /// <summary>
    /// A successful <c>POST /admin/database/reset</c> left the database with no quote content — Reset
    /// rebuilds the schema and deliberately does not reimport (#156, and CLAUDE.md's endpoint
    /// side-effect policy). The payload's changed-file list is empty: nothing changed upstream, the
    /// content is simply gone.
    /// </summary>
    AfterReset
}
