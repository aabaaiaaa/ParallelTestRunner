using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class TestBatcherTests
{
    [TestMethod]
    public void CreateBatches_EvenlyDivisible_ReturnsCorrectBatchCount()
    {
        var tests = Enumerable.Range(1, 10).Select(i => $"Namespace.Class.Test{i}").ToList();

        var batches = TestBatcher.CreateBatches(tests, batchSize: 5);

        Assert.AreEqual(2, batches.Count);
        Assert.AreEqual(5, batches[0].Count);
        Assert.AreEqual(5, batches[1].Count);
    }

    [TestMethod]
    public void CreateBatches_Remainder_ReturnsExtraBatch()
    {
        var tests = Enumerable.Range(1, 11).Select(i => $"Namespace.Class.Test{i}").ToList();

        var batches = TestBatcher.CreateBatches(tests, batchSize: 5);

        Assert.AreEqual(3, batches.Count);
        Assert.AreEqual(5, batches[0].Count);
        Assert.AreEqual(5, batches[1].Count);
        Assert.AreEqual(1, batches[2].Count);
    }

    [TestMethod]
    public void CreateBatches_BatchSizeLargerThanTestCount_ReturnsSingleBatch()
    {
        var tests = Enumerable.Range(1, 3).Select(i => $"Namespace.Class.Test{i}").ToList();

        var batches = TestBatcher.CreateBatches(tests, batchSize: 100);

        Assert.AreEqual(1, batches.Count);
        Assert.AreEqual(3, batches[0].Count);
    }

    [TestMethod]
    public void CreateBatches_EmptyList_ReturnsNoBatches()
    {
        var batches = TestBatcher.CreateBatches([], batchSize: 5);

        Assert.AreEqual(0, batches.Count);
    }

    [TestMethod]
    public void CreateBatches_FilterStringExceedsLimit_AutoSplitsBatch()
    {
        // Create test names long enough that a batch of 10 exceeds the 7000-char filter limit.
        // Each filter entry is "FullyQualifiedName=<name>|", so we need names that push past 7000.
        // "FullyQualifiedName=" is 19 chars, separator "|" is 1 char.
        // Target: 10 tests in one batch that exceed 7000 chars total filter string.
        // Per entry: 19 + name.Length, plus 1 separator between entries.
        // For 10 entries: 10*(19 + nameLen) + 9 separators > 7000
        // nameLen > (7000 - 9 - 190) / 10 = 680.1 → use 700-char names
        var longName = new string('A', 700);
        var tests = Enumerable.Range(1, 10)
            .Select(i => $"{longName}{i:D3}")
            .ToList();

        // Verify the filter for all 10 would exceed 7000
        var fullFilter = TestBatcher.BuildFilterString(tests);
        Assert.IsTrue(fullFilter.Length > 7000,
            $"Expected filter > 7000 chars but was {fullFilter.Length}");

        // Use batch size 10 so initial chunking puts all tests in one batch
        var batches = TestBatcher.CreateBatches(tests, batchSize: 10);

        // Should have been auto-split into multiple batches
        Assert.IsTrue(batches.Count > 1,
            $"Expected multiple batches after auto-split but got {batches.Count}");

        // Each resulting batch's filter string must be within the 7000-char limit
        foreach (var batch in batches)
        {
            var filterString = TestBatcher.BuildFilterString(batch.ToList());
            Assert.IsTrue(filterString.Length <= 7000,
                $"Batch filter string is {filterString.Length} chars, exceeds 7000 limit");
        }

        // All original tests must still be present
        var allTests = batches.SelectMany(b => b).ToList();
        CollectionAssert.AreEquivalent(tests, allTests);
    }

    [TestMethod]
    public void CreateBatches_PreservesAllTestNames()
    {
        var tests = Enumerable.Range(1, 17).Select(i => $"Ns.Cls.Test{i}").ToList();

        var batches = TestBatcher.CreateBatches(tests, batchSize: 5);

        var allTests = batches.SelectMany(b => b).ToList();
        CollectionAssert.AreEquivalent(tests, allTests);
    }

    [TestMethod]
    public void CreateBatches_BatchSizeOne_EachTestInOwnBatch()
    {
        var tests = Enumerable.Range(1, 3).Select(i => $"Ns.Cls.Test{i}").ToList();

        var batches = TestBatcher.CreateBatches(tests, batchSize: 1);

        Assert.AreEqual(3, batches.Count);
        Assert.AreEqual(1, batches[0].Count);
        Assert.AreEqual(1, batches[1].Count);
        Assert.AreEqual(1, batches[2].Count);
    }

    [TestMethod]
    public void BuildFilterString_FormatsCorrectly()
    {
        var tests = new List<string> { "Ns.Class.TestA", "Ns.Class.TestB" };

        var filter = TestBatcher.BuildFilterString(tests);

        Assert.AreEqual("FullyQualifiedName~Ns.Class.TestA|FullyQualifiedName~Ns.Class.TestB", filter);
    }
}
