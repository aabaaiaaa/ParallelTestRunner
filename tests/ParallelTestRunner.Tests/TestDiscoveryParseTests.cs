using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class TestDiscoveryParseTests
{
    [TestMethod]
    public void ParseDiscoveryOutput_ExtractsAndTrimsTestNames()
    {
        var lines = new List<string>
        {
            "Namespace.Class.TestA",
            "Namespace.Class.TestB",
            "Namespace.Class.TestC",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("Namespace.Class.TestA", result[0]);
        Assert.AreEqual("Namespace.Class.TestB", result[1]);
        Assert.AreEqual("Namespace.Class.TestC", result[2]);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_EmptyLines_ReturnsEmpty()
    {
        var result = TestDiscovery.ParseDiscoveryOutput([]);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_BlankLinesOnly_ReturnsEmpty()
    {
        var lines = new List<string> { "", "   ", "" };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_SkipsBlankLines()
    {
        var lines = new List<string>
        {
            "",
            "Ns.Cls.TestA",
            "   ",
            "Ns.Cls.TestB",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_DeduplicatesIdenticalNames()
    {
        var lines = new List<string>
        {
            "Ns.Cls.TestA",
            "Ns.Cls.TestA",
            "Ns.Cls.TestB",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Ns.Cls.TestA", result[0]);
        Assert.AreEqual("Ns.Cls.TestB", result[1]);
    }

    [TestMethod]
    public void ParseDiscoveryOutput_TrimsWhitespace()
    {
        var lines = new List<string>
        {
            "  Ns.Cls.TestA  ",
            "Ns.Cls.TestB",
        };

        var result = TestDiscovery.ParseDiscoveryOutput(lines);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Ns.Cls.TestA", result[0]);
        Assert.AreEqual("Ns.Cls.TestB", result[1]);
    }
}
