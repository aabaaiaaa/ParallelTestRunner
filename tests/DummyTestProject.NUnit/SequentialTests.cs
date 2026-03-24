using NUnit.Framework;

// Enable parallel execution at assembly level — NUnit will run test fixtures concurrently.
// The ParallelTestRunner tool forces sequential execution via NUnit.NumberOfTestWorkers=1,
// so the concurrency-detecting tests below should PASS through the tool.
[assembly: Parallelizable(ParallelScope.All)]

namespace DummyTestProject.NUnit;

[TestFixture]
public class SequentialExecutionTests
{
    internal static int _concurrentCount;

    [Test]
    public void SequentialCheck_A()
    {
        var count = Interlocked.Increment(ref _concurrentCount);
        try
        {
            Assert.That(count, Is.EqualTo(1),
                $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCount);
        }
    }

    [Test]
    public void SequentialCheck_B()
    {
        var count = Interlocked.Increment(ref _concurrentCount);
        try
        {
            Assert.That(count, Is.EqualTo(1),
                $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCount);
        }
    }

    [Test]
    public void SequentialCheck_C()
    {
        var count = Interlocked.Increment(ref _concurrentCount);
        try
        {
            Assert.That(count, Is.EqualTo(1),
                $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCount);
        }
    }

    [Test]
    public void SequentialCheck_D()
    {
        var count = Interlocked.Increment(ref _concurrentCount);
        try
        {
            Assert.That(count, Is.EqualTo(1),
                $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCount);
        }
    }
}

/// <summary>
/// A second test fixture — with [assembly: Parallelizable(ParallelScope.All)], NUnit
/// will run this fixture concurrently with SequentialExecutionTests. Shares the same
/// static counter to detect cross-fixture parallel execution.
/// </summary>
[TestFixture]
public class SequentialExecutionTests2
{
    [Test]
    public void SequentialCheck_E()
    {
        var count = Interlocked.Increment(ref SequentialExecutionTests._concurrentCount);
        try
        {
            Assert.That(count, Is.EqualTo(1),
                $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref SequentialExecutionTests._concurrentCount);
        }
    }

    [Test]
    public void SequentialCheck_F()
    {
        var count = Interlocked.Increment(ref SequentialExecutionTests._concurrentCount);
        try
        {
            Assert.That(count, Is.EqualTo(1),
                $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref SequentialExecutionTests._concurrentCount);
        }
    }
}
