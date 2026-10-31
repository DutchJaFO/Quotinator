using Quotinator.Tools.TestProbe.Commands;

namespace Quotinator.Tools.TestProbe.Tests.Commands;

[TestClass]
public sealed class BundledSeedCommandTests
{
    private const string Rules = """{"rules":[{"entityId":"AAAA0000-0000-0000-0000-000000000001"}]}""";

    private string _root = string.Empty;
    private string _declared = string.Empty;
    private string _list = string.Empty;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "testprobe-" + Guid.NewGuid().ToString("N"));
        _declared = Path.Combine(_root, "declared");
        _list = Path.Combine(_root, "list.json");
        Directory.CreateDirectory(_declared);
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void SeedSources_DistinctSources_Holds()
        => Assert.AreEqual(0, SeedSources(Item("One", "1994"), Item("Two", "1995")).ExitCode);

    [TestMethod]
    public void SeedSources_NoRows_Fails()
        => Assert.AreEqual(1, SeedSources().ExitCode);

    [TestMethod]
    public void SeedSources_ExactDuplicate_FailsNamingIt()
    {
        (int exitCode, string error) = SeedSources(Item("One", "1994"), Item("ONE", "1994"));

        Assert.AreEqual(1, exitCode);
        Assert.Contains("exact duplicate", error);
    }

    [TestMethod]
    public void SeedSources_SameTitleUnderAnotherType_Holds()
        => Assert.AreEqual(0, SeedSources(Item("One", "1994"), Item("One", "1994", "tv")).ExitCode);

    [TestMethod]
    public void SeedSources_TwoSpellings_FailsNamingIt()
    {
        Declare("One", "1994");
        Declare("ONE", "1995");

        (int exitCode, string error) = SeedSources(Item("One", "1994"), Item("ONE", "1995"));

        Assert.AreEqual(1, exitCode);
        Assert.Contains("casing duplicate", error);
    }

    [TestMethod]
    public void SeedSources_UndeclaredDateVariant_FailsNamingIt()
    {
        (int exitCode, string error) = SeedSources(Item("One", "1994"), Item("One", "2019"));

        Assert.AreEqual(1, exitCode);
        Assert.Contains("undeclared date variant", error);
    }

    [TestMethod]
    public void SeedSources_OneOfTwoDatesDeclared_Fails()
    {
        Declare("One", "1994");

        Assert.AreEqual(1, SeedSources(Item("One", "1994"), Item("One", "2019")).ExitCode);
    }

    [TestMethod]
    public void SeedSources_BothDatesDeclared_HoldsInEitherCase()
    {
        Declare("ONE", "1994");
        Declare("one", "2019");

        Assert.AreEqual(0, SeedSources(Item("One", "1994"), Item("One", "2019")).ExitCode);
    }

    [TestMethod]
    public void SeedNoOps_NoNoOps_Fails()
        => Assert.AreEqual(1, SeedNoOps("""{"id":"x","actionType":"Add","entityType":"Quote","entityId":"b","incomingFields":{}}""").ExitCode);

    [TestMethod]
    public void SeedNoOps_IncomingLeavesTheFieldEmpty_Holds()
        => Assert.AreEqual(0, SeedNoOps(NoOp("c", """{"date":"1972"}""", """{"date":null}""")).ExitCode);

    [TestMethod]
    public void SeedNoOps_IncomingHoldsAnEmptyArray_Holds()
        => Assert.AreEqual(0, SeedNoOps(NoOp("c", """{"genres":["drama"]}""", """{"genres":[]}""")).ExitCode);

    [TestMethod]
    public void SeedNoOps_IncomingHoldsADifferentArray_Fails()
        => Assert.AreEqual(1, SeedNoOps(NoOp("c", """{"genres":["drama"]}""", """{"genres":["comedy"]}""")).ExitCode);

    [TestMethod]
    public void SeedNoOps_IncomingLacksTheField_Holds()
        => Assert.AreEqual(0, SeedNoOps(NoOp("c", """{"date":"1972"}""", "{}")).ExitCode);

    [TestMethod]
    public void SeedNoOps_IncomingAgrees_Holds()
        => Assert.AreEqual(0, SeedNoOps(NoOp("c", """{"date":"1972"}""", """{"date":"1972"}""")).ExitCode);

    [TestMethod]
    public void SeedNoOps_IncomingDisagreesWithNoRule_FailsNamingIt()
    {
        (int exitCode, string error) = SeedNoOps(NoOp("cccc", """{"date":"1972"}""", """{"date":"1980"}"""));

        Assert.AreEqual(1, exitCode);
        Assert.Contains("cccc", error);
    }

    [TestMethod]
    public void SeedNoOps_IncomingDisagreesWithARuleInEitherCase_Holds()
        => Assert.AreEqual(0, SeedNoOps(NoOp("aaaa0000-0000-0000-0000-000000000001", """{"date":"1972"}""", """{"date":"1980"}""")).ExitCode);

    [TestMethod]
    public void SeedNoOps_OtherKindsAreNotJudged_Holds()
        => Assert.AreEqual(0, SeedNoOps(
            NoOp("c", """{"date":"1972"}""", "{}"),
            """{"id":"x","actionType":"Modify","entityType":"Quote","entityId":"d","existingFields":{"date":"1"},"incomingFields":{"date":"2"}}""").ExitCode);

    [TestMethod]
    public void SeedSources_MissingFile_Fails()
        => Assert.AreEqual(1, Run("seed-sources", "--sources", Path.Combine(_root, "absent.json"), "--declared", _declared).ExitCode);

    private static string Item(string title, string date, string type = "movie")
        => $$"""{"title":"{{title}}","type":"{{type}}","date":"{{date}}"}""";

    private static string NoOp(string entityId, string existing, string incoming)
        => $$"""{"id":"x","actionType":"ResolvedToExisting","entityType":"Source","entityId":"{{entityId}}","existingFields":{{existing}},"incomingFields":{{incoming}}}""";

    private void Declare(string title, string date)
        => File.WriteAllText(
            Path.Combine(_declared, Guid.NewGuid().ToString("N") + ".json"),
            $$"""{"quotes":[],"sources":[{"title":"{{title}}","type":"movie","date":"{{date}}"}]}""");

    private (int ExitCode, string Error) SeedSources(params string[] items)
    {
        File.WriteAllText(_list, $$"""{"items":[{{string.Join(",", items)}}]}""");

        return Run("seed-sources", "--sources", _list, "--declared", _declared);
    }

    private (int ExitCode, string Error) SeedNoOps(params string[] items)
    {
        File.WriteAllText(_list, $$"""{"items":[{{string.Join(",", items)}}]}""");
        File.WriteAllText(Path.Combine(_declared, "one-conflict-rules.json"), Rules);

        return Run("seed-noops", "--actions", _list, "--rules", _declared);
    }

    private static (int ExitCode, string Error) Run(params string[] args)
    {
        using StringReader input = new(string.Empty);
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = CommandDispatcher.Run(args, input, output, error);

        return (exitCode, error.ToString());
    }
}
