namespace ParallelTestRunner;

public record HangDetectionResult(
    IReadOnlyList<string> HangingTests,
    int RetriesPerformed);

public static class HangDetector
{
    private const int MaxDepth = 10;

    /// <summary>
    /// For each timed-out batch, binary-splits and re-runs to isolate hanging test(s).
    /// </summary>
    public static Task<HangDetectionResult> DetectAsync(
        BatchResult[] results,
        IReadOnlyList<IReadOnlyList<string>> batches,
        Options options,
        CancellationToken ct)
    {
        return DetectAsync(
            results,
            batches,
            (batch, index, ct) => TestRunner.RunSingleBatchAsync(batch, index, options, ct),
            ct);
    }

    /// <summary>
    /// Overload accepting a batch runner func for unit testing.
    /// </summary>
    internal static async Task<HangDetectionResult> DetectAsync(
        BatchResult[] results,
        IReadOnlyList<IReadOnlyList<string>> batches,
        Func<IReadOnlyList<string>, int, CancellationToken, Task<BatchResult>> runBatch,
        CancellationToken ct)
    {
        var hangingTests = new List<string>();
        var retries = 0;

        for (var i = 0; i < results.Length; i++)
        {
            if (!results[i].TimedOut)
                continue;

            var batchTests = batches[results[i].BatchIndex];

            Console.Error.WriteLine();
            Console.Error.WriteLine($"  Hang detection: investigating batch {results[i].BatchIndex} ({batchTests.Count} tests)...");

            var found = await BinarySplitAsync(batchTests.ToList(), runBatch, r => r + 1, ct, 0);
            hangingTests.AddRange(found.Hanging);
            retries += found.Retries;
        }

        return new HangDetectionResult(hangingTests, retries);
    }

    private static async Task<(List<string> Hanging, int Retries)> BinarySplitAsync(
        List<string> tests,
        Func<IReadOnlyList<string>, int, CancellationToken, Task<BatchResult>> runBatch,
        Func<int, int> nextIndex,
        CancellationToken ct,
        int depth)
    {
        if (tests.Count == 0)
            return ([], 0);

        // Base case: single test that timed out = confirmed hanging
        if (tests.Count == 1)
        {
            var result = await runBatch(tests, nextIndex(1000 + depth), ct);
            if (result.TimedOut)
            {
                Console.Error.WriteLine($"    Identified hanging test: {tests[0]}");
                return ([tests[0]], 1);
            }

            // Didn't hang on retry — intermittent
            Console.Error.WriteLine($"    Test passed on retry: {tests[0]}");
            return ([], 1);
        }

        if (depth >= MaxDepth)
        {
            Console.Error.WriteLine($"    Max depth reached with {tests.Count} tests remaining — reporting all as suspect");
            return (tests.ToList(), 0);
        }

        var mid = tests.Count / 2;
        var left = tests.GetRange(0, mid);
        var right = tests.GetRange(mid, tests.Count - mid);

        var hanging = new List<string>();
        var retries = 0;

        // Run left half
        Console.Error.WriteLine($"    Depth {depth}: testing left half ({left.Count} tests)...");
        var leftResult = await runBatch(left, nextIndex(2000 + depth * 2), ct);
        retries++;

        if (leftResult.TimedOut)
        {
            var sub = await BinarySplitAsync(left, runBatch, nextIndex, ct, depth + 1);
            hanging.AddRange(sub.Hanging);
            retries += sub.Retries;
        }

        // Run right half
        Console.Error.WriteLine($"    Depth {depth}: testing right half ({right.Count} tests)...");
        var rightResult = await runBatch(right, nextIndex(2001 + depth * 2), ct);
        retries++;

        if (rightResult.TimedOut)
        {
            var sub = await BinarySplitAsync(right, runBatch, nextIndex, ct, depth + 1);
            hanging.AddRange(sub.Hanging);
            retries += sub.Retries;
        }

        return (hanging, retries);
    }
}
