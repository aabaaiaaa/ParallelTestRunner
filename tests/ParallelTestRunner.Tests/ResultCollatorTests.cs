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
}
