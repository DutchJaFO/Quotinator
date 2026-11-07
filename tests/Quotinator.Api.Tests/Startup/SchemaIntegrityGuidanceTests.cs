using Quotinator.Api.Startup;
using Quotinator.Constants.Routes;
using Quotinator.Data.Enums;
using Quotinator.Data.Schema;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #305: what an operator reads when the schema does not match its migration path. It names every missing
/// or different object, the two remedies, and nothing about how a particular engine exposes its schema
/// (ADR 003, design goal 5).
/// </summary>
[TestClass]
public class SchemaIntegrityGuidanceTests
{
    [TestMethod]
    public void Reason_NamesEveryDifference()
    {
        string reason = SchemaIntegrityGuidance.Reason(NotIntact());

        Assert.Contains("System_Notification", reason, StringComparison.Ordinal);
        Assert.Contains("Quotinator_Quote.Text", reason, StringComparison.Ordinal);
        Assert.Contains("IX_Quote_Source", reason, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Reason_NamesResetAndRestore()
    {
        string reason = SchemaIntegrityGuidance.Reason(NotIntact());

        Assert.Contains("database Reset", reason, StringComparison.Ordinal);
        Assert.Contains(ApiRoutes.DatabaseReset, reason, StringComparison.Ordinal);
        Assert.Contains("restore", reason, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Reason_UsesNoEngineMetadataNames()
    {
        string reason = SchemaIntegrityGuidance.Reason(NotIntact());

        Assert.IsFalse(string.IsNullOrWhiteSpace(reason), "control: an empty reason contains no engine names either");
        foreach (string engineName in new[] { "sqlite", "pragma", "sqlite_master" })
            Assert.DoesNotContain(engineName, reason, StringComparison.OrdinalIgnoreCase);
    }

    private static SchemaIntegrityResult NotIntact() => new()
    {
        Differences =
        [
            new SchemaDifference(SchemaDifferenceKind.Missing, SchemaObjectKind.Table, "System_Notification", null, null, null),
            new SchemaDifference(SchemaDifferenceKind.Different, SchemaObjectKind.Column, "Quotinator_Quote.Text",
                nameof(SchemaColumn.DeclaredType), "TEXT", "BLOB"),
            new SchemaDifference(SchemaDifferenceKind.Missing, SchemaObjectKind.Index, "Quotinator_Quote.IX_Quote_Source", null, null, null),
            new SchemaDifference(SchemaDifferenceKind.Extra, SchemaObjectKind.Column, "Quotinator_Quote.Stray", null, null, null),
        ],
    };
}
