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

    [TestMethod]
    public void ParseTestListFile_NewlineSeparated_Parses()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "A.B.C\nD.E.F\n");
            var result = TestDiscovery.ParseTestListFile(path);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("A.B.C", result[0]);
            Assert.AreEqual("D.E.F", result[1]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ParseTestListFile_PipeSeparated_Parses()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "A.B.C|D.E.F");
            var result = TestDiscovery.ParseTestListFile(path);
            Assert.AreEqual(2, result.Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ParseTestListFile_MixedDelimiters_Parses()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "A.B.C|D.E.F\nG.H.I");
            var result = TestDiscovery.ParseTestListFile(path);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("A.B.C", result[0]);
            Assert.AreEqual("D.E.F", result[1]);
            Assert.AreEqual("G.H.I", result[2]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ParseTestListFile_TrimsWhitespace()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "  A.B.C  \n  D.E.F  ");
            var result = TestDiscovery.ParseTestListFile(path);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("A.B.C", result[0]);
            Assert.AreEqual("D.E.F", result[1]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ParseTestListFile_HandlesUtf8Bom()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
            bytes.AddRange(System.Text.Encoding.UTF8.GetBytes("A.B.C\nD.E.F"));
            File.WriteAllBytes(path, bytes.ToArray());
            var result = TestDiscovery.ParseTestListFile(path);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("A.B.C", result[0]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ParseTestListFile_Deduplicates()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "A.B.C\nA.B.C\nD.E.F");
            var result = TestDiscovery.ParseTestListFile(path);
            Assert.AreEqual(2, result.Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ParseTestListFile_SkipsEmptySegments()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "A.B.C\n\n||D.E.F\n   \n");
            var result = TestDiscovery.ParseTestListFile(path);
            Assert.AreEqual(2, result.Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [ExpectedException(typeof(FileNotFoundException))]
    public void ParseTestListFile_MissingFile_Throws()
    {
        TestDiscovery.ParseTestListFile(Path.Combine(Path.GetTempPath(), "definitely_not_a_real_file_" + Guid.NewGuid() + ".txt"));
    }
}
