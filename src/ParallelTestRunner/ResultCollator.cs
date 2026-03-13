namespace ParallelTestRunner;

public static class ResultCollator
{
    /// <summary>
    /// Prints a summary of batch results to stderr and returns the process exit code.
    /// Returns 0 if all batches passed, 1 if any failed.
    /// </summary>
    public static int Collate(BatchResult[] results, HangDetectionResult? hangDetection = null)
    {
        var totalTests = results.Sum(r => r.TestCount);
        var failedBatches = results.Where(r => r.ExitCode != 0).ToArray();
        var timedOutBatches = results.Where(r => r.TimedOut).ToArray();

        Console.Error.WriteLine();
        Console.Error.WriteLine("========== Test Run Summary ==========");
        Console.Error.WriteLine($"  Batches: {results.Length}");
        Console.Error.WriteLine($"  Total tests: {totalTests}");
        Console.Error.WriteLine($"  Failed batches: {failedBatches.Length}");
        Console.Error.WriteLine($"  Timed out batches: {timedOutBatches.Length}");

        if (timedOutBatches.Length > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("========== Timed Out Batches ==========");
            foreach (var batch in timedOutBatches)
            {
                Console.Error.WriteLine($"  Batch {batch.BatchIndex}: {batch.TestCount} tests — TIMED OUT");

                if (batch.CapturedOutput is { Count: > 0 })
                {
                    Console.Error.WriteLine($"  Last output from batch {batch.BatchIndex} (showing last 50 lines):");
                    Console.Error.WriteLine("  " + new string('-', 60));
                    var linesToShow = batch.CapturedOutput.Skip(Math.Max(0, batch.CapturedOutput.Count - 50));
                    foreach (var line in linesToShow)
                    {
                        Console.Error.WriteLine($"    {line}");
                    }
                    Console.Error.WriteLine("  " + new string('-', 60));
                }
            }
        }

        if (hangDetection is { HangingTests.Count: > 0 })
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("========== Hanging Test Detection ==========");
            Console.Error.WriteLine($"  Retries performed: {hangDetection.RetriesPerformed}");
            Console.Error.WriteLine($"  Hanging tests identified: {hangDetection.HangingTests.Count}");
            foreach (var test in hangDetection.HangingTests)
            {
                Console.Error.WriteLine($"    - {test}");
            }
        }
        else if (hangDetection is not null)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("  Hang detection ran but no hanging tests were confirmed (may be intermittent).");
        }

        if (failedBatches.Length > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Failed batch details:");
            foreach (var batch in failedBatches)
            {
                var suffix = batch.TimedOut ? " (TIMED OUT)" : "";
                Console.Error.WriteLine($"  Batch {batch.BatchIndex}: {batch.TestCount} tests, exit code {batch.ExitCode}{suffix}");
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("Result: FAILED");
            return 1;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Result: PASSED");
        return 0;
    }
}
