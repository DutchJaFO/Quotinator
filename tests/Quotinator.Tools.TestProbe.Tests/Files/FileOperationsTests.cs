using System.Text;
using Quotinator.Tools.TestProbe.Files;

namespace Quotinator.Tools.TestProbe.Tests.Files;

[TestClass]
public sealed class FileOperationsTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "testprobe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteRoot()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void Copy_CreatesTheMissingDirectoryAndCopiesTheContent()
    {
        string source = InRoot("source.json");
        string destination = Path.Combine(_root, "imports", "copied.json");
        File.WriteAllText(source, "the content");

        Assert.IsTrue(FileOperations.Copy(source, destination).Holds);
        Assert.AreEqual("the content", File.ReadAllText(destination));
    }

    [TestMethod]
    public void Copy_MissingSource_Fails()
    {
        Assert.IsFalse(FileOperations.Copy(InRoot("absent.json"), InRoot("copied.json")).Holds);
    }

    [TestMethod]
    public void ExpectText_PatternPresent_Holds()
    {
        string path = InRoot("page.html");
        File.WriteAllText(path, "<META http-equiv=\"refresh\" content=\"2\">");

        Assert.IsTrue(FileOperations.ExpectText(path, "http-equiv\\s*=\\s*.refresh").Holds);
    }

    [TestMethod]
    public void ExpectText_PatternAbsent_Fails()
    {
        string path = InRoot("page.html");
        File.WriteAllText(path, "<p>no refresh meta here</p>");

        Assert.IsFalse(FileOperations.ExpectText(path, "http-equiv").Holds);
    }

    [TestMethod]
    public void ExpectText_MissingFile_Fails()
    {
        Assert.IsFalse(FileOperations.ExpectText(InRoot("absent.html"), "anything").Holds);
    }

    [TestMethod]
    public void ExpectNoText_PatternAbsent_Holds()
    {
        string path = InRoot("page.html");
        File.WriteAllText(path, "<link href=\"lib/site.css\">");

        Assert.IsTrue(FileOperations.ExpectNoText(path, "href\\s*=\\s*.https?://").Holds);
    }

    [TestMethod]
    public void ExpectNoText_PatternPresent_Fails()
    {
        string path = InRoot("page.html");
        File.WriteAllText(path, "<link href=\"https://example.test/site.css\">");

        Assert.IsFalse(FileOperations.ExpectNoText(path, "href\\s*=\\s*.https?://").Holds);
    }

    [TestMethod]
    public void ExpectNoText_MissingFile_Fails()
    {
        Assert.IsFalse(FileOperations.ExpectNoText(InRoot("absent.html"), "anything").Holds);
    }

    [TestMethod]
    public void WriteText_WritesTheTextWithoutAByteOrderMark()
    {
        string path = InRoot("a.json");

        FileOperations.WriteText(path, "{}");

        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes("{}"), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void WriteText_MissingParentDirectory_IsCreated()
    {
        string path = Path.Combine(_root, "absent", "keys");

        FileOperations.WriteText(path, "x");

        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void Sparse_MissingParentDirectory_IsCreated()
    {
        string path = Path.Combine(_root, "absent", "filler");

        FileOperations.Sparse(path, 1);

        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void WriteText_ExistingFile_IsReplaced()
    {
        string path = InRoot("a.json");
        File.WriteAllText(path, "longer content");

        FileOperations.WriteText(path, "x");

        Assert.AreEqual("x", File.ReadAllText(path));
    }

    [TestMethod]
    public void TruncateHalf_CutsToHalfRoundedDown()
    {
        string path = InRoot("db");
        File.WriteAllBytes(path, new byte[11]);

        FileOperations.TruncateHalf(path);

        Assert.AreEqual(5, new FileInfo(path).Length);
    }

    [TestMethod]
    public void TruncateHalf_MissingFile_Fails()
        => Assert.IsFalse(FileOperations.TruncateHalf(InRoot("absent")).Holds);

    [TestMethod]
    public void Sparse_CreatesAFileOfTheLength()
    {
        string path = InRoot("big");

        FileOperations.Sparse(path, 4096);

        Assert.AreEqual(4096, new FileInfo(path).Length);
    }

    [TestMethod]
    public void CreateDirectory_Missing_CreatesIt()
    {
        string directory = InRoot("new");

        FileOperations.CreateDirectory(directory);

        Assert.IsTrue(Directory.Exists(directory));
    }

    [TestMethod]
    public void CreateDirectory_AlreadyThere_Holds()
        => Assert.IsTrue(FileOperations.CreateDirectory(_root).Holds);

    [TestMethod]
    public void CreateDirectory_AFileIsInTheWay_Fails()
    {
        string path = InRoot("f");
        File.WriteAllText(path, "x");

        Assert.IsFalse(FileOperations.CreateDirectory(path).Holds);
    }

    [TestMethod]
    public void Delete_File_RemovesIt()
    {
        string path = InRoot("f");
        File.WriteAllText(path, "x");

        FileOperations.Delete(path);

        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Delete_DirectoryWithContent_RemovesIt()
    {
        string directory = InRoot("d");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "f"), "x");

        FileOperations.Delete(directory);

        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void Delete_NothingThere_Holds()
        => Assert.IsTrue(FileOperations.Delete(InRoot("absent")).Holds);

    [TestMethod]
    public void ExpectFile_File_Holds()
    {
        string path = InRoot("f");
        File.WriteAllText(path, "x");

        Assert.IsTrue(FileOperations.ExpectFile(path).Holds);
    }

    [TestMethod]
    public void ExpectFile_Directory_Fails()
        => Assert.IsFalse(FileOperations.ExpectFile(_root).Holds);

    [TestMethod]
    public void ExpectDirectory_Directory_Holds()
        => Assert.IsTrue(FileOperations.ExpectDirectory(_root).Holds);

    [TestMethod]
    public void ExpectDirectory_Missing_Fails()
        => Assert.IsFalse(FileOperations.ExpectDirectory(InRoot("absent")).Holds);

    [TestMethod]
    public void ExpectEmptyDirectory_Empty_Holds()
        => Assert.IsTrue(FileOperations.ExpectEmptyDirectory(_root).Holds);

    [TestMethod]
    public void ExpectEmptyDirectory_WithAFile_Fails()
    {
        File.WriteAllText(InRoot("f"), "x");

        Assert.IsFalse(FileOperations.ExpectEmptyDirectory(_root).Holds);
    }

    [TestMethod]
    public void ExpectEmptyDirectory_Missing_Fails()
        => Assert.IsFalse(FileOperations.ExpectEmptyDirectory(InRoot("absent")).Holds);

    [TestMethod]
    public void ExpectLength_Matching_Holds()
    {
        string path = InRoot("f");
        File.WriteAllBytes(path, new byte[3]);

        Assert.IsTrue(FileOperations.ExpectLength(path, 3).Holds);
    }

    [TestMethod]
    public void ExpectLength_Different_Fails()
    {
        string path = InRoot("f");
        File.WriteAllBytes(path, new byte[3]);

        Assert.IsFalse(FileOperations.ExpectLength(path, 4).Holds);
    }

    [TestMethod]
    public void ExpectLength_Missing_Fails()
        => Assert.IsFalse(FileOperations.ExpectLength(InRoot("absent"), 0).Holds);

    [TestMethod]
    public void ExpectMatches_EnoughMatches_Holds()
    {
        File.WriteAllText(InRoot("a.bak"), "x");
        File.WriteAllText(InRoot("b.bak"), "x");

        Assert.IsTrue(FileOperations.ExpectMatches(_root, "*.bak", 2).Holds);
    }

    [TestMethod]
    public void ExpectMatches_TooFewMatches_Fails()
    {
        File.WriteAllText(InRoot("a.bak"), "x");
        File.WriteAllText(InRoot("b.txt"), "x");

        Assert.IsFalse(FileOperations.ExpectMatches(_root, "*.bak", 2).Holds);
    }

    [TestMethod]
    public void ExpectAbsent_NothingThere_Holds()
        => Assert.IsTrue(FileOperations.ExpectAbsent(InRoot("absent")).Holds);

    [TestMethod]
    public void ExpectAbsent_File_Fails()
    {
        string path = InRoot("f");
        File.WriteAllText(path, "x");

        Assert.IsFalse(FileOperations.ExpectAbsent(path).Holds);
    }

    [TestMethod]
    public void ExpectAbsent_Directory_Fails()
        => Assert.IsFalse(FileOperations.ExpectAbsent(_root).Holds);

    [TestMethod]
    public void ExpectMatchCount_ExactCount_Holds()
    {
        File.WriteAllText(InRoot("a.db"), "x");

        Assert.IsTrue(FileOperations.ExpectMatchCount(_root, "*.db", 1).Holds);
    }

    [TestMethod]
    public void ExpectMatchCount_MoreThanTheCount_Fails()
    {
        File.WriteAllText(InRoot("a.db"), "x");
        File.WriteAllText(InRoot("b.db"), "x");

        Assert.IsFalse(FileOperations.ExpectMatchCount(_root, "*.db", 1).Holds);
    }

    [TestMethod]
    public void ExpectMatchCount_MissingDirectory_Fails()
        => Assert.IsFalse(FileOperations.ExpectMatchCount(InRoot("absent"), "*", 0).Holds);

    [TestMethod]
    public void ExpectMatches_MissingDirectory_Fails()
        => Assert.IsFalse(FileOperations.ExpectMatches(InRoot("absent"), "*", 0).Holds);

    private string InRoot(string name) => Path.Combine(_root, name);
}
