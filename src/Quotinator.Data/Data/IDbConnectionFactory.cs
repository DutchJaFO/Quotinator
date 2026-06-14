using System.Data;

namespace Quotinator.Data.Data;

/// <summary>Creates and returns database connections.</summary>
public interface IDbConnectionFactory
{
    /// <summary>Creates a new, closed database connection.</summary>
    IDbConnection CreateConnection();
}
