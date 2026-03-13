namespace ParallelTestRunner;

public static class TestBatcher
{
    private const int MaxFilterLength = 7000;
    // Use Name~ (contains) rather than FullyQualifiedName~ because --list-tests returns
    // display names. For BDD frameworks (Reqnroll, SpecFlow) these are scenario titles
    // which don't match FullyQualifiedName. Contains-match is needed because parameterised
    // test names are deduplicated during discovery (their (...) suffix is stripped, since
    // the VSTest filter parser cannot handle parentheses/commas in filter values).
    private const string FilterPrefix = "Name~";
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
    /// Format: <c>Name~Test1|Name~Test2|...</c>
    /// Uses contains-match (<c>~</c>) so that parameterised test variants (whose
    /// <c>(...)</c> suffix was stripped during discovery) are still matched.
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
