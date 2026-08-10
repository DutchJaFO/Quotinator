using Quotinator.Data.Queries;

namespace Quotinator.Core.Queries;

/// <summary>Join strategy for a single translation-resolved Quote — see <see cref="Sql.Quotes.SelectById"/>.</summary>
public sealed class QuoteLineStrategy : IJoinStrategy<QuoteRow>
{
    /// <inheritdoc/>
    public string BuildSql() => Sql.Quotes.SelectById();
}
