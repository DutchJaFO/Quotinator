using System.Text.Json;
using Quotinator.Api.Startup;
using Quotinator.Data.Database;
using Quotinator.Data.Notifications;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #348: Data-owned migration 24 rewrites the stored #279 announcement to the producer's current text,
/// so a reworded announcement is not written a second time. The migration's SQL is frozen text and the
/// producer's is live, so nothing but these tests keeps the two saying the same thing.
/// </summary>
[TestClass]
public class OperationIdRenameAnnouncementTests
{
    private const string BodyKey = "NotificationOperationIdRenameBody";

    /// <summary>The hash is what identifies the stored row, so the migration must write the one the producer computes.</summary>
    [TestMethod]
    public void Migration24_WritesTheContentHashTheProducerComputes()
    {
        Assert.Contains($"'{NotificationContentHash.Of(OperationIdRenameAnnouncement.Body)}'",
            NotificationAnnouncementRewordMigrations.RewordOperationIdRename, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Migration24_WritesTheBodyTheProducerWrites()
    {
        Assert.Contains(SqlLiteral(OperationIdRenameAnnouncement.Body),
            NotificationAnnouncementRewordMigrations.RewordOperationIdRename, StringComparison.Ordinal);
    }

    /// <summary>The producer writes each translation from the UI string files; the migration must match them too.</summary>
    [TestMethod]
    [DataRow("nl")]
    [DataRow("de")]
    public void Migration24_WritesTheTranslationTheProducerWrites(string language)
    {
        Assert.Contains(SqlLiteral(TranslatedBody(language)),
            NotificationAnnouncementRewordMigrations.RewordOperationIdRename, StringComparison.Ordinal);
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
