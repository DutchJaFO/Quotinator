using System.Data;

namespace Quotinator.Data.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
