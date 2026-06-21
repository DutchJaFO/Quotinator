using System.Text.RegularExpressions;
using Quotinator.Changelog.Formatting;

namespace Quotinator.Changelog.Tests;

[TestClass]
public sealed class GeneratedFileHeaderTests
{
    private const string NoticePrefix    = "### *GENERATED FILE [";
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
        Assert.IsTrue(firstLine.StartsWith(NoticePrefix),
            $"First line must start with '{NoticePrefix}'");
    }

    [TestMethod]
    public void Build_FirstLine_ContainsUtcTimestampSuffix()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", "some-cmd");
        var firstLine = result.Split('\n')[0];
        Assert.IsTrue(firstLine.Contains(TimestampSuffix),
            $"First line must contain '{TimestampSuffix}'");
    }

    [TestMethod]
    public void Build_FirstLine_ContainsDoNotEditInstruction()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", "some-cmd");
        var firstLine = result.Split('\n')[0];
        Assert.IsTrue(firstLine.Contains(Instruction),
            $"First line must contain '{Instruction}'");
    }

    [TestMethod]
    public void Build_FirstLine_TimestampMatchesInputValue()
    {
        var timestamp = new DateTime(2026, 6, 21, 12, 8, 0, DateTimeKind.Utc);
        var result    = GeneratedFileHeader.Build(timestamp, "changelog.json", "some-cmd");
        Assert.IsTrue(result.Contains("2026-06-21 12:08"),
            "Header must contain the timestamp value passed to Build");
    }

    [TestMethod]
    public void Build_ContainsEditLabelAndInputPath()
    {
        const string inputPath = "src/Quotinator.Api/changelog.json";
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, inputPath, "some-cmd");
        Assert.IsTrue(result.Contains(EditLabel),
            $"Header must contain '{EditLabel}'");
        Assert.IsTrue(result.Contains(inputPath),
            "Header must contain the input path passed to Build");
    }

    [TestMethod]
    public void Build_ContainsRegenerateLabel()
    {
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", "some-cmd");
        Assert.IsTrue(result.Contains(RegenerateLabel),
            $"Header must contain '{RegenerateLabel}'");
    }

    [TestMethod]
    public void Build_ContainsRegenerateCommand()
    {
        const string cmd = "dotnet-script changelog.csx -- --format keepachangelog --input changelog.json";
        var result = GeneratedFileHeader.Build(DateTime.UtcNow, "changelog.json", cmd);
        Assert.IsTrue(result.Contains(cmd),
            "Header must contain the regenerate command passed to Build");
    }
}
