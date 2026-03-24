using System.Text.RegularExpressions;

namespace ParallelTestRunner;

public record RetryResult(
    IReadOnlyList<string> HangingTests,
    IReadOnlyList<string> SuspectedHangingTests,
    IReadOnlyList<string> PersistentFailures,
    int RetryRoundsPerformed);

public static class RetryOrchestrator
{
    /// <summary>
    /// Orchestrates retries for failed/timed-out batches with integrated hang detection.
    /// Timed-out batches have their output parsed to extract suspected hangers; remaining
    /// tests retry at full parallelism. Suspected hangers are tested solo last.
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
        var resolvedTests = new HashSet<string>(); // Tests that passed solo — never retry again
        var persistentFailures = new HashSet<string>(); // Tests that fail every round
        var previousRoundFailures = new HashSet<string>(); // Track failures across rounds
        var round = 0;

        while (true)
        {
            round++;
            ct.ThrowIfCancellationRequested();

            // Classify current results
            var timedOut = new List<int>();
            var failed = new List<int>();

            for (var i = 0; i < results.Length; i++)
            {
                if (results[i].ExitCode == 0) continue;
                if (results[i].TimedOut)
                    timedOut.Add(i);
                else
                    failed.Add(i);
            }

            if (timedOut.Count == 0 && failed.Count == 0)
                break; // All passed

            var retryPool = new List<string>();
            var alreadyPassed = new HashSet<string>(); // Tests that passed in the initial run

            // Handle timed-out batches — parse output to find suspected hangers
            foreach (var idx in timedOut)
            {
                var batchTests = originalBatches[results[idx].BatchIndex];
                var (completedPassed, completedFailed, suspectedHanger) =
                    ParseTimedOutOutput(results[idx].CapturedOutput, batchTests);

                foreach (var t in completedPassed) alreadyPassed.Add(t);

                if (suspectedHanger is not null &&
                    !confirmedHangers.Contains(suspectedHanger) &&
                    !resolvedTests.Contains(suspectedHanger))
                {
                    suspectedHangers.Add(suspectedHanger);
                    Console.Error.WriteLine($"  Suspected hanging test: {suspectedHanger}");
                }

                // Tests that failed before the hang need retry
                retryPool.AddRange(completedFailed);

                // Tests that never ran (after the hanger) need retry
                var completedSet = new HashSet<string>(completedPassed);
                completedSet.UnionWith(completedFailed);

                foreach (var test in batchTests)
                {
                    if (!completedSet.Contains(test) &&
                        !suspectedHangers.Contains(test) &&
                        !confirmedHangers.Contains(test))
                    {
                        retryPool.Add(test);
                    }
                }
            }

            // Handle failed (but completed) batches — parse output to only retry failed/unaccounted tests
            foreach (var idx in failed)
            {
                var batchTests = originalBatches[results[idx].BatchIndex];
                var (completedPassed, completedFailed, _) =
                    ParseTimedOutOutput(results[idx].CapturedOutput, batchTests);

                foreach (var t in completedPassed) alreadyPassed.Add(t);
                retryPool.AddRange(completedFailed);

                // Tests with no output line are unaccounted — treat as failed
                var accounted = new HashSet<string>(completedPassed);
                accounted.UnionWith(completedFailed);
                foreach (var test in batchTests)
                {
                    if (!accounted.Contains(test))
                        retryPool.Add(test);
                }
            }

            // Remove known/suspected hangers, resolved tests, and persistent failures from retry pool
            retryPool = retryPool
                .Where(t => !confirmedHangers.Contains(t) && !suspectedHangers.Contains(t)
                    && !resolvedTests.Contains(t) && !persistentFailures.Contains(t))
                .Distinct()
                .ToList();

            var retryLabel = options.AutoRetry ? $"Retry {round}" : $"Retry {round}/{options.Retries}";

            // Run failure retries at full parallelism with smaller batches to maximize
            // worker utilization and reduce timeout risk for slow tests
            if (retryPool.Count > 0)
            {
                var retryBatchSize = Math.Max(5, retryPool.Count / options.MaxParallelism);
                var retryBatches = TestBatcher.CreateBatches(retryPool, retryBatchSize);

                Console.Error.WriteLine();
                Console.Error.WriteLine($"========== {retryLabel} ==========");
                Console.Error.WriteLine($"  Re-running {retryPool.Count} test(s) in {retryBatches.Count} batch(es)...");

                var retryResults = await runAll(retryBatches, options, ct);

                // Map retry results back: update original batch results
                // Build a set of tests that passed/failed in this retry round
                var passedTests = new HashSet<string>();
                var failedTests = new HashSet<string>();
                var stillTimedOut = new List<(int RetryIdx, IReadOnlyList<string> Tests)>();

                for (var j = 0; j < retryResults.Length; j++)
                {
                    if (retryResults[j].TimedOut)
                    {
                        stillTimedOut.Add((j, retryBatches[j]));
                    }
                    else if (retryResults[j].ExitCode == 0)
                    {
                        foreach (var t in retryBatches[j])
                            passedTests.Add(t);
                    }
                    else
                    {
                        // Parse output to identify which specific tests passed/failed
                        // rather than marking all tests in a failed batch as failed
                        var (cp, cf, _) = ParseTimedOutOutput(retryResults[j].CapturedOutput, retryBatches[j]);
                        foreach (var t in cp) passedTests.Add(t);
                        foreach (var t in cf) failedTests.Add(t);
                        // Tests not in either list (no output captured) count as failed
                        var accounted = new HashSet<string>(cp);
                        accounted.UnionWith(cf);
                        foreach (var t in retryBatches[j])
                        {
                            if (!accounted.Contains(t))
                                failedTests.Add(t);
                        }
                    }
                }

                // Handle any new timeouts from retry batches
                foreach (var (retryIdx, tests) in stillTimedOut)
                {
                    var (cp, cf, sh) = ParseTimedOutOutput(retryResults[retryIdx].CapturedOutput, tests);
                    foreach (var t in cp) passedTests.Add(t);
                    foreach (var t in cf) failedTests.Add(t);
                    if (sh is not null && !confirmedHangers.Contains(sh))
                    {
                        suspectedHangers.Add(sh);
                        Console.Error.WriteLine($"  Suspected hanging test: {sh}");
                    }
                }

                // Track persistent failures — tests that fail every round
                var thisRoundFailures = new HashSet<string>(failedTests);
                if (previousRoundFailures.Count > 0)
                {
                    // Tests that failed both this round and last round
                    var consecutive = thisRoundFailures.Intersect(previousRoundFailures).ToList();
                    foreach (var t in consecutive)
                    {
                        persistentFailures.Add(t);
                        Console.Error.WriteLine($"  Persistent failure: {t}");
                    }
                }
                previousRoundFailures = thisRoundFailures;

                // Update original batch results based on retry outcomes
                for (var i = 0; i < results.Length; i++)
                {
                    if (results[i].ExitCode == 0) continue;

                    var batchTests = originalBatches[results[i].BatchIndex];
                    var allAccountedFor = batchTests.All(t =>
                        passedTests.Contains(t) ||
                        alreadyPassed.Contains(t) ||
                        resolvedTests.Contains(t) ||
                        persistentFailures.Contains(t) ||
                        confirmedHangers.Contains(t) ||
                        suspectedHangers.Contains(t));

                    // Mark batch as passing only if all tests are accounted for
                    // and none are confirmed hangers or persistent failures
                    var hasUnrecoverable = batchTests.Any(t =>
                        confirmedHangers.Contains(t) || persistentFailures.Contains(t));

                    if (allAccountedFor && !hasUnrecoverable)
                    {
                        results[i] = results[i] with { ExitCode = 0, TimedOut = false };
                    }
                }

                var recovered = results.Count(r => r.ExitCode == 0);
                var totalFailing = results.Length - recovered;
                Console.Error.WriteLine($"  {retryLabel}: {recovered}/{results.Length} batch(es) passing, {totalFailing} still failing");

                if (options.AutoRetry && passedTests.Count == 0)
                {
                    Console.Error.WriteLine("  Auto-retry: no progress this round — stopping retries");
                    break;
                }
            }

            // Now test suspected hangers solo
            var newConfirmed = 0;
            if (suspectedHangers.Count > 0)
            {
                var hangersToTest = suspectedHangers.ToList();
                var hangerBatches = hangersToTest.Select(t => (IReadOnlyList<string>)new[] { t }).ToList();

                Console.Error.WriteLine();
                Console.Error.WriteLine($"  Testing {hangerBatches.Count} suspected hanging test(s) individually...");

                var hangerResults = await runAll(hangerBatches, options, ct);

                for (var j = 0; j < hangerResults.Length; j++)
                {
                    var test = hangersToTest[j];
                    if (hangerResults[j].TimedOut)
                    {
                        confirmedHangers.Add(test);
                        suspectedHangers.Remove(test);
                        newConfirmed++;
                        Console.Error.WriteLine($"    Confirmed hanging test: {test}");
                    }
                    else if (hangerResults[j].ExitCode == 0)
                    {
                        suspectedHangers.Remove(test);
                        resolvedTests.Add(test);
                        Console.Error.WriteLine($"    Test passed on retry: {test}");
                    }
                    // If failed (not timed out), stays in suspected for next round retry
                }
            }

            // Stop conditions
            var anyFailures = results.Any(r => r.ExitCode != 0);
            if (!anyFailures) break;

            if (retryPool.Count == 0 && suspectedHangers.Count == 0)
                break; // Nothing left to retry

            if (!options.AutoRetry && round >= options.Retries)
                break;

            if (options.AutoRetry && round >= 10)
            {
                Console.Error.WriteLine("  Auto-retry: max rounds (10) reached — stopping retries");
                break;
            }
        }

        return new RetryResult(
            confirmedHangers.ToList(),
            suspectedHangers.ToList(),
            persistentFailures.ToList(),
            round);
    }

    /// <summary>
    /// Parses output from a timed-out or failed batch to identify completed tests and the suspected hanger.
    /// Uses ##ptr lines from the custom ParallelTestRunner.TestLogger for accurate FQN matching.
    /// Falls back to display-name parsing (Passed/Failed lines) if no ##ptr lines are found.
    /// Since Workers=1 forces sequential execution, the first test not in the completed list
    /// is the one that was running when the timeout hit.
    /// </summary>
    internal static (List<string> CompletedPassed, List<string> CompletedFailed, string? SuspectedHanger)
        ParseTimedOutOutput(IReadOnlyList<string>? capturedOutput, IReadOnlyList<string> batchTests)
    {
        var completedPassed = new List<string>();
        var completedFailed = new List<string>();

        if (capturedOutput is not null)
        {
            // Try ##ptr lines first (accurate FQN matching)
            var ptrRegex = Patterns.PtrLoggerLineRegex();
            var batchTestSet = new HashSet<string>(batchTests);
            var foundPtrLines = false;

            foreach (var line in capturedOutput)
            {
                var ptrMatch = ptrRegex.Match(line);
                if (!ptrMatch.Success) continue;

                foundPtrLines = true;
                var status = ptrMatch.Groups[1].Value;
                var fqn = ptrMatch.Groups[2].Value;

                // Exact FQN match against batch tests
                if (!batchTestSet.Contains(fqn)) continue;

                if (status == "Passed")
                    completedPassed.Add(fqn);
                else if (status == "Failed")
                    completedFailed.Add(fqn);
            }

            // Fallback: if no ##ptr lines were found, try legacy display-name parsing.
            // This handles cases where the custom logger is unavailable.
            if (!foundPtrLines)
            {
                var regex = Patterns.TestResultLineRegex();
                foreach (var line in capturedOutput)
                {
                    var match = regex.Match(line);
                    if (!match.Success) continue;

                    var status = match.Groups[1].Value;
                    var testName = match.Groups[2].Value;

                    // Match against batch tests: prefer exact match, fall back to
                    // starts-with for parameterised variants (e.g. "Ns.Test(1)" starts with FQN "Ns.Test")
                    var matchedTest = batchTests.FirstOrDefault(t => testName == t)
                        ?? batchTests.FirstOrDefault(t => testName.StartsWith(t + "("));

                    if (matchedTest is null) continue;

                    if (status == "Passed")
                        completedPassed.Add(matchedTest);
                    else
                        completedFailed.Add(matchedTest);
                }
            }
        }

        var completedSet = new HashSet<string>(completedPassed);
        completedSet.UnionWith(completedFailed);

        // The suspected hanger is the first test in the batch not in the completed set
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
