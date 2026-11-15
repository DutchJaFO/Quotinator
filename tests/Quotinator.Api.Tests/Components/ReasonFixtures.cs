using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Components;

/// <summary>
/// #490: the reasons a Reset sets a file aside, as the check and the backup report them, shared by the tests
/// of every surface that shows one.
/// </summary>
internal static class ReasonFixtures
{
    /// <summary>A check layer that showed a file was not a sound copy.</summary>
    internal static readonly DatabaseCheckLayerResult BrokenIndex = new()
    {
        Layer = DatabaseCheckLayer.Indexes,
        Status = DatabaseCheckLayerStatus.Failed,
        Finding = "row 5 missing from index IX_CheckFixture_Rank",
    };

    /// <summary>The backup's own error on a file only the backup copy found unreadable.</summary>
    internal const string BackupError = "SQLite Error 26: 'file is not a database'.";
}
