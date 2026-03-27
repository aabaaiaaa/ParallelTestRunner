using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class ResultCollatorTests
{
    [TestMethod]
    public void Collate_AllBatchesPass_Returns0()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 0),
            new BatchResult(1, 10, 0),
            new BatchResult(2, 5, 0),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public void Collate_OneBatchFails_Returns1()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 0),
            new BatchResult(1, 10, 1),
            new BatchResult(2, 5, 0),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_AllBatchesFail_Returns1()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 1),
            new BatchResult(1, 10, 1),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_SinglePassingBatch_Returns0()
    {
        var results = new[]
        {
            new BatchResult(0, 25, 0),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public void Collate_NonZeroExitCode_TreatedAsFailure()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 0),
            new BatchResult(1, 10, 2),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_WithHangDetectionResults_Returns1()
    {
        var results = new[]
        {
            new BatchResult(0, 10, -1, TimedOut: true),
        };

        var retryResult = new RetryResult(
            HangingTests: new[] { "HangingTest1", "HangingTest2" },
            SuspectedHangingTests: Array.Empty<string>(),
            PersistentFailures: Array.Empty<string>(),
            RetryRoundsPerformed: 5);

        var exitCode = ResultCollator.Collate(results, retryResult);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_HangDetectionNoHangingTests_Returns1WhenTimedOut()
    {
        var results = new[]
        {
            new BatchResult(0, 10, -1, TimedOut: true),
        };

        var retryResult = new RetryResult(
            HangingTests: Array.Empty<string>(),
            SuspectedHangingTests: Array.Empty<string>(),
            PersistentFailures: Array.Empty<string>(),
            RetryRoundsPerformed: 3);

        var exitCode = ResultCollator.Collate(results, retryResult);

        // Still fails because the batch timed out (exit code -1 != 0)
        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_HangingTests_AllBatchesPassAfterRetry_Returns1()
    {
        // All batches marked as passing (retries recovered non-hanging tests),
        // but confirmed hanging tests exist — should still return 1.
        var results = new[]
        {
            new BatchResult(0, 10, 0),
            new BatchResult(1, 10, 0),
        };

        var retryResult = new RetryResult(
            HangingTests: new[] { "HangingTest" },
            SuspectedHangingTests: Array.Empty<string>(),
            PersistentFailures: Array.Empty<string>(),
            RetryRoundsPerformed: 1);

        var exitCode = ResultCollator.Collate(results, retryResult);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_PersistentFailures_AllBatchesPassAfterRetry_Returns1()
    {
        // All batches marked as passing, but persistent failures exist — should return 1.
        var results = new[]
        {
            new BatchResult(0, 10, 0),
        };

        var retryResult = new RetryResult(
            HangingTests: Array.Empty<string>(),
            SuspectedHangingTests: Array.Empty<string>(),
            PersistentFailures: new[] { "FailingTest" },
            RetryRoundsPerformed: 2);

        var exitCode = ResultCollator.Collate(results, retryResult);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_NoRetryResult_AllBatchesPass_Returns0()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 0),
        };

        var exitCode = ResultCollator.Collate(results, retryResult: null);

        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public void Collate_RetryResult_NoIssues_AllBatchesPass_Returns0()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 0),
        };

        var retryResult = new RetryResult(
            HangingTests: Array.Empty<string>(),
            SuspectedHangingTests: Array.Empty<string>(),
            PersistentFailures: Array.Empty<string>(),
            RetryRoundsPerformed: 1);

        var exitCode = ResultCollator.Collate(results, retryResult);

        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public void TeamCityEscape_EscapesSpecialCharacters()
    {
        Assert.AreEqual("test||value", ResultCollator.TeamCityEscape("test|value"));
        Assert.AreEqual("test|'value", ResultCollator.TeamCityEscape("test'value"));
        Assert.AreEqual("test|[value|]", ResultCollator.TeamCityEscape("test[value]"));
        Assert.AreEqual("line1|nline2", ResultCollator.TeamCityEscape("line1\nline2"));
        Assert.AreEqual("line1|rline2", ResultCollator.TeamCityEscape("line1\rline2"));
    }

    [TestMethod]
    public void TeamCityEscape_NoSpecialChars_ReturnsUnchanged()
    {
        Assert.AreEqual("Ns.Class.Method", ResultCollator.TeamCityEscape("Ns.Class.Method"));
    }
}
