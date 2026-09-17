namespace Quotinator.Api.Tests.Solution;

/// <summary>
/// `docs/logging.md` documents the exception lines #397 adds. Asserted over the document's own text
/// rather than left to a reader, per `process.md`'s refusal of a verification step nobody schedules —
/// two documentation-confirmation rows held #307 open for weeks.
/// </summary>
[TestClass]
public class LoggingDocumentationTests
{
    private static readonly string LoggingDoc =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "logging.md"));

    /// <summary>
    /// The prefix is registered in the defined-prefixes table, and each of the three lines is
    /// documented with the level it is written at — the thing a reader needs in order to tell an
    /// expected exception from a dangerous one.
    /// </summary>
    [TestMethod]
    public void RuntimeExceptionLines_AreRegisteredAndDocumentedWithTheirLevels()
    {
        Assert.IsTrue(File.Exists(LoggingDoc), $"docs/logging.md not found at: {LoggingDoc}");

        string doc = File.ReadAllText(LoggingDoc);

        Assert.Contains("[Runtime - Exception]", doc,
            "a new subsystem registers its prefix in the Defined prefixes table before its log lines land");

        foreach (string expected in new[] { "thrown", "handled", "not handled" })
        {
            Assert.Contains(expected, doc,
                $"the '{expected}' line is undocumented, so nothing tells a reader what it means");
        }

        int prefix = doc.IndexOf("[Runtime - Exception]", StringComparison.Ordinal);
        string fromPrefix = doc[prefix..];

        Assert.Contains("Error", fromPrefix, "the thrown and handled lines' level is not stated");
        Assert.Contains("Critical", fromPrefix, "the level for an exception nothing handled is not stated");
    }
}
