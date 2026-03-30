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
    public async Task TimedOutBatch_ExtractsSuspectedHanger_RescuesRest()
    {
        // Batch with 4 tests, times out. Output shows TestA passed, TestB is the suspected hanger.
        // TestC and TestD never ran. In the unified round, TestC+TestD get rescued and TestB
        // gets tested solo — all in one runAll call.
        var capturedOutput = new List<string>
        {
            "##ptr[Passed|FQN=TestA|Name=Test A Display]",
        };
        var results = new[]
        {
            new BatchResult(0, 4, -1, TimedOut: true, CapturedOutput: capturedOutput),
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC", "TestD" },
        };

        var allBatchTests = new List<List<string>>();

        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions with { Retries = 1 },
            (runBatches, opts, ct) =>
            {
                foreach (var b in runBatches)
                    allBatchTests.Add(b.ToList());

                // Solo hanger (TestB) times out, rescue batches pass
                return Task.FromResult(runBatches.Select((b, i) =>
                    b.Count == 1 && b[0] == "TestB"
                        ? new BatchResult(i, b.Count, -1, TimedOut: true)
                        : new BatchResult(i, b.Count, 0)).ToArray());
            },
            CancellationToken.None);

        var allTests = allBatchTests.SelectMany(b => b).ToList();
        // TestA passed initially, should not appear in any work batch
        CollectionAssert.DoesNotContain(allTests, "TestA");
        // TestC and TestD should be rescued
        CollectionAssert.Contains(allTests, "TestC");
        CollectionAssert.Contains(allTests, "TestD");
        // TestB should be tested solo
        CollectionAssert.Contains(allTests, "TestB");

        // TestB confirmed as hanger
        Assert.AreEqual(1, result.HangingTests.Count);
        Assert.AreEqual("TestB", result.HangingTests[0]);
    }

    [TestMethod]
    public async Task SuspectedHanger_PassesSolo_NotReported()
    {
        var capturedOutput = new List<string>
        {
            "##ptr[Passed|FQN=TestA|Name=Test A Display]",
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
            "##ptr[Passed|FQN=TestA|Name=Test A Display]",
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
        // All 3 tests have confirmed failure output — no rescue needed
        var capturedOutput = new List<string>
        {
            "##ptr[Failed|FQN=TestA|Name=Test A]",
            "##ptr[Failed|FQN=TestB|Name=Test B]",
            "##ptr[Failed|FQN=TestC|Name=Test C]",
        };
        var results = new[]
        {
            new BatchResult(0, 3, 1, CapturedOutput: capturedOutput), // persistent failure
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
                // Always fails with output so tests are classified as failed (not never-ran)
                return Task.FromResult(retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 1, CapturedOutput: b.Select(t =>
                        $"##ptr[Failed|FQN={t}|Name={t}]").ToList())).ToArray());
            },
            CancellationToken.None);

        // Should stop after detecting no progress (2 rounds: first fails, second fails with no improvement)
        Assert.IsTrue(callCount <= 3, $"Expected at most 3 calls but got {callCount}");
    }

    [TestMethod]
    public async Task FixedRetryCap_Respected()
    {
        // All 3 tests have confirmed failure output — no rescue needed
        var capturedOutput = new List<string>
        {
            "##ptr[Failed|FQN=TestA|Name=Test A]",
            "##ptr[Failed|FQN=TestB|Name=Test B]",
            "##ptr[Failed|FQN=TestC|Name=Test C]",
        };
        var results = new[]
        {
            new BatchResult(0, 3, 1, CapturedOutput: capturedOutput), // persistent failure
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
                // Always fails with output so tests are classified as failed (not never-ran)
                return Task.FromResult(retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 1, CapturedOutput: b.Select(t =>
                        $"##ptr[Failed|FQN={t}|Name={t}]").ToList())).ToArray());
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
            "##ptr[Passed|FQN=TestA|Name=Test A]",
            "##ptr[Passed|FQN=TestB|Name=Test B]",
            "##ptr[Failed|FQN=TestC|Name=Test C]",
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

    [TestMethod]
    public async Task FailedBatch_WithCapturedOutput_OnlyRetriesFailedTests()
    {
        // Simulate a non-timed-out failed batch where 2 tests passed and 1 failed.
        // Only the failed test should be retried, not the passed ones.
        var capturedOutput = new List<string>
        {
            "Build started...",
            "##ptr[Passed|FQN=TestA|Name=Test A]",
            "##ptr[Passed|FQN=TestB|Name=Test B]",
            "##ptr[Failed|FQN=TestC|Name=Test C]",
        };
        var results = new[]
        {
            new BatchResult(0, 3, 1, CapturedOutput: capturedOutput), // failed, NOT timed out
            new BatchResult(1, 2, 0), // passed
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC" },
            new[] { "TestD", "TestE" },
        };

        var retriedTests = new List<string>();
        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions with { Retries = 1 },
            (retryBatches, opts, ct) =>
            {
                foreach (var b in retryBatches)
                    retriedTests.AddRange(b);
                return Task.FromResult(retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 0)).ToArray());
            },
            CancellationToken.None);

        // Only TestC (the failed test) should have been retried
        CollectionAssert.Contains(retriedTests, "TestC");
        CollectionAssert.DoesNotContain(retriedTests, "TestA", "Passed test should not be retried");
        CollectionAssert.DoesNotContain(retriedTests, "TestB", "Passed test should not be retried");
        CollectionAssert.DoesNotContain(retriedTests, "TestD", "Test from passing batch should not be retried");
    }

    [TestMethod]
    public async Task FailedBatch_NullCapturedOutput_RescuesAllTests()
    {
        // When CapturedOutput is null, all tests are "never ran" and get a rescue run.
        // The first test is suspected as hanging since it's unaccounted.
        var results = new[]
        {
            new BatchResult(0, 3, 1, CapturedOutput: null), // failed, no output
        };
        var batches = new List<IReadOnlyList<string>>
        {
            new[] { "TestA", "TestB", "TestC" },
        };

        var rescuedTests = new List<string>();
        var result = await RetryOrchestrator.RunAsync(
            results, batches, DefaultOptions with { Retries = 1 },
            (retryBatches, opts, ct) =>
            {
                foreach (var b in retryBatches)
                    rescuedTests.AddRange(b);
                return Task.FromResult(retryBatches.Select((b, i) =>
                    new BatchResult(i, b.Count, 0)).ToArray());
            },
            CancellationToken.None);

        // TestB and TestC should be rescued (TestA is suspected hanger, tested solo)
        CollectionAssert.Contains(rescuedTests, "TestB");
        CollectionAssert.Contains(rescuedTests, "TestC");
        // TestA should be tested solo as suspected hanger
        CollectionAssert.Contains(rescuedTests, "TestA");
    }

    [TestMethod]
    public void ParseTimedOutOutput_SimilarNames_MatchesExactly()
    {
        // When batch contains "Ns.Foo.Bar" and "Ns.Foo.Bar.Baz", ##ptr FQNs
        // must match exactly — "Ns.Foo.Bar.Baz" must not match "Ns.Foo.Bar".
        var output = new List<string>
        {
            "##ptr[Passed|FQN=Ns.Foo.Bar|Name=Bar Display]",
            "##ptr[Failed|FQN=Ns.Foo.Bar.Baz|Name=Baz Display]",
        };
        var batchTests = new List<string> { "Ns.Foo.Bar", "Ns.Foo.Bar.Baz", "Ns.Foo.Qux" };

        var (passed, failed, suspected) = RetryOrchestrator.ParseTimedOutOutput(output, batchTests);

        CollectionAssert.AreEquivalent(new[] { "Ns.Foo.Bar" }, passed);
        CollectionAssert.AreEquivalent(new[] { "Ns.Foo.Bar.Baz" }, failed);
        Assert.AreEqual("Ns.Foo.Qux", suspected);
    }

    [TestMethod]
    public void ParseTimedOutOutput_PtrLogger_MatchesByFqn()
    {
        // ##ptr logger emits the base FQN even for parameterised tests — exact match works
        var output = new List<string>
        {
            "##ptr[Passed|FQN=Ns.Test|Name=Ns.Test (1)]",
        };
        var batchTests = new List<string> { "Ns.Test", "Ns.TestOther" };

        var (passed, failed, suspected) = RetryOrchestrator.ParseTimedOutOutput(output, batchTests);

        CollectionAssert.AreEquivalent(new[] { "Ns.Test" }, passed);
        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("Ns.TestOther", suspected);
    }

    [TestMethod]
    public void ParseTimedOutOutput_NoFallback_DisplayNameLinesIgnored()
    {
        // Display-name lines without ##ptr are ignored — no fallback parsing
        var output = new List<string>
        {
            "  Passed Ns.Test(1) [1s]",
        };
        var batchTests = new List<string> { "Ns.Test", "Ns.TestOther" };

        var (passed, failed, suspected) = RetryOrchestrator.ParseTimedOutOutput(output, batchTests);

        Assert.AreEqual(0, passed.Count, "Display-name lines should not be parsed");
        Assert.AreEqual(0, failed.Count);
        Assert.AreEqual("Ns.Test", suspected, "All tests should be unaccounted without ##ptr lines");
    }

    [TestMethod]
    public void ParseTimedOutOutput_DisplayNameDiffersFromFqn_PtrLoggerMatchesByFqn()
    {
        // The critical scenario: display name is completely different from FQN.
        // ##ptr lines contain the FQN, so matching works even when display names diverge.
        // Display-name lines are present but ignored — only ##ptr lines are parsed.
        var output = new List<string>
        {
            "  Passed Adding two positive numbers returns correct sum [1s]",
            "##ptr[Passed|FQN=DummyTestProject.DisplayNames.DisplayNameTests.Addition_PositiveNumbers_ReturnsSum|Name=Adding two positive numbers returns correct sum]",
            "  Failed This display name test should fail when triggered [2s]",
            "##ptr[Failed|FQN=DummyTestProject.DisplayNames.DisplayNameTests.DisplayName_ConditionalFailure|Name=This display name test should fail when triggered]",
        };
        var batchTests = new List<string>
        {
            "DummyTestProject.DisplayNames.DisplayNameTests.Addition_PositiveNumbers_ReturnsSum",
            "DummyTestProject.DisplayNames.DisplayNameTests.DisplayName_ConditionalFailure",
            "DummyTestProject.DisplayNames.DisplayNameTests.Subtraction_BasicOperation_Works",
        };

        var (passed, failed, suspected) = RetryOrchestrator.ParseTimedOutOutput(output, batchTests);

        CollectionAssert.AreEquivalent(
            new[] { "DummyTestProject.DisplayNames.DisplayNameTests.Addition_PositiveNumbers_ReturnsSum" }, passed);
        CollectionAssert.AreEquivalent(
            new[] { "DummyTestProject.DisplayNames.DisplayNameTests.DisplayName_ConditionalFailure" }, failed);
        Assert.AreEqual("DummyTestProject.DisplayNames.DisplayNameTests.Subtraction_BasicOperation_Works", suspected);
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
