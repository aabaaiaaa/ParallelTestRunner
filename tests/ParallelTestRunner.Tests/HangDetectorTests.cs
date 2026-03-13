using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class HangDetectorTests
{
    [TestMethod]
    public async Task NoTimedOutBatches_ReturnsEmpty()
    {
        var results = new[]
        {
            new BatchResult(0, 5, 0),
            new BatchResult(1, 5, 0),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC", "TestD", "TestE" },
            new[] { "TestF", "TestG", "TestH", "TestI", "TestJ" },
        };

        var detection = await HangDetector.DetectAsync(
            results, batches, FakeRunner(new HashSet<string>()), CancellationToken.None);

        Assert.AreEqual(0, detection.HangingTests.Count);
        Assert.AreEqual(0, detection.RetriesPerformed);
    }

    [TestMethod]
    public async Task SingleTestBatch_TimedOut_IdentifiesHangingTest()
    {
        var hangingTests = new HashSet<string> { "TestHang" };
        var results = new[]
        {
            new BatchResult(0, 1, -1, TimedOut: true),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestHang" },
        };

        var detection = await HangDetector.DetectAsync(
            results, batches, FakeRunner(hangingTests), CancellationToken.None);

        Assert.AreEqual(1, detection.HangingTests.Count);
        Assert.AreEqual("TestHang", detection.HangingTests[0]);
        Assert.IsTrue(detection.RetriesPerformed >= 1);
    }

    [TestMethod]
    public async Task BinarySplit_IdentifiesOneHangingTestFromMany()
    {
        var hangingTests = new HashSet<string> { "TestC" };
        var results = new[]
        {
            new BatchResult(0, 4, -1, TimedOut: true),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC", "TestD" },
        };

        var detection = await HangDetector.DetectAsync(
            results, batches, FakeRunner(hangingTests), CancellationToken.None);

        Assert.AreEqual(1, detection.HangingTests.Count);
        Assert.AreEqual("TestC", detection.HangingTests[0]);
    }

    [TestMethod]
    public async Task BinarySplit_IdentifiesMultipleHangingTests()
    {
        var hangingTests = new HashSet<string> { "TestA", "TestD" };
        var results = new[]
        {
            new BatchResult(0, 4, -1, TimedOut: true),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC", "TestD" },
        };

        var detection = await HangDetector.DetectAsync(
            results, batches, FakeRunner(hangingTests), CancellationToken.None);

        Assert.AreEqual(2, detection.HangingTests.Count);
        CollectionAssert.Contains(detection.HangingTests.ToList(), "TestA");
        CollectionAssert.Contains(detection.HangingTests.ToList(), "TestD");
    }

    [TestMethod]
    public async Task IntermittentHang_NotReportedIfPassesOnRetry()
    {
        // A test that was in a timed-out batch but passes when retried alone
        var hangingTests = new HashSet<string>(); // Nothing actually hangs
        var results = new[]
        {
            new BatchResult(0, 2, -1, TimedOut: true),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB" },
        };

        var detection = await HangDetector.DetectAsync(
            results, batches, FakeRunner(hangingTests), CancellationToken.None);

        Assert.AreEqual(0, detection.HangingTests.Count);
        Assert.IsTrue(detection.RetriesPerformed > 0);
    }

    [TestMethod]
    public async Task MaxDepth_ReportsRemainingTestsAsSuspect()
    {
        // Create a scenario where every split still times out (all tests hang)
        // With 2048 tests and max depth 10, we can't isolate individual tests
        var allTests = Enumerable.Range(1, 2048).Select(i => $"Test{i}").ToList();
        var hangingTests = new HashSet<string>(allTests);

        var results = new[]
        {
            new BatchResult(0, allTests.Count, -1, TimedOut: true),
        };
        var batches = new List<IReadOnlyList<string>> { allTests };

        var detection = await HangDetector.DetectAsync(
            results, batches, FakeRunner(hangingTests), CancellationToken.None);

        // Should still report tests (some at max depth as suspects, some individually identified)
        Assert.IsTrue(detection.HangingTests.Count > 0);
    }

    /// <summary>
    /// Creates a fake batch runner that times out if any test in the batch is in the hanging set.
    /// </summary>
    private static Func<IReadOnlyList<string>, int, CancellationToken, Task<BatchResult>> FakeRunner(
        HashSet<string> hangingTests)
    {
        return (batch, index, ct) =>
        {
            var anyHanging = batch.Any(t => hangingTests.Contains(t));
            var result = anyHanging
                ? new BatchResult(index, batch.Count, -1, TimedOut: true)
                : new BatchResult(index, batch.Count, 0);
            return Task.FromResult(result);
        };
    }
}
