namespace Quotinator.Data.Enums;

/// <summary>
/// How thoroughly the database check runs (#490, ADR 028).
/// </summary>
public enum DatabaseCheckDepth
{
    /// <summary>Every layer. A Reset and the integrity endpoint always check at this depth.</summary>
    Full,

    /// <summary>
    /// Every layer except <see cref="DatabaseCheckLayer.Indexes"/> and <see cref="DatabaseCheckLayer.References"/>,
    /// which are reported <see cref="DatabaseCheckLayerStatus.Skipped"/>. An index that disagrees with its table,
    /// a broken unique constraint or a dangling reference then goes undetected until a check at
    /// <see cref="Full"/> runs.
    /// </summary>
    Fast,
}
