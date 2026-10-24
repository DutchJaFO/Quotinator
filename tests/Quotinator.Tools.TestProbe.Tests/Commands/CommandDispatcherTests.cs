using Quotinator.Tools.TestProbe.Commands;

namespace Quotinator.Tools.TestProbe.Tests.Commands;

[TestClass]
public sealed class CommandDispatcherTests
{
    [TestMethod]
    public void Run_JsonExpectationHolds_ExitsZero()
        => Assert.AreEqual(0, Run(["json", "--equals", "status=unhealthy"], """{"status":"unhealthy"}""").ExitCode);

    [TestMethod]
    public void Run_JsonExpectationFails_ExitsOne()
        => Assert.AreEqual(1, Run(["json", "--equals", "status=healthy"], """{"status":"unhealthy"}""").ExitCode);

    [TestMethod]
    public void Run_JsonExpectationFails_NamesItOnStandardError()
        => Assert.Contains("status", Run(["json", "--equals", "status=healthy"], """{"status":"unhealthy"}""").Error);

    [TestMethod]
    public void Run_JsonExpectationHolds_PrintsTheObservedValue()
        => Assert.Contains("locked", Run(["json", "--present", "reason"], """{"reason":"locked"}""").Output);

    /// <summary>PowerShell 5.1 prefixes a byte-order mark when it pipes a string into a native process.</summary>
    [TestMethod]
    public void Run_JsonPipedWithAByteOrderMark_IsReadAsJson()
        => Assert.AreEqual(0, Run(["json", "--equals", "status=unhealthy"], "﻿{\"status\":\"unhealthy\"}").ExitCode);

    [TestMethod]
    public void Run_JsonFromFile_HoldsAgainstTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "testprobe-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"status":"unhealthy"}""");

        try
        {
            Assert.AreEqual(0, Run(["json", "--file", path, "--equals", "status=unhealthy"], string.Empty).ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Run_JsonFromFile_FailsAgainstTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "testprobe-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"status":"unhealthy"}""");

        try
        {
            Assert.AreEqual(1, Run(["json", "--file", path, "--equals", "status=healthy"], string.Empty).ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Run_JsonFromAMissingFile_ExitsOne()
    {
        string path = Path.Combine(Path.GetTempPath(), "testprobe-absent-" + Guid.NewGuid().ToString("N") + ".json");

        Assert.AreEqual(1, Run(["json", "--file", path, "--present", "status"], string.Empty).ExitCode);
    }

    [TestMethod]
    public void Run_JsonUnreadableOption_ExitsOne()
        => Assert.AreEqual(1, Run(["json", "--equals", "status"], """{"status":"x"}""").ExitCode);

    [TestMethod]
    public void Run_UnknownCommand_ExitsOne()
        => Assert.AreEqual(1, Run(["jsn"], string.Empty).ExitCode);

    [TestMethod]
    public void Run_NoCommand_ExitsOne()
        => Assert.AreEqual(1, Run([], string.Empty).ExitCode);

    [TestMethod]
    public void Run_FileCheckHolds_ExitsZero()
        => Assert.AreEqual(0, Run(["file", "--expect-directory", Path.GetTempPath()], string.Empty).ExitCode);

    [TestMethod]
    public void Run_FileCheckFails_ExitsOne()
    {
        string absent = Path.Combine(Path.GetTempPath(), "testprobe-absent-" + Guid.NewGuid().ToString("N"));

        Assert.AreEqual(1, Run(["file", "--expect-directory", absent], string.Empty).ExitCode);
    }

    [TestMethod]
    public void Run_FileWithoutAnyOperation_ExitsOne()
        => Assert.AreEqual(1, Run(["file"], string.Empty).ExitCode);

    [TestMethod]
    public void Run_WaitFieldWithoutUrl_ExitsOne()
        => Assert.AreEqual(1, Run(["wait-field", "--field", "status", "--in", "healthy"], string.Empty).ExitCode);

    [TestMethod]
    public void Run_LogWithoutContainer_ExitsOne()
        => Assert.AreEqual(1, Run(["log", "--pattern", "x", "--at-least", "1"], string.Empty).ExitCode);

    private static (int ExitCode, string Output, string Error) Run(string[] args, string stdin)
    {
        using StringReader input = new(stdin);
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = CommandDispatcher.Run(args, input, output, error);

        return (exitCode, output.ToString(), error.ToString());
    }
}
