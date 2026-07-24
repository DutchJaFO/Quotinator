namespace Quotinator.Core.Database;

/// <summary>
/// Thrown by <see cref="ImportActionFieldRowMapper.BuildRequest"/> when a bulk-decide row's
/// <c>EntityType</c> is not one of <see cref="Quotinator.Core.Helpers.ImportActionEntityTypes.All"/>
/// (#163 spec requirement 5). Reported per-row by the bulk-decide endpoint — never aborts the rest of
/// the file, matching <c>POST /import</c>'s existing "one bad row never aborts the rest" model.
/// </summary>
/// <remarks>Creates the exception with the offending row's <c>EntityType</c>.</remarks>
public sealed class ImportActionUnknownEntityTypeException(string entityType) : Exception($"'{entityType}' is not a recognised import action entity type.")
{
    /// <summary>The row's offending <c>EntityType</c> value.</summary>
    public string EntityType { get; } = entityType;
}
