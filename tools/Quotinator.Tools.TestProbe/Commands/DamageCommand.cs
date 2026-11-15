using System.Globalization;
using Quotinator.Data.Testing.Database;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary>
/// <c>damage</c>: damages a database file the way the database check's unit tests do (#490), through
/// <c>DocumentedFormatFixtures</c>, so an automated-testing document builds its fixture with the same helper
/// rather than a second copy of the byte layout. Each operation writes only bytes the documented SQLite file
/// format defines, and refuses when its precondition does not hold.
/// <list type="bullet">
/// <item>
/// <c>--rewrite-last-index-key &lt;db&gt; &lt;index&gt; &lt;value&gt;</c>: rewrites the last entry of an index whose
/// first column holds 8-bit integers, so the index no longer agrees with its table.
/// </item>
/// </list>
/// </summary>
internal sealed class DamageCommand : IProbeCommand
{
    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        if (args is not ["--rewrite-last-index-key", string db, string index, string value])
            return ExpectationResult.Failed("damage takes --rewrite-last-index-key <db> <index> <value>");

        if (!sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte newValue))
            return ExpectationResult.Failed($"'{value}' is not a value from -128 to 127");

        try
        {
            DocumentedFormatFixtures.RewriteLastIndexKey(db, index, newValue);
            return ExpectationResult.Passed($"rewrote the last key of {index} in {db} to {newValue}");
        }
        // A precondition the helper checks before writing anything: the index, its page or its key is not what
        // the damage needs. Reported as the command's failure, so a document stops on it.
        catch (InvalidOperationException ex)
        {
            return ExpectationResult.Failed(ex.Message);
        }
    }
}
