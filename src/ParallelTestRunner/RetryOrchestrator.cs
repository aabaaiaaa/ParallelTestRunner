namespace ParallelTestRunner;

public record RetryResult(
    IReadOnlyList<string> HangingTests,
    IReadOnlyList<string> SuspectedHangingTests,
    IReadOnlyList<string> PersistentFailures,
    IReadOnlyList<string> SlowTests,
    int RetryRoundsPerformed,
    int RescueRoundsPerformed = 0);

/// <summary>
/// Tracks the category of a batch within a work round so results can be
/// classified correctly after execution.
/// </summary>
internal enum WorkKind { Rescue, SoloHanger, Retry }

public static class RetryOrchestrator
{
    /// <summary>
    /// Orchestrates rescue runs, hang detection, and retries in a single unified loop.
    /// All work types (rescue, solo hanger testing, failure retries) are combined into
    /// each round so all parallel slots stay busy at all times.
    /// </summary>
    public static Task<RetryResult> RunAsync(
        BatchResult[] results,
        IReadOnlyList<IReadOnlyList<string>> originalBatches,
        Options options,
        CancellationToken ct)
    {
        return RunAsync(results, originalBatches, options, TestRunner.RunAllAsync, ct);
    }

    /// <summary>
    /// Testable overload accepting a fake runner.
    /// </summary>
    internal static async Task<RetryResult> RunAsync(
        BatchResult[] results,
        IReadOnlyList<IReadOnlyList<string>> originalBatches,
        Options options,
        Func<IReadOnlyList<IReadOnlyList<string>>, Options, CancellationToken, Task<BatchResult[]>> runAll,
        CancellationToken ct)
    {
        var confirmedHangers = new HashSet<string>();
        var suspectedHangers = new HashSet<string>();
        var resolvedTests = new HashSet<string>();
        var persistentFailures = new HashSet<string>();
        var slowTests = new HashSet<string>(); // Tests that only passed with extended timeout

        // Per-test outcome tracking
        var passedTests = new HashSet<string>();
        var failedTests = new HashSet<string>();
        var neverRan = new HashSet<string>();

        // Per-test retry count — only incremented for actual retries, not rescue runs
        var testRetryCount = new Dictionary<string, int>();

        // Parse initial results
        ClassifyBatchResults(results, originalBatches, passedTests, failedTests, suspectedHangers, neverRan);

        var rescueRounds = 0;
        var retryRounds = 0;
        var previousRoundFailures = new HashSet<string>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Build unified work pool from all sources
            var workBatches = new List<IReadOnlyList<string>>();
            var workKinds = new List<WorkKind>();

            // 1. Rescue batches — tests that never got a result
            var rescuePool = neverRan
                .Where(t => !confirmedHangers.Contains(t) && !suspectedHangers.Contains(t))
                .ToList();
            neverRan.Clear();

            if (rescuePool.Count > 0)
            {
                var batchSize = Math.Max(5, rescuePool.Count / Math.Max(1, options.MaxParallelism - suspectedHangers.Count));
                foreach (var batch in TestBatcher.CreateBatches(rescuePool, batchSize))
                {
                    workBatches.Add(batch);
                    workKinds.Add(WorkKind.Rescue);
                }
            }

            // 2. Solo hanger batches — run with extended timeout (3x) to distinguish
            //    truly hanging tests from slow ones. These run concurrently with other work.
            var hangersToTest = suspectedHangers.ToList();
            var hangerBatches = new List<IReadOnlyList<string>>();
            foreach (var hanger in hangersToTest)
                hangerBatches.Add(new[] { hanger });

            // 3. Retry batches — confirmed failures that haven't exhausted retries
            var maxRetries = options.AutoRetry ? 10 : options.Retries;
            var retryPool = failedTests
                .Where(t => !confirmedHangers.Contains(t)
                    && !persistentFailures.Contains(t)
                    && testRetryCount.GetValueOrDefault(t, 0) < maxRetries)
                .ToList();

            if (retryPool.Count > 0)
            {
                var slotsUsed = workBatches.Count + hangerBatches.Count;
                var slotsRemaining = Math.Max(1, options.MaxParallelism - slotsUsed);
                var batchSize = Math.Max(5, retryPool.Count / slotsRemaining);
                foreach (var batch in TestBatcher.CreateBatches(retryPool, batchSize))
                {
                    workBatches.Add(batch);
                    workKinds.Add(WorkKind.Retry);
                }
            }

            // Nothing left to do
            if (workBatches.Count == 0 && hangerBatches.Count == 0)
                break;

            // Log what we're doing
            var hasRescue = workKinds.Any(k => k == WorkKind.Rescue);
            var hasHangers = hangerBatches.Count > 0;
            var hasRetry = workKinds.Any(k => k == WorkKind.Retry);

            if (hasRescue) rescueRounds++;
            if (hasRetry) retryRounds++;

            var parts = new List<string>();
            if (hasRescue) parts.Add($"{rescuePool.Count} rescue");
            if (hasHangers) parts.Add($"{hangersToTest.Count} hanger solo (extended timeout)");
            if (hasRetry) parts.Add($"{retryPool.Count} retry");

            Console.Error.WriteLine();
            Console.Error.WriteLine($"========== Round {rescueRounds + retryRounds} ({string.Join(" + ", parts)}) ==========");
            Console.Error.WriteLine($"  Running {workBatches.Count + hangerBatches.Count} batch(es) across all work types...");

            // Launch normal work and extended-timeout hanger work concurrently
            var normalTask = workBatches.Count > 0
                ? runAll(workBatches, options, ct)
                : Task.FromResult(Array.Empty<BatchResult>());

            var extendedTimeout = options.IdleTimeout > TimeSpan.Zero
                ? options.IdleTimeout * 3
                : TimeSpan.Zero;
            var hangerOptions = options with { IdleTimeout = extendedTimeout };

            var hangerTask = hangerBatches.Count > 0
                ? runAll(hangerBatches, hangerOptions, ct)
                : Task.FromResult(Array.Empty<BatchResult>());

            await Task.WhenAll(normalTask, hangerTask);

            var workResults = normalTask.Result;
            var hangerResults = hangerTask.Result;

            // Process results
            var thisRoundPassed = new HashSet<string>();
            var thisRoundFailed = new HashSet<string>();

            // Process normal work results (rescue + retry)
            for (var j = 0; j < workResults.Length; j++)
            {
                var kind = workKinds[j];
                var batchTests = workBatches[j];
                var batchResult = workResults[j];

                ProcessRunResult(batchResult, batchTests, passedTests, failedTests,
                    suspectedHangers, confirmedHangers, neverRan, thisRoundPassed, thisRoundFailed);

                if (kind == WorkKind.Retry)
                {
                    foreach (var t in batchTests)
                    {
                        if (!thisRoundPassed.Contains(t))
                            testRetryCount[t] = testRetryCount.GetValueOrDefault(t, 0) + 1;
                    }
                }
            }

            // Process solo hanger results
            for (var j = 0; j < hangerResults.Length; j++)
            {
                var test = hangersToTest[j];
                var batchResult = hangerResults[j];

                ProcessSoloHangerResult(test, batchResult, confirmedHangers,
                    suspectedHangers, resolvedTests, passedTests, failedTests, slowTests, extendedTimeout);
            }

            // Remove passed tests from failure tracking
            foreach (var t in thisRoundPassed)
            {
                failedTests.Remove(t);
            }

            // Track persistent failures — tests that failed in consecutive retry rounds
            if (hasRetry && previousRoundFailures.Count > 0)
            {
                var retryFailed = thisRoundFailed.Where(t => retryPool.Contains(t)).ToHashSet();
                var consecutive = retryFailed.Intersect(previousRoundFailures).ToList();
                foreach (var t in consecutive)
                {
                    persistentFailures.Add(t);
                    Console.Error.WriteLine($"  Persistent failure: {t}");
                }
            }
            if (hasRetry)
                previousRoundFailures = thisRoundFailed.Where(t => retryPool.Contains(t)).ToHashSet();

            // Safety: rescue pool made no progress
            if (hasRescue && rescuePool.Count > 0 && neverRan.Count >= rescuePool.Count)
            {
                Console.Error.WriteLine("  Rescue: no progress — remaining tests treated as failed");
                foreach (var t in neverRan)
                    failedTests.Add(t);
                neverRan.Clear();
            }

            // Auto-retry stop condition
            if (hasRetry && options.AutoRetry && thisRoundPassed.Count == 0 && neverRan.Count == 0 && suspectedHangers.Count == 0)
            {
                Console.Error.WriteLine("  Auto-retry: no progress this round — stopping retries");
                break;
            }

            // Log progress
            var totalRemaining = neverRan.Count + suspectedHangers.Count +
                failedTests.Count(t => !confirmedHangers.Contains(t) && !persistentFailures.Contains(t)
                    && testRetryCount.GetValueOrDefault(t, 0) < maxRetries);
            Console.Error.WriteLine($"  {thisRoundPassed.Count} recovered | {totalRemaining} remaining");
        }

        // Update original batch results based on final per-test outcomes
        UpdateOriginalBatchResults(results, originalBatches, passedTests, confirmedHangers, persistentFailures);

        return new RetryResult(
            confirmedHangers.ToList(),
            suspectedHangers.ToList(),
            persistentFailures.ToList(),
            slowTests.ToList(),
            retryRounds,
            rescueRounds);
    }

    private static void ProcessSoloHangerResult(
        string test,
        BatchResult result,
        HashSet<string> confirmedHangers,
        HashSet<string> suspectedHangers,
        HashSet<string> resolvedTests,
        HashSet<string> passedTests,
        HashSet<string> failedTests,
        HashSet<string> slowTests,
        TimeSpan extendedTimeout)
    {
        if (result.TimedOut)
        {
            confirmedHangers.Add(test);
            suspectedHangers.Remove(test);
            failedTests.Remove(test);
            Console.Error.WriteLine($"    Confirmed hanging test (timed out even with {extendedTimeout.TotalSeconds:F0}s timeout): {test}");
        }
        else if (result.ExitCode == 0)
        {
            suspectedHangers.Remove(test);
            resolvedTests.Add(test);
            passedTests.Add(test);
            failedTests.Remove(test);
            slowTests.Add(test);
            Console.Error.WriteLine($"    Test passed with extended timeout ({extendedTimeout.TotalSeconds:F0}s): {test}");
        }
        else
        {
            suspectedHangers.Remove(test);
            failedTests.Add(test);
            Console.Error.WriteLine($"    Test failed (not hanging): {test}");
        }
    }

    private static void ProcessRunResult(
        BatchResult result,
        IReadOnlyList<string> batchTests,
        HashSet<string> passedTests,
        HashSet<string> failedTests,
        HashSet<string> suspectedHangers,
        HashSet<string> confirmedHangers,
        HashSet<string> neverRan,
        HashSet<string> thisRoundPassed,
        HashSet<string> thisRoundFailed)
    {
        if (result.ExitCode == 0 && !result.TimedOut)
        {
            foreach (var t in batchTests)
            {
                passedTests.Add(t);
                thisRoundPassed.Add(t);
            }
            return;
        }

        var (cp, cf, sh) = ParseTimedOutOutput(result.CapturedOutput, batchTests);
        foreach (var t in cp) { passedTests.Add(t); thisRoundPassed.Add(t); }
        foreach (var t in cf) { failedTests.Add(t); thisRoundFailed.Add(t); }

        if (sh is not null && !confirmedHangers.Contains(sh))
        {
            suspectedHangers.Add(sh);
            Console.Error.WriteLine($"  Suspected hanging test: {sh}");
        }

        var accounted = new HashSet<string>(cp);
        accounted.UnionWith(cf);
        if (sh is not null) accounted.Add(sh);

        foreach (var test in batchTests)
        {
            if (!accounted.Contains(test))
            {
                if (result.TimedOut)
                    neverRan.Add(test);
                else
                {
                    failedTests.Add(test);
                    thisRoundFailed.Add(test);
                }
            }
        }
    }

    /// <summary>
    /// Classifies the initial batch results into per-test outcome sets.
    /// </summary>
    private static void ClassifyBatchResults(
        BatchResult[] results,
        IReadOnlyList<IReadOnlyList<string>> originalBatches,
        HashSet<string> passedTests,
        HashSet<string> failedTests,
        HashSet<string> suspectedHangers,
        HashSet<string> neverRan)
    {
        for (var i = 0; i < results.Length; i++)
        {
            var batchTests = originalBatches[results[i].BatchIndex];

            if (results[i].ExitCode == 0)
            {
                foreach (var t in batchTests)
                    passedTests.Add(t);
                continue;
            }

            var (completedPassed, completedFailed, suspectedHanger) =
                ParseTimedOutOutput(results[i].CapturedOutput, batchTests);

            foreach (var t in completedPassed) passedTests.Add(t);
            foreach (var t in completedFailed) failedTests.Add(t);

            if (suspectedHanger is not null)
            {
                suspectedHangers.Add(suspectedHanger);
                Console.Error.WriteLine($"  Suspected hanging test: {suspectedHanger}");
            }

            var accounted = new HashSet<string>(completedPassed);
            accounted.UnionWith(completedFailed);
            if (suspectedHanger is not null) accounted.Add(suspectedHanger);

            foreach (var test in batchTests)
            {
                if (!accounted.Contains(test))
                    neverRan.Add(test);
            }
        }
    }

    /// <summary>
    /// Updates the original batch results array based on final per-test outcomes,
    /// so ResultCollator can report accurately.
    /// </summary>
    private static void UpdateOriginalBatchResults(
        BatchResult[] results,
        IReadOnlyList<IReadOnlyList<string>> originalBatches,
        HashSet<string> passedTests,
        HashSet<string> confirmedHangers,
        HashSet<string> persistentFailures)
    {
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i].ExitCode == 0) continue;

            var batchTests = originalBatches[results[i].BatchIndex];

            var allResolved = batchTests.All(t =>
                passedTests.Contains(t) || confirmedHangers.Contains(t) || persistentFailures.Contains(t));

            var hasUnrecoverable = batchTests.Any(t =>
                confirmedHangers.Contains(t) || persistentFailures.Contains(t));

            if (allResolved && !hasUnrecoverable)
            {
                results[i] = results[i] with { ExitCode = 0, TimedOut = false };
            }
        }
    }

    /// <summary>
    /// Parses output from a timed-out or failed batch to identify completed tests and the suspected hanger.
    /// Uses ##ptr lines from the custom ParallelTestRunner.TestLogger for accurate FQN matching.
    /// </summary>
    internal static (List<string> CompletedPassed, List<string> CompletedFailed, string? SuspectedHanger)
        ParseTimedOutOutput(IReadOnlyList<string>? capturedOutput, IReadOnlyList<string> batchTests)
    {
        var completedPassed = new List<string>();
        var completedFailed = new List<string>();

        if (capturedOutput is not null)
        {
            var ptrRegex = Patterns.PtrLoggerLineRegex();
            var batchTestSet = new HashSet<string>(batchTests);

            foreach (var line in capturedOutput)
            {
                var ptrMatch = ptrRegex.Match(line);
                if (!ptrMatch.Success) continue;

                var status = ptrMatch.Groups[1].Value;
                var fqn = ptrMatch.Groups[2].Value;

                if (!batchTestSet.Contains(fqn)) continue;

                if (status == "Passed")
                    completedPassed.Add(fqn);
                else if (status == "Failed")
                    completedFailed.Add(fqn);
            }
        }

        var completedSet = new HashSet<string>(completedPassed);
        completedSet.UnionWith(completedFailed);

        string? suspectedHanger = null;
        foreach (var test in batchTests)
        {
            if (!completedSet.Contains(test))
            {
                suspectedHanger = test;
                break;
            }
        }

        return (completedPassed, completedFailed, suspectedHanger);
    }
}
