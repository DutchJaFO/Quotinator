using Quotinator.Constants.Routes;

namespace Quotinator.Api.Startup;

/// <summary>
/// What an operator is told when the database's recorded schema version is ahead of this build
/// (#350): what happened, why it is a fault rather than stale bookkeeping, and the two ways out.
/// <para>
/// Lives in the Api layer alongside <see cref="BackupObstacleGuidance"/> and for the same reason:
/// <c>Quotinator.Data</c> is domain-agnostic per ADR 004 and has no business deciding an operator's
/// wording. It states symptom, cause and remedy, which is the shape #333's Knowledgebase sweep
/// derives an entry from, and allocates no <c>QTN-</c> code, since #333 owns code allocation for
/// every message that shipped before it.
/// </para>
/// <para>
/// The text claims nothing about the schema being complete. An overshoot means the migrations this
/// build never saw may have added, altered or removed anything, so its shape is unknown: that is
/// also why the only remedies are a Reset or a restore, neither of which asks the operator to trust
/// the current contents.
/// </para>
/// </summary>
internal static class SchemaOvershootGuidance
{
    /// <summary>The degraded-startup reason for a recorded schema version ahead of this build.</summary>
    /// <param name="dataSchemaVersion">The recorded version of Quotinator.Data's own migrations.</param>
    /// <param name="appSchemaVersion">The recorded version of this application's own migrations.</param>
    internal static string Reason(int dataSchemaVersion, int appSchemaVersion) =>
        $"The database's recorded schema version is ahead of this build (data v{dataSchemaVersion}, "
        + $"app v{appSchemaVersion}). This build does not know what the migrations it never saw "
        + "changed, so the schema's shape is unknown and quote content is not served from it. Resolve "
        + $"it with a database Reset (POST {ApiRoutes.DatabaseReset}), which rebuilds the schema at "
        + "this build's own version and discards what the database holds now, or restore a backup "
        + "taken before the version moved ahead and restart.";
}
