using ParallelTestRunner;

namespace ParallelTestRunner.Tests;

[TestClass]
public class AutoTunerTests
{
    [TestMethod]
    public void Calculate_715Tests_22Cores_Returns33BatchSize_11Parallelism()
    {
        var (batchSize, parallelism) = AutoTuner.Calculate(715, 22, 40.0);

        Assert.AreEqual(33, batchSize);
        Assert.AreEqual(11, parallelism);
    }

    [TestMethod]
    public void Calculate_10Tests_22Cores_Returns1BatchSize_5Parallelism()
    {
        var (batchSize, parallelism) = AutoTuner.Calculate(10, 22, 40.0);

        Assert.AreEqual(1, batchSize);
        Assert.AreEqual(5, parallelism);
    }

    [TestMethod]
    public void Calculate_100Tests_4Cores_Returns25BatchSize_2Parallelism()
    {
        var (batchSize, parallelism) = AutoTuner.Calculate(100, 4, 40.0);

        Assert.AreEqual(25, batchSize);
        Assert.AreEqual(2, parallelism);
    }

    [TestMethod]
    public void Calculate_3Tests_22Cores_Returns1BatchSize_2Parallelism()
    {
        var (batchSize, parallelism) = AutoTuner.Calculate(3, 22, 40.0);

        Assert.AreEqual(1, batchSize);
        Assert.AreEqual(2, parallelism);
    }

    [TestMethod]
    public void Calculate_1Test_Returns1BatchSize_1Parallelism()
    {
        var (batchSize, parallelism) = AutoTuner.Calculate(1, 16, 40.0);

        Assert.AreEqual(1, batchSize);
        Assert.AreEqual(1, parallelism);
    }

    [TestMethod]
    public void Calculate_0Tests_Returns1BatchSize_1Parallelism()
    {
        var (batchSize, parallelism) = AutoTuner.Calculate(0, 16, 40.0);

        Assert.AreEqual(1, batchSize);
        Assert.AreEqual(1, parallelism);
    }

    [TestMethod]
    public void Calculate_2Cores_100Tests_Returns1Parallelism()
    {
        var (batchSize, parallelism) = AutoTuner.Calculate(100, 2, 40.0);

        Assert.AreEqual(50, batchSize);
        Assert.AreEqual(1, parallelism);
    }

    [TestMethod]
    public void Calculate_LargeTestNames_CapsFilterLength()
    {
        // With avg name length 500, per-test overhead = 5 + 500 + 1 = 506
        // Max safe batch size = floor(7000 / 506) = 13
        var (batchSize, parallelism) = AutoTuner.Calculate(1000, 16, 500.0);

        Assert.IsTrue(batchSize <= 13, $"Batch size {batchSize} should be capped to fit filter limit");
        Assert.IsTrue(batchSize >= 1);
        Assert.IsTrue(parallelism >= 1);
    }

    [TestMethod]
    public void Calculate_ParallelismNeverExceedsCoresHalf()
    {
        var (_, parallelism) = AutoTuner.Calculate(10000, 8, 40.0);

        Assert.IsTrue(parallelism <= 4, $"Parallelism {parallelism} should not exceed cores/2 (4)");
    }

    [TestMethod]
    public void Calculate_ParallelismNeverExceedsTestCount()
    {
        var (_, parallelism) = AutoTuner.Calculate(2, 64, 40.0);

        Assert.IsTrue(parallelism <= 2, $"Parallelism {parallelism} should not exceed test count (2)");
    }
}
