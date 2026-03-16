using System.Text.RegularExpressions;

namespace ParallelTestRunner;

public record RetryResult(
    IReadOnlyList<string> HangingTests,
    IReadOnlyList<string> SuspectedHangingTests,
    int RetryRoundsPerformed);

public static partial class RetryOrchestrator
{
    [GeneratedRegex(@"^\s+(Passed|Failed)\s+(.+?)\s+\[", RegexOptions.Compiled)]
    private static partial Regex TestResultLineRegex();

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

            // Handle timed-out batches — parse output to find suspected hangers
            foreach (var idx in timedOut)
            {
                var batchTests = originalBatches[results[idx].BatchIndex];
                var (completedPassed, completedFailed, suspectedHanger) =
                    ParseTimedOutOutput(results[idx].CapturedOutput, batchTests);

                if (suspectedHanger is not null && !confirmedHangers.Contains(suspectedHanger))
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

            // Handle failed (but completed) batches — retry all their tests
            foreach (var idx in failed)
            {
                retryPool.AddRange(originalBatches[results[idx].BatchIndex]);
            }

            // Remove known/suspected hangers from retry pool
            retryPool = retryPool
                .Where(t => !confirmedHangers.Contains(t) && !suspectedHangers.Contains(t))
                .Distinct()
                .ToList();

            var retryLabel = options.AutoRetry ? $"Retry {round}" : $"Retry {round}/{options.Retries}";

            // Run failure retries at full parallelism
            if (retryPool.Count > 0)
            {
                var retryBatches = TestBatcher.CreateBatches(retryPool, options.BatchSize);

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
                        foreach (var t in retryBatches[j])
                            failedTests.Add(t);
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

                // Update original batch results based on retry outcomes
                for (var i = 0; i < results.Length; i++)
                {
                    if (results[i].ExitCode == 0) continue;

                    var batchTests = originalBatches[results[i].BatchIndex];
                    var allPassed = batchTests.All(t =>
                        passedTests.Contains(t) ||
                        confirmedHangers.Contains(t) ||
                        suspectedHangers.Contains(t));

                    if (allPassed && !batchTests.Any(t => confirmedHangers.Contains(t)))
                    {
                        results[i] = results[i] with { ExitCode = 0, TimedOut = false };
                    }
                }

                var recovered = results.Count(r => r.ExitCode == 0);
                var totalFailing = results.Length - recovered;
                Console.Error.WriteLine($"  {retryLabel}: {recovered}/{results.Length} batch(es) passing, {totalFailing} still failing");

                if (options.AutoRetry && passedTests.Count == 0 && stillTimedOut.Count == 0 && suspectedHangers.Count == 0)
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
                        Console.Error.WriteLine($"    Test passed on retry: {test}");

                        // Update any original batch that contained this test
                        for (var i = 0; i < results.Length; i++)
                        {
                            if (results[i].ExitCode != 0)
                            {
                                var batchTests = originalBatches[results[i].BatchIndex];
                                if (batchTests.Contains(test))
                                {
                                    var allResolved = batchTests.All(t =>
                                        t == test ||
                                        confirmedHangers.Contains(t) ||
                                        suspectedHangers.Contains(t) ||
                                        results[i].ExitCode == 0);
                                    // Don't mark resolved here — let next round handle it
                                }
                            }
                        }
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
        }

        return new RetryResult(
            confirmedHangers.ToList(),
            suspectedHangers.ToList(),
            round);
    }

    /// <summary>
    /// Parses output from a timed-out batch to identify completed tests and the suspected hanger.
    /// VSTest output lines: "  Passed TestName [Xs]" / "  Failed TestName [Xs]"
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
            var regex = TestResultLineRegex();
            foreach (var line in capturedOutput)
            {
                var match = regex.Match(line);
                if (!match.Success) continue;

                var status = match.Groups[1].Value;
                var testName = match.Groups[2].Value;

                // Match against batch tests using contains (same as filter logic)
                var matchedTest = batchTests.FirstOrDefault(t =>
                    testName.Contains(t) || t.Contains(testName) || testName == t);

                if (matchedTest is null) continue;

                if (status == "Passed")
                    completedPassed.Add(matchedTest);
                else
                    completedFailed.Add(matchedTest);
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
