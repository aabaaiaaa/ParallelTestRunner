using Xunit;

namespace DummyTestProject.XUnit;

/// <summary>
/// xUnit runs test collections in parallel by default.
/// These tests detect concurrent execution using a shared counter — they will FAIL
/// if more than one test runs at the same time. The ParallelTestRunner tool forces
/// sequential execution via xUnit.MaxParallelThreads=1, so they should PASS through the tool.
///
/// All tests are in the same collection to ensure xUnit would normally try to parallelise them.
/// </summary>
[CollectionDefinition("Sequential", DisableParallelization = false)]
public class SequentialCollection;

[Collection("Sequential")]
public class SequentialExecutionTests
{
    internal static int _concurrentCount;

    [Fact]
    public void SequentialCheck_A()
    {
        var count = Interlocked.Increment(ref _concurrentCount);
        try
        {
            Assert.Equal(1, count);
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCount);
        }
    }

    [Fact]
    public void SequentialCheck_B()
    {
        var count = Interlocked.Increment(ref _concurrentCount);
        try
        {
            Assert.Equal(1, count);
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCount);
        }
    }

    [Fact]
    public void SequentialCheck_C()
    {
        var count = Interlocked.Increment(ref _concurrentCount);
        try
        {
            Assert.Equal(1, count);
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCount);
        }
    }

    [Fact]
    public void SequentialCheck_D()
    {
        var count = Interlocked.Increment(ref _concurrentCount);
        try
        {
            Assert.Equal(1, count);
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCount);
        }
    }
}

/// <summary>
/// A second test class in a different implicit collection — xUnit runs different classes
/// in parallel by default. These share the same static counter as SequentialExecutionTests,
/// so they'll detect cross-class parallel execution too.
/// </summary>
public class SequentialExecutionTests2
{
    [Fact]
    public void SequentialCheck_E()
    {
        var count = Interlocked.Increment(ref SequentialExecutionTests._concurrentCount);
        try
        {
            Assert.Equal(1, count);
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref SequentialExecutionTests._concurrentCount);
        }
    }

    [Fact]
    public void SequentialCheck_F()
    {
        var count = Interlocked.Increment(ref SequentialExecutionTests._concurrentCount);
        try
        {
            Assert.Equal(1, count);
            Thread.Sleep(100);
        }
        finally
        {
            Interlocked.Decrement(ref SequentialExecutionTests._concurrentCount);
        }
    }
}
