using Quotinator.Data.Enums;

namespace Quotinator.Data.Database;

/// <summary>
/// Signals that a Reset was abandoned before anything was moved or written, because the database could
/// not be replaced (#482): an unreadable file could not be set aside, or the data directory cannot be
/// written, which also stops a readable database's Reset run with the override (#348 step 29).
/// <para>
/// A transport, for the same reason as <see cref="DatabaseBackupUnavailableException"/>: the refusal is
/// found inside <c>OnResetAsync</c>, whose signature belongs to subclasses and has no result to return,
/// and <see cref="DatabaseInitializer.ResetAsync"/> converts it into a <see cref="DatabaseOperationResult"/>.
/// </para>
/// </summary>
/// <param name="obstacle">What stopped the Reset.</param>
/// <param name="innerException">The underlying failure, where one was thrown.</param>
public sealed class DatabaseResetRefusedException(ResetObstacle obstacle, Exception? innerException = null)
    : Exception($"The database could not be replaced ({obstacle}), so the Reset was abandoned.", innerException)
{
    /// <summary>What stopped the Reset.</summary>
    public ResetObstacle Obstacle { get; } = obstacle;
}
