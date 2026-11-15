using Quotinator.Api.Startup;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Startup;

/// <summary>#490: the <c>Quotinator:DatabaseStartupCheck</c> setting names a depth, and anything else checks at Full.</summary>
[TestClass]
public class DatabaseStartupCheckSettingTests
{
    /// <summary>The setting's default is the one <see cref="DatabaseOptions"/> starts with, so the two cannot disagree.</summary>
    [TestMethod]
    public void Default_IsTheDatabaseOptionsDefault()
    {
        DatabaseCheckDepth optionsDefault = new DatabaseOptions { DbPath = "unused.db" }.StartupCheckDepth;

        Assert.AreEqual(DatabaseStartupCheckSetting.Default, optionsDefault);
    }

    /// <summary>Both depths are recognised, whatever their case: the add-on writes them lower case.</summary>
    /// <param name="value">The configured value.</param>
    /// <param name="expected">The depth it names.</param>
    [TestMethod]
    [DataRow("full", DatabaseCheckDepth.Full)]
    [DataRow("Full", DatabaseCheckDepth.Full)]
    [DataRow("FULL", DatabaseCheckDepth.Full)]
    [DataRow("fast", DatabaseCheckDepth.Fast)]
    [DataRow("Fast", DatabaseCheckDepth.Fast)]
    public void Parse_ADepth_IsRecognised(string value, DatabaseCheckDepth expected)
    {
        (DatabaseCheckDepth depth, bool recognised) = DatabaseStartupCheckSetting.Parse(value);

        Assert.AreEqual(expected, depth);
        Assert.IsTrue(recognised);
    }

    /// <summary>An absent or empty setting is the default, and nothing is wrong with it.</summary>
    /// <param name="value">The configured value.</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Parse_Absent_IsTheDefaultAndRecognised(string? value)
    {
        (DatabaseCheckDepth depth, bool recognised) = DatabaseStartupCheckSetting.Parse(value);

        Assert.AreEqual(DatabaseStartupCheckSetting.Default, depth);
        Assert.IsTrue(recognised);
    }

    /// <summary>A value that names no depth checks at Full, the safe answer, and is reported as not recognised.</summary>
    /// <param name="value">The configured value.</param>
    [TestMethod]
    [DataRow("thorough")]
    [DataRow("1")]
    [DataRow("fast-ish")]
    public void Parse_AnythingElse_IsFullAndNotRecognised(string value)
    {
        (DatabaseCheckDepth depth, bool recognised) = DatabaseStartupCheckSetting.Parse(value);

        Assert.AreEqual(DatabaseCheckDepth.Full, depth);
        Assert.IsFalse(recognised);
    }
}
