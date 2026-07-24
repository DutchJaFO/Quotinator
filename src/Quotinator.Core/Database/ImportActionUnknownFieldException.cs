namespace Quotinator.Core.Database;

/// <summary>
/// Thrown by <see cref="ImportActionFieldRowMapper.BuildRequest"/> when a bulk-decide row's
/// <c>Field</c> is not a currently-decidable field for its <c>EntityType</c> (#163 spec requirement 5).
/// Reported per-row by the bulk-decide endpoint — never aborts the rest of the file, matching
/// <c>POST /import</c>'s existing "one bad row never aborts the rest" model.
/// </summary>
/// <remarks>Creates the exception with the offending row's <c>EntityType</c> and <c>Field</c>.</remarks>
public sealed class ImportActionUnknownFieldException(string entityType, string field) : Exception($"'{field}' is not a decidable field for entity type '{entityType}'.")
{
    /// <summary>The row's <c>EntityType</c>.</summary>
    public string EntityType { get; } = entityType;

    /// <summary>The row's offending <c>Field</c> value.</summary>
    public string Field { get; } = field;
}
