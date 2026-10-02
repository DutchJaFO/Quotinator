using System.Text.Json;
using Quotinator.Api.Startup;
using Quotinator.Data.Database;
using Quotinator.Data.Notifications;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Data-owned migration 27 restates the stored #279 announcement over four lines and clears the expiry
/// v1.8.3 gave it (#413), continuing the in-place edit migration 24 began (#348). The migration's SQL is
/// frozen text and the producer's is live, so nothing but these tests keeps the two saying the same
/// thing.
/// <para>
/// Migration 24 has no live guard of its own any more, deliberately: its text is frozen history, and
/// migration 27 is what a database ends up agreeing with. Holding 24 to the producer would assert that
/// the producer still ships the single-line text, which is exactly what this issue changed.
/// </para>
/// </summary>
[TestClass]
public class OperationIdRenameAnnouncementTests
{
    private const string BodyKey = "NotificationOperationIdRenameBody";

    /// <summary>The hash is what identifies the stored row, so the migration must write the one the producer computes.</summary>
    [TestMethod]
    public void Migration27_WritesTheContentHashTheProducerComputes()
    {
        Assert.Contains($"'{NotificationContentHash.Of(OperationIdRenameAnnouncement.Body)}'",
            NotificationAnnouncementLineBreakMigrations.BreakAnnouncementIntoLines, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserted line by line, because the migration joins the lines with <c>char(10)</c> rather than
    /// holding the body as one literal: a whole-body match would be asserting the SQL's formatting
    /// rather than its content.
    /// </summary>
    [TestMethod]
    public void Migration27_WritesEveryLineOfTheBodyTheProducerWrites()
    {
        foreach (string line in OperationIdRenameAnnouncement.Body.Split('\n'))
        {
            Assert.Contains(SqlLiteral(line),
                NotificationAnnouncementLineBreakMigrations.BreakAnnouncementIntoLines, StringComparison.Ordinal,
                $"The migration must write the producer's line: {line}");
        }
    }

    /// <summary>The producer's body is what this issue made multi-line; a single line would defeat the point.</summary>
    [TestMethod]
    public void TheProducersBody_IsLaidOutOverSeveralLines()
    {
        Assert.IsGreaterThan(1, OperationIdRenameAnnouncement.Body.Split('\n').Length);
    }

    /// <summary>One line per renamed operation ID, so neither shares a line with prose or the other.</summary>
    [TestMethod]
    [DataRow("GetImportBatches")]
    [DataRow("GetFileResources")]
    public void TheProducersBody_GivesEachRenamedOperationIdItsOwnLine(string oldName)
    {
        string[] carrying = [.. OperationIdRenameAnnouncement.Body.Split('\n')
            .Where(l => l.Contains(oldName, StringComparison.Ordinal))];

        Assert.HasCount(1, carrying);
        Assert.AreEqual($"{oldName} → GetAll{oldName[3..]}", carrying[0].Trim());
    }

    /// <summary>The producer writes each translation from the UI string files; the migration must match them too.</summary>
    [TestMethod]
    [DataRow("nl")]
    [DataRow("de")]
    public void Migration27_WritesEveryLineOfTheTranslationTheProducerWrites(string language)
    {
        foreach (string line in TranslatedBody(language).Split('\n'))
        {
            Assert.Contains(SqlLiteral(line),
                NotificationAnnouncementLineBreakMigrations.BreakAnnouncementIntoLines, StringComparison.Ordinal,
                $"The migration must write the {language} line: {line}");
        }
    }

    /// <summary>Every language is laid out, not only the original: the issue asks for all three.</summary>
    [TestMethod]
    [DataRow("en-GB")]
    [DataRow("nl")]
    [DataRow("de")]
    public void EveryLanguagesBody_IsLaidOutOverSeveralLines(string language)
    {
        Assert.IsGreaterThan(1, TranslatedBody(language).Split('\n').Length);
    }

    private static string SqlLiteral(string text) => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string TranslatedBody(string language)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Quotinator.Api", "i18ntext", $"UI.{language}.json");
        Dictionary<string, string> strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        return strings[BodyKey];
    }
}
