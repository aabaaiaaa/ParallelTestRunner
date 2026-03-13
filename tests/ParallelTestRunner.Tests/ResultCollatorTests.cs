using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class ResultCollatorTests
{
    [TestMethod]
    public void Collate_AllBatchesPass_Returns0()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 0),
            new BatchResult(1, 10, 0),
            new BatchResult(2, 5, 0),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public void Collate_OneBatchFails_Returns1()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 0),
            new BatchResult(1, 10, 1),
            new BatchResult(2, 5, 0),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_AllBatchesFail_Returns1()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 1),
            new BatchResult(1, 10, 1),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void Collate_SinglePassingBatch_Returns0()
    {
        var results = new[]
        {
            new BatchResult(0, 25, 0),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public void Collate_NonZeroExitCode_TreatedAsFailure()
    {
        var results = new[]
        {
            new BatchResult(0, 10, 0),
            new BatchResult(1, 10, 2),
        };

        var exitCode = ResultCollator.Collate(results);

        Assert.AreEqual(1, exitCode);
    }
}
