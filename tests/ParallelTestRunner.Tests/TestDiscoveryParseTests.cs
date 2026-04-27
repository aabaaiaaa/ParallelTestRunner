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

    [TestMethod]
    public void ParseTestList_PipeDelimited_ParsesCorrectly()
    {
        var result = TestDiscovery.ParseTestList("Ns.Cls.TestA|Ns.Cls.TestB|Ns.Cls.TestC");

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("Ns.Cls.TestA", result[0]);
        Assert.AreEqual("Ns.Cls.TestB", result[1]);
        Assert.AreEqual("Ns.Cls.TestC", result[2]);
    }

    [TestMethod]
    public void ParseTestList_EmptySegments_Filtered()
    {
        var result = TestDiscovery.ParseTestList("Ns.Cls.TestA||Ns.Cls.TestB|");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Ns.Cls.TestA", result[0]);
        Assert.AreEqual("Ns.Cls.TestB", result[1]);
    }

    [TestMethod]
    public void ParseTestList_TrimsWhitespace()
    {
        var result = TestDiscovery.ParseTestList("  Ns.Cls.TestA  | Ns.Cls.TestB ");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Ns.Cls.TestA", result[0]);
        Assert.AreEqual("Ns.Cls.TestB", result[1]);
    }

    [TestMethod]
    public void ParseTestList_Deduplicates()
    {
        var result = TestDiscovery.ParseTestList("Ns.Cls.TestA|Ns.Cls.TestA|Ns.Cls.TestB");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Ns.Cls.TestA", result[0]);
        Assert.AreEqual("Ns.Cls.TestB", result[1]);
    }

    [TestMethod]
    public void ParseTestList_NullInput_ReturnsEmpty()
    {
        var result = TestDiscovery.ParseTestList(null);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseTestList_EmptyString_ReturnsEmpty()
    {
        var result = TestDiscovery.ParseTestList("");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseTestList_WhitespaceOnly_ReturnsEmpty()
    {
        var result = TestDiscovery.ParseTestList("   ");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ValidateTestList_ValidFqn_ReturnsNoFailures()
    {
        var failures = TestDiscovery.ValidateTestList(["Ns.Class.Method"]);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void ValidateTestList_DeepNamespace_ReturnsNoFailures()
    {
        var failures = TestDiscovery.ValidateTestList(["A.B.C.D.E.Method"]);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void ValidateTestList_UnderscoresAndDigits_ReturnsNoFailures()
    {
        var failures = TestDiscovery.ValidateTestList(["_Foo.Bar_.Test_1"]);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void ValidateTestList_VstestFilterExpression_ReturnsFailure()
    {
        var failures = TestDiscovery.ValidateTestList(["(FullNameMatchesRegex '(\\.X$)')"]);
        Assert.AreEqual(1, failures.Count);
        Assert.AreEqual(0, failures[0].Index);
    }

    [TestMethod]
    public void ValidateTestList_Parens_ReturnsFailure()
    {
        var failures = TestDiscovery.ValidateTestList(["Ns.Class.Method()"]);
        Assert.AreEqual(1, failures.Count);
        StringAssert.Contains(failures[0].Reason.ToLowerInvariant(), "paren");
    }

    [TestMethod]
    public void ValidateTestList_SingleQuotes_ReturnsFailure()
    {
        var failures = TestDiscovery.ValidateTestList(["'Ns.Class.Method'"]);
        Assert.AreEqual(1, failures.Count);
    }

    [TestMethod]
    public void ValidateTestList_EqualsSign_ReturnsFailure()
    {
        var failures = TestDiscovery.ValidateTestList(["FullyQualifiedName=Ns.Class.M"]);
        Assert.AreEqual(1, failures.Count);
    }

    [TestMethod]
    public void ValidateTestList_RegexAnchor_ReturnsFailure()
    {
        var failures = TestDiscovery.ValidateTestList(["Ns.Class.Method$"]);
        Assert.AreEqual(1, failures.Count);
    }

    [TestMethod]
    public void ValidateTestList_Spaces_ReturnsFailure()
    {
        var failures = TestDiscovery.ValidateTestList(["Ns Class Method"]);
        Assert.AreEqual(1, failures.Count);
    }

    [TestMethod]
    public void ValidateTestList_BareIdentifierNoDots_ReturnsFailure()
    {
        var failures = TestDiscovery.ValidateTestList(["MyTest"]);
        Assert.AreEqual(1, failures.Count);
        StringAssert.Contains(failures[0].Reason.ToLowerInvariant(), "dot");
    }

    [TestMethod]
    public void ValidateTestList_LeadingDigit_ReturnsFailure()
    {
        var failures = TestDiscovery.ValidateTestList(["1Ns.Class.Method"]);
        Assert.AreEqual(1, failures.Count);
    }

    [TestMethod]
    public void ValidateTestList_PartialFailures_ReportsOnlyBadOnes()
    {
        var failures = TestDiscovery.ValidateTestList(["Good.Class.M", "bad input", "Also.Good.M"]);
        Assert.AreEqual(1, failures.Count);
        Assert.AreEqual(1, failures[0].Index);
        Assert.AreEqual("bad input", failures[0].Segment);
    }
}
