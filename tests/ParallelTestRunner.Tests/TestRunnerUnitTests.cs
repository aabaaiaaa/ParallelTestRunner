using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class TestRunnerUnitTests
{
    private static readonly Options DefaultOptions = new(
        ProjectPath: "MyTests.csproj",
        BatchSize: 50,
        ResultsDirectory: "C:/default-results",
        IdleTimeout: TimeSpan.FromSeconds(60));

    [TestMethod]
    public void BuildArguments_IncludesBasicArgs()
    {
        var args = TestRunner.BuildArguments(DefaultOptions, "FullyQualifiedName=Test1", 0);

        Assert.IsTrue(args.Contains("test"));
        Assert.IsTrue(args.Contains("MyTests.csproj"));
        Assert.IsTrue(args.Contains("--no-build"));
        Assert.IsTrue(args.Contains("-v normal"));
        Assert.IsTrue(args.Contains("--filter"));
        Assert.IsTrue(args.Contains("FullyQualifiedName=Test1"));
    }

    [TestMethod]
    public void BuildArguments_IncludesCustomLogger()
    {
        var args = TestRunner.BuildArguments(DefaultOptions, "FullyQualifiedName=Test1", 0);

        Assert.IsTrue(args.Contains("--test-adapter-path"));
        Assert.IsTrue(args.Contains("--logger"));
        Assert.IsTrue(args.Contains("ParallelTestRunner"));
    }

    [TestMethod]
    public void BuildArguments_IncludesWorkersForAllFrameworks()
    {
        var args = TestRunner.BuildArguments(DefaultOptions, "FullyQualifiedName=Test1", 0);

        Assert.IsTrue(args.Contains("MSTest.Parallelize.Workers=4"));
        Assert.IsTrue(args.Contains("xUnit.MaxParallelThreads=4"));
        Assert.IsTrue(args.Contains("NUnit.NumberOfTestWorkers=4"));
    }

    [TestMethod]
    public void BuildArguments_CustomWorkers_UsesSpecifiedValue()
    {
        var options = DefaultOptions with { Workers = 2 };
        var args = TestRunner.BuildArguments(options, "FullyQualifiedName=Test1", 0);

        Assert.IsTrue(args.Contains("MSTest.Parallelize.Workers=2"));
        Assert.IsTrue(args.Contains("xUnit.MaxParallelThreads=2"));
        Assert.IsTrue(args.Contains("NUnit.NumberOfTestWorkers=2"));
    }

    [TestMethod]
    public void BuildArguments_AlwaysIncludesTrxLogger()
    {
        var args = TestRunner.BuildArguments(DefaultOptions, "FullyQualifiedName=Test1", 3);

        Assert.IsTrue(args.Contains("trx;LogFileName=batch_3.trx"));
        Assert.IsTrue(args.Contains("--results-directory"));
        Assert.IsTrue(args.Contains("C:/default-results"));
    }

    [TestMethod]
    public void BuildArguments_IncludesCustomResultsDir()
    {
        var options = DefaultOptions with { ResultsDirectory = "C:/custom-results" };
        var args = TestRunner.BuildArguments(options, "FullyQualifiedName=Test1", 0);

        Assert.IsTrue(args.Contains("C:/custom-results"));
    }

    [TestMethod]
    public void BuildArguments_IncludesExtraArgs()
    {
        var options = DefaultOptions with { ExtraDotnetTestArgs = new[] { "--configuration", "Release" } };
        var args = TestRunner.BuildArguments(options, "FullyQualifiedName=Test1", 0);

        Assert.IsTrue(args.Contains("--configuration"));
        Assert.IsTrue(args.Contains("Release"));
    }

    [TestMethod]
    public void BuildArguments_QuotesPathsWithSpaces()
    {
        var options = DefaultOptions with { ProjectPath = "C:/My Projects/Tests.csproj" };
        var args = TestRunner.BuildArguments(options, "FullyQualifiedName=Test1", 0);

        Assert.IsTrue(args.Contains("\"C:/My Projects/Tests.csproj\""));
    }

    [TestMethod]
    public void Quote_NoSpecialChars_ReturnsUnquoted()
    {
        Assert.AreEqual("simple", TestRunner.Quote("simple"));
    }

    [TestMethod]
    public void Quote_WithSpaces_ReturnsQuoted()
    {
        Assert.AreEqual("\"has spaces\"", TestRunner.Quote("has spaces"));
    }

    [TestMethod]
    public void Quote_WithPipe_ReturnsQuoted()
    {
        Assert.AreEqual("\"a|b\"", TestRunner.Quote("a|b"));
    }

    [TestMethod]
    public void Quote_WithQuotes_EscapesAndQuotes()
    {
        Assert.AreEqual("\"has \\\"quotes\\\"\"", TestRunner.Quote("has \"quotes\""));
    }

    [TestMethod]
    public void ValidateLoggerOutput_WithPtrLines_ReturnsTrue()
    {
        var results = new[]
        {
            new BatchResult(0, 5, 0, CapturedOutput: new List<string>
            {
                "##ptr[Passed|FQN=Ns.Test|Name=Test]",
                "  Passed Test [1s]",
            }),
        };

        Assert.IsTrue(TestRunner.ValidateLoggerOutput(results));
    }

    [TestMethod]
    public void ValidateLoggerOutput_TestResultsButNoPtrLines_ReturnsFalse()
    {
        var results = new[]
        {
            new BatchResult(0, 5, 1, CapturedOutput: new List<string>
            {
                "  Passed Test [1s]",
                "  Failed OtherTest [2s]",
            }),
        };

        Assert.IsFalse(TestRunner.ValidateLoggerOutput(results));
    }

    [TestMethod]
    public void ValidateLoggerOutput_NullOutput_ReturnsTrue()
    {
        var results = new[]
        {
            new BatchResult(0, 5, -1, TimedOut: true, CapturedOutput: null),
        };

        Assert.IsTrue(TestRunner.ValidateLoggerOutput(results));
    }

    [TestMethod]
    public void ValidateLoggerOutput_EmptyOutput_ReturnsTrue()
    {
        var results = new[]
        {
            new BatchResult(0, 5, -1, TimedOut: true, CapturedOutput: new List<string>()),
        };

        Assert.IsTrue(TestRunner.ValidateLoggerOutput(results));
    }

    [TestMethod]
    public void ValidateLoggerOutput_NoTestResults_ReturnsTrue()
    {
        var results = new[]
        {
            new BatchResult(0, 5, -1, TimedOut: true, CapturedOutput: new List<string>
            {
                "Build started...",
                "Test run for something.dll",
            }),
        };

        Assert.IsTrue(TestRunner.ValidateLoggerOutput(results));
    }

    [TestMethod]
    public void ValidateLoggerOutput_MultipleBatches_FirstHasPtrLines_ReturnsTrue()
    {
        var results = new[]
        {
            new BatchResult(0, 5, 0, CapturedOutput: new List<string>
            {
                "##ptr[Passed|FQN=Ns.Test|Name=Test]",
            }),
            new BatchResult(1, 5, 1, CapturedOutput: new List<string>
            {
                "  Failed OtherTest [2s]",
            }),
        };

        Assert.IsTrue(TestRunner.ValidateLoggerOutput(results));
    }

    [TestMethod]
    public void ValidateLoggerOutput_MultipleBatches_NoneHavePtrLines_ReturnsFalse()
    {
        var results = new[]
        {
            new BatchResult(0, 5, 0, CapturedOutput: new List<string>
            {
                "  Passed Test [1s]",
            }),
            new BatchResult(1, 5, 1, CapturedOutput: new List<string>
            {
                "  Failed OtherTest [2s]",
            }),
        };

        Assert.IsFalse(TestRunner.ValidateLoggerOutput(results));
    }
}
