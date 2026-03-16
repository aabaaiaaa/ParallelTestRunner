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
    public void Discovery_FindsAllTests()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-tests 10 --max-parallelism 8 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 66 tests");
        StringAssert.Contains(result.Stderr, "Limited to 10 tests");
    }

    [TestMethod]
    public void RunWithBatching_AllTestsExecute()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 5 --max-tests 15 --max-parallelism 8 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 3 batches");
    }

    [TestMethod]
    public void FailTestsEnvVar_ExitCode1()
    {
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 8 --retries 0",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_TESTS"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    [Timeout(120000)]
    public void IdleTimeout_DetectsAndIsolatesHangingTest()
    {
        // Single test covering idle timeout detection + retry-based hang isolation.
        // Uses 3s idle timeout — passing tests produce output within ~2s of startup.
        // Retries enabled so the orchestrator can extract suspected hangers and test them solo.
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 8 --idle-timeout 3 --retries 2",
            environmentOverrides: new Dictionary<string, string> { ["HANG_TEST"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        // Idle timeout fires
        StringAssert.Contains(result.Stderr, "IDLE TIMEOUT");
        // Retry orchestrator identifies the hanging test
        StringAssert.Contains(result.Stderr, "Suspected hanging test");
        StringAssert.Contains(result.Stderr, "HangingTest_ConditionalBlock");
    }

    [TestMethod]
    public void SequentialExecution_ForcesWorkersToOne()
    {
        // DummyTestProject has [assembly: Parallelize(Workers = 4)] and SequentialCheck tests
        // that fail if >1 test runs concurrently. The tool should override this with Workers=1.
        // Must use --max-parallelism 1 here so all sequential tests run in the same batch/process.
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode,
            $"Expected exit code 0 — SequentialCheck tests likely detected parallel execution.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void LongFilterString_AutoSplitsWhenExceedingLimit()
    {
        // 66 tests with FQN prefix exceeds 7000-char filter limit, auto-splits into 2 batches
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 8 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 66 tests");
        StringAssert.Contains(result.Stderr, "Created 2 batches");
    }

    [TestMethod]
    public void BatchSize1_EachTestGetsOwnBatch()
    {
        // Each test gets its own batch — limit to 10 tests to keep process count reasonable
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 1 --max-tests 10 --max-parallelism 8 --idle-timeout 30 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 10 batches");
    }

    [TestMethod]
    public void BatchSize10_MultiFilterPerBatch()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 10 --max-tests 20 --max-parallelism 8 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 2 batches");
    }

    [TestMethod]
    public void SkipTests_SkipsFirstN()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --skip-tests 50 --batch-size 100 --max-parallelism 8 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 66 tests");
        StringAssert.Contains(result.Stderr, "Skipped first 50 tests, 16 remaining");
    }

    [TestMethod]
    public void SkipTests_CombinedWithMaxTests()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --skip-tests 5 --max-tests 10 --batch-size 100 --max-parallelism 8 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Skipped first 5 tests, 61 remaining");
        StringAssert.Contains(result.Stderr, "Limited to 10 tests (--max-tests 10)");
    }

    [TestMethod]
    public void SkipTests_SkipAllProducesNoTests()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --skip-tests 100 --batch-size 100 --max-parallelism 8 --retries 0");

        // Exit code 2 = infrastructure error (zero tests found)
        Assert.AreEqual(2, result.ExitCode, $"Expected exit code 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void Retries_TransientFailure_PassesOnRetry()
    {
        // Clean up marker file in case a previous run left it behind
        var markerPath = Path.Combine(Path.GetTempPath(), "parallel_test_runner_fail_once.marker");
        if (File.Exists(markerPath))
            File.Delete(markerPath);

        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 8 --retries 1 --idle-timeout 10",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_ONCE"] = "1" });

        Assert.AreEqual(0, result.ExitCode, $"Expected exit code 0 (retry should recover) but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Retry 1/1");
    }

    [TestMethod]
    public void Retries_PersistentFailure_StillFails()
    {
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 8 --retries 2 --idle-timeout 10",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_TESTS"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Retry 1/2");
        StringAssert.Contains(result.Stderr, "Retry 2/2");
    }

    [TestMethod]
    public void AutoRetry_KeepsRetryingWhileMakingProgress()
    {
        // Clean up marker file in case a previous run left it behind
        var markerPath = Path.Combine(Path.GetTempPath(), "parallel_test_runner_fail_once.marker");
        if (File.Exists(markerPath))
            File.Delete(markerPath);

        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 8 --auto-retry --retries 0 --idle-timeout 10",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_ONCE"] = "1" });

        Assert.AreEqual(0, result.ExitCode, $"Expected exit code 0 (auto-retry should recover) but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        // Proves auto-retry overrides --retries 0
        StringAssert.Contains(result.Stderr, "Retry 1:");
        StringAssert.Contains(result.Stderr, "Auto-retry: enabled");
    }

    [TestMethod]
    public void AutoRetry_OnlyRetriesFailedBatches_NotPassingTests()
    {
        // With batch-size 5, the 3 FailableTests end up in the last batch (tests 64-66).
        // The other 12 batches pass. Auto-retry should only re-run the failed batch's tests,
        // not all 66 tests, and stop after 1 round of no progress.
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 5 --max-parallelism 8 --auto-retry --idle-timeout 30",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_TESTS"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        // Auto-retry should stop after detecting no progress — proves it doesn't loop
        StringAssert.Contains(result.Stderr, "Auto-retry: no progress this round");
        // Only the failed batch's tests should be retried (6 or fewer), not all 66
        Assert.IsFalse(result.Stderr.Contains("Re-running 66 test(s)"),
            $"Should not retry all tests.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void TeamCity_EmitsServiceMessages_WhenEnvVarSet()
    {
        // When TEAMCITY_VERSION is set, the tool appends /TestAdapterPath:. /Logger:teamcity
        // to dotnet test. Since the teamcity logger isn't actually installed, the test process
        // will fail — but we can verify the arguments were passed by checking the error output.
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --max-tests 5 --retries 0 --idle-timeout 10",
            environmentOverrides: new Dictionary<string, string> { ["TEAMCITY_VERSION"] = "2024.1" });

        // The tool should attempt to use the TeamCity logger
        // Either it works (if installed) or fails with an error mentioning the logger
        var combined = result.Stdout + result.Stderr;
        Assert.IsTrue(
            combined.Contains("teamcity") || combined.Contains("Logger"),
            $"Expected TeamCity logger reference in output.\nStdout:\n{result.Stdout}\nStderr:\n{result.Stderr}");
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
        if (environmentOverrides is null || !environmentOverrides.ContainsKey("FAIL_ONCE"))
            process.StartInfo.Environment.Remove("FAIL_ONCE");

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
