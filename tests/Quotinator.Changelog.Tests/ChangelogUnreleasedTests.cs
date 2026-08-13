using Quotinator.Changelog.Enums;
using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Tests;

[TestClass]
public sealed class ChangelogUnreleasedTests
{
    private static readonly string[] ExpectedHighlights = ["First highlight.", "Second highlight."];

    [TestMethod]
    public void GetHighlightsFor_NotificationKeyPresent_ReturnsItems()
    {
        var unreleased = new ChangelogUnreleased
        {
            AudienceHighlights = new Dictionary<string, List<string>>
            {
                ["notification"] = [.. ExpectedHighlights]
            }
        };

        var result = unreleased.GetHighlightsFor(ChangelogReservedAudience.Notification);

        Assert.AreSequenceEqual(ExpectedHighlights, result);
    }

    [TestMethod]
    public void GetHighlightsFor_NotificationKeyAbsent_ReturnsEmptyList()
    {
        var unreleased = new ChangelogUnreleased
        {
            Highlights = ["Not the notification audience."]
        };

        var result = unreleased.GetHighlightsFor(ChangelogReservedAudience.Notification);

        Assert.IsNotNull(result);
        Assert.IsEmpty(result);
    }
}
