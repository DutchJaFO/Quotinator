using Quotinator.Core.Models;
using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;

namespace Quotinator.Core.Tests.Models;

/// <summary>
/// #490: a Reset's response says why it set a file aside rather than backing it up, as its notifications do.
/// A reason that stops a backup is reported to whoever ran the Reset, never left in the log alone.
/// </summary>
[TestClass]
public class RestorePointDtoTests
{
    private const string SetAsideName = "quotinatordata_unreadable.db";

    private static readonly DatabaseCheckLayerResult BrokenIndex = new()
    {
        Layer = DatabaseCheckLayer.Indexes,
        Status = DatabaseCheckLayerStatus.Failed,
        Finding = "row 5 missing from index IX_CheckFixture_Rank",
    };

    [TestMethod]
    public void From_SetAsideByTheCheck_CarriesTheLayerStatusAndFinding()
    {
        RestorePointDto dto = RestorePointDto.From(RestorePointResult.SetAside(SetAsideName, SetAsideReasonResult.FromCheck(BrokenIndex)))!;

        Assert.AreEqual(BrokenIndex.Layer, dto.CheckLayer);
        Assert.AreEqual(BrokenIndex.Status, dto.CheckStatus);
        Assert.AreEqual(BrokenIndex.Finding, dto.CheckFinding);
        Assert.IsNull(dto.BackupError, "a file the check set aside carries a backup error");
    }

    [TestMethod]
    public void From_SetAsideByTheBackup_CarriesTheBackupError()
    {
        const string backupError = "SQLite Error 26: 'file is not a database'.";

        RestorePointDto dto = RestorePointDto.From(RestorePointResult.SetAside(SetAsideName, SetAsideReasonResult.FromBackup(backupError)))!;

        Assert.AreEqual(backupError, dto.BackupError);
        Assert.IsNull(dto.CheckLayer, "a file the backup set aside carries a check layer");
    }

    /// <summary>Control: a backup taken has no set-aside reason to report.</summary>
    [TestMethod]
    public void From_BackedUp_CarriesNoReason()
    {
        RestorePointDto dto = RestorePointDto.From(RestorePointResult.BackedUp("backup.db"))!;

        Assert.IsNull(dto.CheckLayer);
        Assert.IsNull(dto.BackupError);
    }
}
