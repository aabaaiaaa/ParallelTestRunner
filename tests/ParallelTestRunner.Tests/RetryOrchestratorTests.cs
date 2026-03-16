using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class RetryOrchestratorTests
{
    private static readonly Options DefaultOptions = new(
        ProjectPath: "dummy.csproj",
        BatchSize: 50,
        Retries: 2,
        IdleTimeout: TimeSpan.FromSeconds(5));

    [TestMethod]
    public async Task NoFailures_ReturnsEmptyResult()
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

        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions, FakeRunAll(), CancellationToken.None);

        Assert.AreEqual(0, result.HangingTests.Count);
        Assert.AreEqual(0, result.SuspectedHangingTests.Count);
    }

    [TestMethod]
    public async Task FailedBatch_RetriesAndRecovers()
    {
        var results = new[]
        {
            new BatchResult(0, 3, 1), // failed
            new BatchResult(1, 3, 0), // passed
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC" },
            new[] { "TestD", "TestE", "TestF" },
        };

        // Fake runner: always passes on retry
        var callCount = 0;
        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions,
            (retryBatches, opts, ct) =>
            {
                callCount++;
                var retryResults = retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 0)).ToArray();
                return Task.FromResult(retryResults);
            },
            CancellationToken.None);

        Assert.AreEqual(0, result.HangingTests.Count);
        Assert.IsTrue(callCount >= 1);
        Assert.AreEqual(0, results[0].ExitCode, "Original batch should be updated to passing");
    }

    [TestMethod]
    public async Task TimedOutBatch_ExtractsSuspectedHanger_RetriesRest()
    {
        // Batch with 4 tests, times out. Output shows TestA passed, TestB is the hanger.
        var capturedOutput = new List<string>
        {
            "  Passed TestA [1s]",
        };
        var results = new[]
        {
            new BatchResult(0, 4, -1, TimedOut: true, CapturedOutput: capturedOutput),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC", "TestD" },
        };

        var retryBatchTests = new List<List<string>>();
        var hangerBatchTests = new List<List<string>>();
        var callNum = 0;

        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions with { Retries = 1 },
            (retryBatches, opts, ct) =>
            {
                callNum++;
                if (callNum == 1)
                {
                    // First call: retry pool (TestC, TestD — not TestB which is suspected)
                    foreach (var b in retryBatches)
                        retryBatchTests.Add(b.ToList());
                    return Task.FromResult(retryBatches.Select((b, i) =>
                        new BatchResult(i, b.Count, 0)).ToArray());
                }
                else
                {
                    // Second call: solo hanger test (TestB)
                    foreach (var b in retryBatches)
                        hangerBatchTests.Add(b.ToList());
                    // TestB times out solo → confirmed hanger
                    return Task.FromResult(retryBatches.Select((b, i) =>
                        new BatchResult(i, b.Count, -1, TimedOut: true)).ToArray());
                }
            },
            CancellationToken.None);

        // TestB should NOT be in the retry pool
        var allRetried = retryBatchTests.SelectMany(b => b).ToList();
        CollectionAssert.DoesNotContain(allRetried, "TestB");
        // TestC and TestD should be retried
        CollectionAssert.Contains(allRetried, "TestC");
        CollectionAssert.Contains(allRetried, "TestD");
        // TestA passed, should not be retried
        CollectionAssert.DoesNotContain(allRetried, "TestA");

        // TestB confirmed as hanger
        Assert.AreEqual(1, result.HangingTests.Count);
        Assert.AreEqual("TestB", result.HangingTests[0]);
    }

    [TestMethod]
    public async Task SuspectedHanger_PassesSolo_NotReported()
    {
        var capturedOutput = new List<string>
        {
            "  Passed TestA [1s]",
        };
        var results = new[]
        {
            new BatchResult(0, 2, -1, TimedOut: true, CapturedOutput: capturedOutput),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB" },
        };

        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions with { Retries = 1 },
            (retryBatches, opts, ct) =>
            {
                // Everything passes
                return Task.FromResult(retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 0)).ToArray());
            },
            CancellationToken.None);

        Assert.AreEqual(0, result.HangingTests.Count);
        Assert.AreEqual(0, result.SuspectedHangingTests.Count);
    }

    [TestMethod]
    public async Task ConfirmedHanger_ExcludedFromFutureRounds()
    {
        var capturedOutput = new List<string>
        {
            "  Passed TestA [1s]",
        };
        var results = new[]
        {
            new BatchResult(0, 3, -1, TimedOut: true, CapturedOutput: capturedOutput),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC" },
        };

        var allRetriedTests = new List<List<string>>();
        var callNum = 0;

        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions with { Retries = 3 },
            (retryBatches, opts, ct) =>
            {
                callNum++;
                foreach (var b in retryBatches)
                    allRetriedTests.Add(b.ToList());

                if (retryBatches.Any(b => b.Count == 1 && b[0] == "TestB"))
                {
                    // Solo hanger test → times out
                    return Task.FromResult(retryBatches.Select((b, i) =>
                        b.Count == 1 && b[0] == "TestB"
                            ? new BatchResult(i, b.Count, -1, TimedOut: true)
                            : new BatchResult(i, b.Count, 0)).ToArray());
                }

                // Regular retries pass
                return Task.FromResult(retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 0)).ToArray());
            },
            CancellationToken.None);

        // TestB should be confirmed as hanging
        CollectionAssert.Contains(result.HangingTests.ToList(), "TestB");

        // After confirmation, TestB should not appear in any subsequent retry batches
        // (only in the solo test batch)
        var afterConfirmation = false;
        foreach (var batch in allRetriedTests)
        {
            if (batch.Count == 1 && batch[0] == "TestB")
            {
                afterConfirmation = true;
                continue;
            }
            if (afterConfirmation)
            {
                CollectionAssert.DoesNotContain(batch, "TestB",
                    "Confirmed hanger should not appear in subsequent retry batches");
            }
        }
    }

    [TestMethod]
    public async Task AutoRetry_StopsWhenNoProgress()
    {
        var results = new[]
        {
            new BatchResult(0, 3, 1), // persistent failure
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC" },
        };

        var callCount = 0;
        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions with { AutoRetry = true, Retries = 0 },
            (retryBatches, opts, ct) =>
            {
                callCount++;
                // Always fails
                return Task.FromResult(retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 1)).ToArray());
            },
            CancellationToken.None);

        // Should stop after detecting no progress (2 rounds: first fails, second fails with no improvement)
        Assert.IsTrue(callCount <= 3, $"Expected at most 3 calls but got {callCount}");
    }

    [TestMethod]
    public async Task FixedRetryCap_Respected()
    {
        var results = new[]
        {
            new BatchResult(0, 3, 1), // persistent failure
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC" },
        };

        var callCount = 0;
        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions with { Retries = 2, AutoRetry = false },
            (retryBatches, opts, ct) =>
            {
                callCount++;
                // Always fails
                return Task.FromResult(retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 1)).ToArray());
            },
            CancellationToken.None);

        Assert.AreEqual(2, result.RetryRoundsPerformed);
    }

    [TestMethod]
    public async Task ParseTimedOutOutput_IdentifiesCompletedAndSuspected()
    {
        var output = new List<string>
        {
            "Build started...",
            "  Passed TestA [1s]",
            "  Passed TestB [2s]",
            "  Failed TestC [500ms]",
            "Some other line",
        };
        var batchTests = new List<string> { "TestA", "TestB", "TestC", "TestD", "TestE" };

        var (passed, failed, suspected) = RetryOrchestrator.ParseTimedOutOutput(output, batchTests);

        CollectionAssert.Contains(passed, "TestA");
        CollectionAssert.Contains(passed, "TestB");
        CollectionAssert.Contains(failed, "TestC");
        Assert.AreEqual("TestD", suspected, "First unaccounted test should be the suspected hanger");
    }

    [TestMethod]
    public async Task ParseTimedOutOutput_NullOutput_FirstTestIsSuspected()
    {
        var batchTests = new List<string> { "TestA", "TestB" };

        var (passed, failed, suspected) = RetryOrchestrator.ParseTimedOutOutput(null, batchTests);

        Assert.AreEqual(0, passed.Count);
        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("TestA", suspected);
    }

    /// <summary>
    /// Creates a fake RunAll that passes everything by default.
    /// </summary>
    private static Func<IReadOnlyList<IReadOnlyList<string>>, Options, CancellationToken, Task<BatchResult[]>> FakeRunAll(
        HashSet<string>? hangingTests = null)
    {
        hangingTests ??= [];
        return (batches, opts, ct) =>
        {
            var results = batches.Select((b, i) =>
            {
                var anyHanging = b.Any(t => hangingTests.Contains(t));
                return anyHanging
                    ? new BatchResult(i, b.Count, -1, TimedOut: true)
                    : new BatchResult(i, b.Count, 0);
            }).ToArray();
            return Task.FromResult(results);
        };
    }
}
