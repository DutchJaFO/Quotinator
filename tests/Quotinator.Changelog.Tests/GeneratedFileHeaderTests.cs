using System.Text.RegularExpressions;
using Quotinator.Changelog.Formatting;

namespace Quotinator.Changelog.Tests;

[TestClass]
public sealed class GeneratedFileHeaderTests
{
    private const string NoticePrefix    = "##### *GENERATED FILE [";
    private const string TimestampSuffix = " UTC]";
    private const string Instruction     = "do not edit by hand.";
    private const string EditLabel       = "Edit:";
    private const string RegenerateLabel = "To regenerate:";

    private static readonly Regex TimestampPattern =
        new(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}", RegexOptions.Compiled);

    [TestMethod]
    public void Build_FirstLine_StartsWithNoticePrefix()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", "some-cmd");
        var firstLine = result.Split('\n')[0];
        Assert.StartsWith(NoticePrefix, firstLine,
            $"First line must start with '{NoticePrefix}'");
    }

    [TestMethod]
    public void Build_FirstLine_ContainsUtcTimestampSuffix()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", "some-cmd");
        var firstLine = result.Split('\n')[0];
        Assert.Contains(TimestampSuffix, firstLine,
            $"First line must contain '{TimestampSuffix}'");
    }

    [TestMethod]
    public void Build_FirstLine_ContainsDoNotEditInstruction()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", "some-cmd");
        var firstLine = result.Split('\n')[0];
        Assert.Contains(Instruction, firstLine,
            $"First line must contain '{Instruction}'");
    }

    [TestMethod]
    public void Build_FirstLine_TimestampMatchesInputValue()
    {
        var timestamp = new DateTime(2026, 6, 21, 12, 8, 0, DateTimeKind.Utc);
        var result    = GeneratedFileHeader.Build(timestamp, "changelog.json", "some-cmd");
        Assert.Contains("2026-06-21 12:08", result,
            "Header must contain the timestamp value passed to Build");
    }

    [TestMethod]
    public void Build_ContainsEditLabelAndInputPath()
    {
        const string inputPath = "src/Quotinator.Api/changelog.json";
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, inputPath, "some-cmd");
        Assert.Contains(EditLabel, result,
            $"Header must contain '{EditLabel}'");
        Assert.Contains(inputPath, result,
            "Header must contain the input path passed to Build");
    }

    [TestMethod]
    public void Build_ContainsRegenerateLabel()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", "some-cmd");
        Assert.Contains(RegenerateLabel, result,
            $"Header must contain '{RegenerateLabel}'");
    }

    [TestMethod]
    public void Build_ContainsRegenerateCommand()
    {
        const string cmd = "dotnet-script changelog.csx -- --format keepachangelog --input changelog.json";
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", cmd);
        Assert.Contains(cmd, result,
            "Header must contain the regenerate command passed to Build");
    }

    [TestMethod]
    public void Build_Minimal_StartsWithNoticePrefix()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow);
        Assert.StartsWith(NoticePrefix, result,
            $"Minimal header must start with '{NoticePrefix}'");
    }

    [TestMethod]
    public void Build_Minimal_ContainsUtcTimestampSuffix()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow);
        Assert.Contains(TimestampSuffix, result,
            $"Minimal header must contain '{TimestampSuffix}'");
    }

    [TestMethod]
    public void Build_Minimal_TimestampMatchesInputValue()
    {
        var timestamp = new DateTime(2026, 6, 22, 9, 0, 0, DateTimeKind.Utc);
        var result    = GeneratedFileHeader.Build(timestamp);
        Assert.Contains("2026-06-22 09:00", result,
            "Minimal header must contain the timestamp value passed to Build");
    }

    [TestMethod]
    public void Build_Minimal_IsSingleLine()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow);
        Assert.IsFalse(result.Contains('\n'),
            "Minimal header must be a single line with no newlines");
    }

    [TestMethod]
    public void Build_Minimal_DoesNotContainEditLabel()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow);
        Assert.DoesNotContain(EditLabel, result,
            $"Minimal header must not contain '{EditLabel}'");
    }

    [TestMethod]
    public void Build_Minimal_DoesNotContainRegenerateLabel()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow);
        Assert.DoesNotContain(RegenerateLabel, result,
            $"Minimal header must not contain '{RegenerateLabel}'");
    }
}
