namespace Quotinator.Data.Repositories;

/// <summary>SQL factory methods for <see cref="SqliteRepository{T}"/> and <see cref="SqliteRestorableRepository{T}"/>.</summary>
/// <remarks>
/// Table names come from the <c>[Table]</c> attribute on the entity type — developer-controlled
/// metadata, not user input. Interpolating them into SQL is safe; SQLite does not support
/// parameterised identifiers, so string interpolation is the only viable mechanism.
/// </remarks>
internal static class RepositorySql
{
    /// <summary>Selects an active record by primary key.</summary>
    internal static string SelectById(string tableName)
        => $"SELECT * FROM {tableName} WHERE Id = @id AND IsDeleted = 0";

    /// <summary>Soft-deletes a record by primary key.</summary>
    internal static string SoftDelete(string tableName)
        => $"UPDATE {tableName} SET IsDeleted = 1, DateDeleted = @now, DateModified = @now WHERE Id = @id AND IsDeleted = 0;";

    /// <summary>Selects all soft-deleted records in the table.</summary>
    internal static string SelectDeleted(string tableName)
        => $"SELECT * FROM {tableName} WHERE IsDeleted = 1";

    /// <summary>Restores a soft-deleted record by primary key.</summary>
    internal static string Restore(string tableName)
        => $"UPDATE {tableName} SET IsDeleted = 0, DateDeleted = NULL, DateModified = @now WHERE Id = @id AND IsDeleted = 1";

    /// <summary>Hard-deletes a soft-deleted record by primary key.</summary>
    internal static string HardDelete(string tableName)
        => $"DELETE FROM {tableName} WHERE Id = @id AND IsDeleted = 1";

    /// <summary>Purges all soft-deleted records from the table.</summary>
    internal static string Purge(string tableName)
        => $"DELETE FROM {tableName} WHERE IsDeleted = 1";

    /// <summary>
    /// Selects the active detail record whose <paramref name="fkColumn"/> matches the given parent ID.
    /// Used by <see cref="SqliteOneToOneRepository{TParent,TDetail}"/> for separate-FK layouts.
    /// </summary>
    internal static string SelectByForeignKey(string tableName, string fkColumn)
        => $"SELECT * FROM [{tableName}] WHERE [{fkColumn}] = @parentId AND [IsDeleted] = 0";
}
