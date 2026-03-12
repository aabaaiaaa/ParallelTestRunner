namespace ParallelTestRunner;

public static class ResultCollator
{
    /// <summary>
    /// Prints a summary of batch results to stderr and returns the process exit code.
    /// Returns 0 if all batches passed, 1 if any failed.
    /// </summary>
    public static int Collate(BatchResult[] results)
    {
        var totalTests = results.Sum(r => r.TestCount);
        var failedBatches = results.Where(r => r.ExitCode != 0).ToArray();

        Console.Error.WriteLine();
        Console.Error.WriteLine("========== Test Run Summary ==========");
        Console.Error.WriteLine($"  Batches: {results.Length}");
        Console.Error.WriteLine($"  Total tests: {totalTests}");
        Console.Error.WriteLine($"  Failed batches: {failedBatches.Length}");

        if (failedBatches.Length > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Failed batch details:");
            foreach (var batch in failedBatches)
            {
                Console.Error.WriteLine($"  Batch {batch.BatchIndex}: {batch.TestCount} tests, exit code {batch.ExitCode}");
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
