using System.Diagnostics;
using System.Reflection;

namespace ParallelTestRunner.Tests;

[TestClass]
public class IntegrationTests
{
    private static string _solutionRoot = null!;
    private static string _dummyProjectPath = null!;
    private static string _toolProjectPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _solutionRoot = FindSolutionRoot();
        _dummyProjectPath = Path.Combine(_solutionRoot, "tests", "DummyTestProject", "DummyTestProject.csproj");
        _toolProjectPath = Path.Combine(_solutionRoot, "src", "ParallelTestRunner", "ParallelTestRunner.csproj");

        Assert.IsTrue(File.Exists(_dummyProjectPath), $"DummyTestProject not found at {_dummyProjectPath}");
        Assert.IsTrue(File.Exists(_toolProjectPath), $"Tool project not found at {_toolProjectPath}");

        // Build DummyTestProject since the tool always passes --no-build
        var buildResult = RunProcess("dotnet", $"build \"{_dummyProjectPath}\" -c Debug --nologo -v q");
        Assert.AreEqual(0, buildResult.ExitCode, $"Failed to build DummyTestProject:\n{buildResult.Stderr}");
    }

    [TestMethod]
    public void Discovery_Finds20Tests()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 61 tests");
    }

    [TestMethod]
    public void RunWithBatching_AllTestsExecute()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 5 --max-parallelism 2");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 61 tests");
        // With batch size 5 and 61 tests, expect 13 batches
        StringAssert.Contains(result.Stderr, "Created 13 batches");
    }

    [TestMethod]
    public void AllTestsPass_ExitCode0()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 5 --max-parallelism 2");

        Assert.AreEqual(0, result.ExitCode, $"Expected exit code 0 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void FailTestsEnvVar_ExitCode1()
    {
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 5 --max-parallelism 2",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_TESTS"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void IdleTimeout_KillsHangingBatch()
    {
        // Run with a very short idle timeout (10s) and trigger the hanging test.
        // The hanging test produces no output while blocked, so idle detection should kill it.
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --idle-timeout 10",
            environmentOverrides: new Dictionary<string, string> { ["HANG_TEST"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "IDLE TIMEOUT");
        StringAssert.Contains(result.Stderr, "Timed out batches: 1");
        // The captured output should be present so the user can see which test was last running
        StringAssert.Contains(result.Stderr, "Last output from batch");
    }

    [TestMethod]
    [Timeout(300000)] // 5 minute timeout for the whole test
    public void IdleTimeout_BinarySplit_IdentifiesHangingTest()
    {
        // Run with small batches and short idle timeout to trigger hang detection.
        // The binary-split should isolate HangingTest_ConditionalBlock.
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --idle-timeout 10",
            environmentOverrides: new Dictionary<string, string> { ["HANG_TEST"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Hang Detection");
        StringAssert.Contains(result.Stderr, "Hanging Test Detection");
        StringAssert.Contains(result.Stderr, "HangingTest_ConditionalBlock");
    }

    [TestMethod]
    public void LongFilterString_40LongNamesInOneBatch()
    {
        // 40 tests with ~95-char names in a single batch = ~4000 char filter string
        // Tests whether VSTest can handle a large Name~...|Name~...|... filter
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        // 21 original + 40 long-named = 61 total
        StringAssert.Contains(result.Stderr, "Discovered 61 tests");
        StringAssert.Contains(result.Stderr, "Created 1 batches");
        StringAssert.Contains(result.Stderr, "Timed out batches: 0");
    }

    [TestMethod]
    public void LongFilterString_40LongNamesParallel()
    {
        // Split the 40 long-named tests into batches of 20, run 2 in parallel
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 20 --max-parallelism 2 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 61 tests");
        StringAssert.Contains(result.Stderr, "Timed out batches: 0");
    }

    [TestMethod]
    public void BatchSize1_AllTestsComplete()
    {
        // Each test gets its own batch with a single Name~ filter
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 1 --max-parallelism 1 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 61 batches");
        StringAssert.Contains(result.Stderr, "Timed out batches: 0");
    }

    [TestMethod]
    public void BatchSize5_MultiFilterPerBatch()
    {
        // 5 Name~ conditions OR'd together per batch
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 5 --max-parallelism 1 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 13 batches");
        StringAssert.Contains(result.Stderr, "Timed out batches: 0");
    }

    [TestMethod]
    public void BatchSize10_MultiFilterPerBatch()
    {
        // 10 Name~ conditions OR'd together per batch
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 10 --max-parallelism 1 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 7 batches");
        StringAssert.Contains(result.Stderr, "Timed out batches: 0");
    }

    [TestMethod]
    public void BatchSizeAll_SingleBatchMultiFilter()
    {
        // All 21 tests in a single batch — one big Name~...|Name~...|... filter
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 1 batches");
        StringAssert.Contains(result.Stderr, "Timed out batches: 0");
    }

    [TestMethod]
    public void BatchSizeAll_ParallelMultiFilter()
    {
        // All 61 tests split into batches of 5, running 3 in parallel
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 5 --max-parallelism 3 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 13 batches");
        StringAssert.Contains(result.Stderr, "Timed out batches: 0");
    }

    [TestMethod]
    public void SkipTests_SkipsFirstN()
    {
        // Skip first 50 tests, run the rest — should see fewer tests
        var result = RunTool($"\"{_dummyProjectPath}\" --skip-tests 50 --batch-size 100 --max-parallelism 1 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 61 tests");
        StringAssert.Contains(result.Stderr, "Skipped first 50 tests, 11 remaining");
    }

    [TestMethod]
    public void SkipTests_CombinedWithMaxTests()
    {
        // Skip 5, then take 10 — should run tests 6-15 from the discovered list
        var result = RunTool($"\"{_dummyProjectPath}\" --skip-tests 5 --max-tests 10 --batch-size 100 --max-parallelism 1 --idle-timeout 30");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 61 tests");
        StringAssert.Contains(result.Stderr, "Skipped first 5 tests, 56 remaining");
        StringAssert.Contains(result.Stderr, "Limited to 10 tests (--max-tests 10)");
        StringAssert.Contains(result.Stderr, "Timed out batches: 0");
    }

    [TestMethod]
    public void SkipTests_SkipAllProducesNoTests()
    {
        // Skip more tests than exist — should find zero tests after skip
        var result = RunTool($"\"{_dummyProjectPath}\" --skip-tests 100 --batch-size 100 --max-parallelism 1 --idle-timeout 30");

        // Exit code 2 = infrastructure error (zero tests found)
        Assert.AreEqual(2, result.ExitCode, $"Expected exit code 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
    }

    private static ProcessResult RunTool(string arguments, Dictionary<string, string>? environmentOverrides = null)
    {
        return RunProcess("dotnet", $"run --project \"{_toolProjectPath}\" --no-launch-profile -- {arguments}",
            environmentOverrides);
    }

    private static ProcessResult RunProcess(string fileName, string arguments,
        Dictionary<string, string>? environmentOverrides = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _solutionRoot,
            },
            EnableRaisingEvents = true,
        };

        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
                process.StartInfo.Environment[key] = value;
        }

        // Make sure trigger env vars are not set unless explicitly provided
        if (environmentOverrides is null || !environmentOverrides.ContainsKey("FAIL_TESTS"))
            process.StartInfo.Environment.Remove("FAIL_TESTS");
        if (environmentOverrides is null || !environmentOverrides.ContainsKey("HANG_TEST"))
            process.StartInfo.Environment.Remove("HANG_TEST");

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(TimeSpan.FromMinutes(5));
        Assert.IsTrue(exited, "Process did not exit within 5 minutes");

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string FindSolutionRoot()
    {
        var assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var dir = new DirectoryInfo(assemblyLocation);

        while (dir is not null)
        {
            if (dir.GetFiles("ParallelTestRunner.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find ParallelTestRunner.sln searching upward from {assemblyLocation}");
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
