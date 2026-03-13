using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class TestDiscoveryParseTests
{
    [TestMethod]
    public void ParseDiscoveryOutput_StandardOutput_ExtractsTestNames()
    {
        var lines = new List<string>
        {
            "Build started...",
            "Build completed.",
            "The following Tests are available:",
            "    Namespace.Class.TestA",
            "    Namespace.Class.TestB",
            "    Namespace.Class.TestC",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("Namespace.Class.TestA", result[0]);
        Assert.AreEqual("Namespace.Class.TestB", result[1]);
        Assert.AreEqual("Namespace.Class.TestC", result[2]);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_NoSentinel_ReturnsEmpty()
    {
        var lines = new List<string>
        {
            "Build started...",
            "Build completed.",
            "Some other output",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_EmptyLines_ReturnsEmpty()
    {
        var result = TestDiscovery.ParseDiscoveryOutput([]);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_SentinelButNoTests_ReturnsEmpty()
    {
        var lines = new List<string>
        {
            "The following Tests are available:",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_DeduplicatesParameterisedTests()
    {
        var lines = new List<string>
        {
            "The following Tests are available:",
            "    Ns.Cls.Add(1,2,3)",
            "    Ns.Cls.Add(4,5,9)",
            "    Ns.Cls.Add(0,0,0)",
            "    Ns.Cls.Subtract",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Ns.Cls.Add", result[0]);
        Assert.AreEqual("Ns.Cls.Subtract", result[1]);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_SkipsBlankLinesAfterSentinel()
    {
        var lines = new List<string>
        {
            "The following Tests are available:",
            "",
            "    Ns.Cls.TestA",
            "   ",
            "    Ns.Cls.TestB",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_SentinelWithTrailingWhitespace_StillMatches()
    {
        var lines = new List<string>
        {
            "The following Tests are available:   ",
            "    Ns.Cls.TestA",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Ns.Cls.TestA", result[0]);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_DuplicateNonParameterisedTests_Deduplicates()
    {
        var lines = new List<string>
        {
            "The following Tests are available:",
            "    Ns.Cls.TestA",
            "    Ns.Cls.TestA",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(1, result.Count);
    }
}
