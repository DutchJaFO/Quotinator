using System.Reflection;
using Quotinator.Constants.Api;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Solution;

/// <summary>
/// #348: the Knowledgebase entries the application links to exist, and the one a backup refusal links to
/// covers every obstacle. A link the application renders to a missing page, or an entry silent about the
/// obstacle the reader was sent there for, is the reader told to look somewhere that does not answer.
/// </summary>
public partial class RepositoryStructureTests
{
    private const string KnowledgebaseAddressPrefix = "https://github.com/DutchJaFO/Quotinator/blob/main/";

    /// <summary>Every address <see cref="KnowledgebaseLinks"/> holds, so a link added later is checked without being listed here.</summary>
    public static IEnumerable<object[]> KnowledgebaseAddresses =>
        typeof(KnowledgebaseLinks).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => new object[] { (string)f.GetRawConstantValue()! });

    /// <summary>Every obstacle a backup can report; <see cref="BackupOutcome.Succeeded"/> is not one.</summary>
    public static IEnumerable<object[]> BackupObstacles =>
        Enum.GetValues<BackupOutcome>().Where(o => o != BackupOutcome.Succeeded).Select(o => new object[] { o });

    [TestMethod]
    [DynamicData(nameof(KnowledgebaseAddresses))]
    public void KnowledgebaseLink_NamesAnEntryThatExists(string address)
    {
        Assert.IsTrue(File.Exists(Path.Combine(RepoRoot, address[KnowledgebaseAddressPrefix.Length..])), address);
    }

    /// <summary>The entry has a section headed by each obstacle's name, which is the name the refusal quotes.</summary>
    [TestMethod]
    [DynamicData(nameof(BackupObstacles))]
    public void NoBackupCouldBeTaken_HasASectionForTheObstacle(BackupOutcome obstacle)
    {
        string entry = File.ReadAllText(Path.Combine(
            RepoRoot, KnowledgebaseLinks.NoBackupCouldBeTaken[KnowledgebaseAddressPrefix.Length..]));

        Assert.Contains($"\n### `{obstacle}`\n", entry.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
