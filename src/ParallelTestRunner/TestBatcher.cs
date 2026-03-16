namespace ParallelTestRunner;

public static class TestBatcher
{
    private const int MaxFilterLength = 7000;
    // Use FullyQualifiedName= (exact match) for filtering. Discovery provides FQNs via
    // dotnet vstest --ListFullyQualifiedTests, so names are clean (no parens/commas).
    // Parameterised tests are naturally grouped — the FQN is the base method name and
    // FullyQualifiedName= matches all variants.
    private const string FilterPrefix = "FullyQualifiedName=";
    private const string FilterSeparator = "|";

    /// <summary>
    /// Splits test names into batches of at most <paramref name="batchSize"/>,
    /// then auto-splits any batch whose filter string exceeds 7000 characters.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> CreateBatches(
        IReadOnlyList<string> testNames, int batchSize)
    {
        if (testNames.Count == 0)
            return [];

        // Initial chunking by batch size
        var initialBatches = testNames.Chunk(batchSize);

        var result = new List<IReadOnlyList<string>>();

        foreach (var batch in initialBatches)
        {
            var batchList = batch.ToList();

            if (BuildFilterString(batchList).Length <= MaxFilterLength)
            {
                result.Add(batchList);
            }
            else
            {
                // Auto-split oversized batches
                result.AddRange(SplitToFitFilterLimit(batchList));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the <c>--filter</c> string for a batch of test names.
    /// Format: <c>FullyQualifiedName=Ns.Class.Test1|FullyQualifiedName=Ns.Class.Test2|...</c>
    /// </summary>
    internal static string BuildFilterString(IReadOnlyList<string> testNames)
    {
        return string.Join(FilterSeparator,
            testNames.Select(name => $"{FilterPrefix}{name}"));
    }

    private static List<IReadOnlyList<string>> SplitToFitFilterLimit(List<string> tests)
    {
        var result = new List<IReadOnlyList<string>>();
        var currentBatch = new List<string>();
        var currentLength = 0;

        foreach (var test in tests)
        {
            var entryLength = FilterPrefix.Length + test.Length;

            // Account for the separator before this entry (if not the first in batch)
            var lengthIfAdded = currentLength == 0
                ? entryLength
                : currentLength + FilterSeparator.Length + entryLength;

            if (lengthIfAdded > MaxFilterLength && currentBatch.Count > 0)
            {
                result.Add(currentBatch);
                currentBatch = new List<string>();
                currentLength = 0;
                lengthIfAdded = entryLength;
            }

            currentBatch.Add(test);
            currentLength = lengthIfAdded;
        }

        if (currentBatch.Count > 0)
            result.Add(currentBatch);

        return result;
    }
}
