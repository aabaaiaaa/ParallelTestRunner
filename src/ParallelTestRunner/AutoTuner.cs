namespace ParallelTestRunner;

public static class AutoTuner
{
    private const int MaxFilterLength = 7000;
    private const int FilterPrefixLength = 5; // "Name~"
    private const int FilterSeparatorLength = 1; // "|"

    /// <summary>
    /// Calculates sensible batch size and parallelism based on test count,
    /// available cores, and average test name length.
    /// </summary>
    public static (int BatchSize, int MaxParallelism) Calculate(
        int testCount, int processorCount, double averageTestNameLength)
    {
        if (testCount <= 0)
            return (1, 1);

        // Step 1: initial max parallelism = min(testCount, cores/2)
        var maxParallelism = Math.Min(testCount, Math.Max(1, processorCount / 2));

        // Step 2: target 2x parallel slots for load balancing
        var targetBatches = maxParallelism * 2;

        // Step 3: batch size from target batches
        var batchSize = Math.Max(1, (int)Math.Ceiling((double)testCount / targetBatches));

        // Step 4: filter limit guard
        // Each entry in filter: "Name~" + name + "|" (separator)
        var perTestLength = FilterPrefixLength + averageTestNameLength + FilterSeparatorLength;
        if (batchSize * perTestLength > MaxFilterLength)
        {
            batchSize = Math.Max(1, (int)Math.Floor(MaxFilterLength / perTestLength));
        }

        // Step 5: adjust parallelism based on actual batch count
        var actualBatches = (int)Math.Ceiling((double)testCount / batchSize);
        maxParallelism = Math.Min(maxParallelism, (int)Math.Ceiling(actualBatches / 2.0));
        maxParallelism = Math.Max(1, maxParallelism);

        return (batchSize, maxParallelism);
    }
}
