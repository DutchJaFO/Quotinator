using Quotinator.Tools.TestProbe.Commands;

namespace Quotinator.Tools.TestProbe.Tests.Commands;

[TestClass]
public sealed class UrlCommandTests
{
    private string _root = string.Empty;
    private string _body = string.Empty;
    private string _out = string.Empty;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "testprobe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _body = Path.Combine(_root, "body.json");
        _out = Path.Combine(_root, "made", "url.txt");
        File.WriteAllText(_body, """{"batchId":"ab12","items":[{"id":"cd34"}]}""");
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void Url_FillsThePlaceholderAndWritesTheFile()
    {
        Assert.AreEqual(0, Run("http://h/x/{batchId}", "--value", $"batchId={_body}#batchId"));
        Assert.AreEqual("http://h/x/ab12", File.ReadAllText(_out));
    }

    [TestMethod]
    public void Url_ReadsAPathThroughAnArray()
    {
        Assert.AreEqual(0, Run("http://h/x/{id}/decide", "--value", $"id={_body}#items.0.id"));
        Assert.AreEqual("http://h/x/cd34/decide", File.ReadAllText(_out));
    }

    [TestMethod]
    public void Url_UpperValue_FillsItInUpperCase()
    {
        Assert.AreEqual(0, Run("http://h/x?batchId={batchId}", "--upper-value", $"batchId={_body}#batchId"));
        Assert.AreEqual("http://h/x?batchId=AB12", File.ReadAllText(_out));
    }

    [TestMethod]
    public void Url_LowerValue_FillsItInLowerCase()
    {
        File.WriteAllText(_body, """{"batchId":"AB12"}""");

        Assert.AreEqual(0, Run("http://h/x?batchId={batchId}", "--lower-value", $"batchId={_body}#batchId"));
        Assert.AreEqual("http://h/x?batchId=ab12", File.ReadAllText(_out));
    }

    [TestMethod]
    public void Url_FieldMissingFromTheFile_ExitsOneAndWritesNothing()
    {
        Assert.AreEqual(1, Run("http://h/x/{id}", "--value", $"id={_body}#absent"));
        Assert.IsFalse(File.Exists(_out));
    }

    [TestMethod]
    public void Url_SourceFileMissing_ExitsOne()
        => Assert.AreEqual(1, Run("http://h/x/{id}", "--value", $"id={Path.Combine(_root, "absent.json")}#id"));

    [TestMethod]
    public void Url_PlaceholderLeftUnfilled_ExitsOneAndWritesNothing()
    {
        Assert.AreEqual(1, Run("http://h/x/{other}", "--value", $"batchId={_body}#batchId"));
        Assert.IsFalse(File.Exists(_out));
    }

    [TestMethod]
    public void Url_ValueNamingNoPlaceholder_ExitsOne()
        => Assert.AreEqual(1, Run("http://h/x", "--value", $"batchId={_body}#batchId"));

    [TestMethod]
    public void Url_WithoutATemplate_ExitsOne()
    {
        using StringReader input = new(string.Empty);
        using StringWriter output = new();
        using StringWriter error = new();

        Assert.AreEqual(1, CommandDispatcher.Run(["url", "--out", _out], input, output, error));
    }

    private int Run(string template, string option, string value)
    {
        using StringReader input = new(string.Empty);
        using StringWriter output = new();
        using StringWriter error = new();

        return CommandDispatcher.Run(["url", "--template", template, option, value, "--out", _out], input, output, error);
    }
}
