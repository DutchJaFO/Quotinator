using System.Data;

namespace Quotinator.Core.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
