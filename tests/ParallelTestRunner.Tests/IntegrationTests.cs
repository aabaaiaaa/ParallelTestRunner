using System.Diagnostics;
using System.Reflection;

namespace ParallelTestRunner.Tests;

[TestClass]
public class IntegrationTests
{
    private static string _solutionRoot = null!;
    private static string _dummyProjectPath = null!;
    private static string _dummyXUnitProjectPath = null!;
    private static string _dummyNUnitProjectPath = null!;
    private static string _toolProjectPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _solutionRoot = FindSolutionRoot();
        _dummyProjectPath = Path.Combine(_solutionRoot, "tests", "DummyTestProject", "DummyTestProject.csproj");
        _dummyXUnitProjectPath = Path.Combine(_solutionRoot, "tests", "DummyTestProject.XUnit", "DummyTestProject.XUnit.csproj");
        _dummyNUnitProjectPath = Path.Combine(_solutionRoot, "tests", "DummyTestProject.NUnit", "DummyTestProject.NUnit.csproj");
        _toolProjectPath = Path.Combine(_solutionRoot, "src", "ParallelTestRunner", "ParallelTestRunner.csproj");

        Assert.IsTrue(File.Exists(_dummyProjectPath), $"DummyTestProject not found at {_dummyProjectPath}");
        Assert.IsTrue(File.Exists(_dummyXUnitProjectPath), $"DummyTestProject.XUnit not found at {_dummyXUnitProjectPath}");
        Assert.IsTrue(File.Exists(_dummyNUnitProjectPath), $"DummyTestProject.NUnit not found at {_dummyNUnitProjectPath}");
        Assert.IsTrue(File.Exists(_toolProjectPath), $"Tool project not found at {_toolProjectPath}");

        // Build all dummy projects since the tool always passes --no-build
        var buildResult = RunProcess("dotnet", $"build \"{_dummyProjectPath}\" -c Debug --nologo -v q");
        Assert.AreEqual(0, buildResult.ExitCode, $"Failed to build DummyTestProject:\n{buildResult.Stderr}");

        var xunitBuild = RunProcess("dotnet", $"build \"{_dummyXUnitProjectPath}\" -c Debug --nologo -v q");
        Assert.AreEqual(0, xunitBuild.ExitCode, $"Failed to build DummyTestProject.XUnit:\n{xunitBuild.Stderr}");

        var nunitBuild = RunProcess("dotnet", $"build \"{_dummyNUnitProjectPath}\" -c Debug --nologo -v q");
        Assert.AreEqual(0, nunitBuild.ExitCode, $"Failed to build DummyTestProject.NUnit:\n{nunitBuild.Stderr}");
    }

    [TestMethod]
    public void NonexistentProject_ExitCode2WithErrorMessage()
    {
        var result = RunTool("\"C:/nonexistent/Fake.csproj\" --retries 0");

        Assert.AreEqual(2, result.ExitCode, $"Expected exit code 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Project file not found");
    }

    [TestMethod]
    public void Discovery_FindsAllTests()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-tests 10 --max-parallelism 8 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 70 tests");
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
        // that fail if >1 test runs concurrently. Using --workers 1 to force sequential execution.
        // Must use --max-parallelism 1 here so all sequential tests run in the same batch/process.
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --workers 1 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode,
            $"Expected exit code 0 — SequentialCheck tests likely detected parallel execution.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void LongFilterString_AutoSplitsWhenExceedingLimit()
    {
        // 70 tests with FQN prefix exceeds 7000-char filter limit, auto-splits into batches
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 8 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 70 tests");
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
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 10 --max-tests 20 --max-parallelism 8 --idle-timeout 30 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Created 2 batches");
    }

    [TestMethod]
    public void SkipTests_SkipsFirstN()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --skip-tests 50 --batch-size 100 --max-parallelism 8 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 70 tests");
        StringAssert.Contains(result.Stderr, "Skipped first 50 tests, 20 remaining");
    }

    [TestMethod]
    public void SkipTests_CombinedWithMaxTests()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --skip-tests 5 --max-tests 10 --batch-size 100 --max-parallelism 8 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Skipped first 5 tests, 65 remaining");
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
        StringAssert.Contains(result.Stderr, "retry)");
    }

    [TestMethod]
    public void Retries_PersistentFailure_StillFails()
    {
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 8 --retries 2 --idle-timeout 10",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_TESTS"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        // Unified loop runs retry rounds — verify at least 2 rounds occurred
        StringAssert.Contains(result.Stderr, "Round 1");
        StringAssert.Contains(result.Stderr, "Round 2");
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
        StringAssert.Contains(result.Stderr, "retry)");
        StringAssert.Contains(result.Stderr, "Auto-retry: enabled");
    }

    [TestMethod]
    public void AutoRetry_OnlyRetriesFailedBatches_NotPassingTests()
    {
        // With batch-size 5, the 3 FailableTests end up in a batch.
        // The other batches pass. Auto-retry should only re-run the failed batch's tests,
        // not all 70 tests, and stop after 1 round of no progress.
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 5 --max-parallelism 8 --auto-retry --idle-timeout 30",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_TESTS"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        // Auto-retry should stop after detecting no progress — proves it doesn't loop
        StringAssert.Contains(result.Stderr, "Auto-retry: no progress this round");
        // Only the failed batch's tests should be retried (6 or fewer), not all 66
        Assert.IsFalse(result.Stderr.Contains("Re-running 70 test(s)"),
            $"Should not retry all tests.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void TeamCity_EmitsServiceMessages_WhenEnvVarSet()
    {
        // When TEAMCITY_VERSION is set, the tool appends --logger teamcity to dotnet test.
        // Since the teamcity logger isn't actually installed, the batch will fail.
        // We verify the tool detected TeamCity by confirming the batch failed (the logger
        // isn't installed so dotnet test errors out) and that no tests executed (0 ##ptr lines).
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --max-tests 5 --retries 0 --idle-timeout 10",
            environmentOverrides: new Dictionary<string, string> { ["TEAMCITY_VERSION"] = "2024.1" });

        // The batch should fail because the TeamCity logger isn't installed
        Assert.AreEqual(1, result.ExitCode,
            $"Expected exit code 1 (TeamCity logger not installed should cause failure).\nStderr:\n{result.Stderr}");
        // No tests should have executed (the process fails before running tests)
        Assert.IsFalse(result.Stdout.Contains("##ptr[Passed"),
            $"No tests should pass when TeamCity logger is missing.\nStdout:\n{result.Stdout}");
    }

    [TestMethod]
    public void Logger_EmitsPtrLinesWithFqn()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-tests 5 --max-parallelism 1 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        // The custom logger should emit ##ptr lines with FQN data
        Assert.IsTrue(result.Stdout.Contains("##ptr[Passed|FQN="),
            $"Expected ##ptr lines in stdout.\nStdout:\n{result.Stdout}");
    }

    [TestMethod]
    public void Logger_EmitsDisplayNameSeparateFromFqn()
    {
        // Run enough tests to include the DisplayName tests
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        // Verify a ##ptr line exists where FQN contains the method name and Name contains the display name
        Assert.IsTrue(result.Stdout.Contains("FQN=DummyTestProject.DisplayNames.DisplayNameTests.Addition_PositiveNumbers_ReturnsSum"),
            $"Expected FQN for display name test in stdout.\nStdout:\n{result.Stdout}");
        Assert.IsTrue(result.Stdout.Contains("Name=Adding two positive numbers returns correct sum"),
            $"Expected display name in ##ptr line.\nStdout:\n{result.Stdout}");
    }

    [TestMethod]
    public void Logger_RetryIdentifiesFailedTestByFqn_NotDisplayName()
    {
        // Trigger failure on the display-name test, then verify retry output references the FQN.
        // Uses --retries 2 so that persistent failure detection kicks in (needs 2 consecutive rounds).
        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 2 --idle-timeout 30",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_DISPLAY_NAME_TESTS"] = "1" });

        Assert.AreEqual(1, result.ExitCode, $"Expected exit code 1 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        // The retry should only retry the 1 failed test — verify via "1 retry" in round label
        StringAssert.Contains(result.Stderr, "1 retry)");
        // The persistent failure should be reported by FQN, not display name
        Assert.IsTrue(result.Stderr.Contains("DisplayName_ConditionalFailure"),
            $"Expected FQN-based persistent failure report.\nStderr:\n{result.Stderr}");
        // Should NOT contain only the display name without FQN context
        Assert.IsFalse(result.Stderr.Contains("Persistent failure: This display name test should fail"),
            $"Persistent failure should use FQN, not display name.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void Logger_ProgressCountsAccurateWithDisplayNames()
    {
        // Run all tests including display-name tests — progress should count correctly
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        // 70 tests total (66 original + 4 display name tests)
        StringAssert.Contains(result.Stderr, "Discovered 70 tests");
    }

    [TestMethod]
    public void FilterExpression_FiltersTestsAtDiscovery()
    {
        // Filter to only Arithmetic namespace tests — should discover fewer than the full 70
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --filter-expression \"FullyQualifiedName~Arithmetic\"");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 9 tests");
    }

    [TestMethod]
    public void FilterExpression_WithExclusion_FiltersCorrectly()
    {
        // Exclude Arithmetic tests — should discover 70 - 9 = 61 tests
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --filter-expression \"FullyQualifiedName!~Arithmetic\"");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 61 tests");
    }

    [TestMethod]
    public void FilterExpression_WithAndOperator_CombinesFilters()
    {
        // Filter to Arithmetic AND exclude parameterized — should discover base methods only
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --filter-expression \"FullyQualifiedName~Arithmetic&FullyQualifiedName!~Parameterized\"");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        // 9 Arithmetic tests minus 2 parameterized = 7
        StringAssert.Contains(result.Stderr, "Discovered 7 tests");
    }

    [TestMethod]
    public void XUnit_SequentialExecution_ForcesWorkersToOne()
    {
        // xUnit runs test collections in parallel by default. Using --workers 1 to force
        // sequential execution via xUnit.MaxParallelThreads=1.
        // Must use --max-parallelism 1 so all sequential tests run in the same batch/process.
        var result = RunTool($"\"{_dummyXUnitProjectPath}\" --batch-size 100 --max-parallelism 1 --workers 1 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode,
            $"Expected exit code 0 — xUnit SequentialCheck tests likely detected parallel execution.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void NUnit_SequentialExecution_ForcesWorkersToOne()
    {
        // NUnit has [assembly: Parallelizable(ParallelScope.All)] enabling parallel execution.
        // Using --workers 1 to force sequential execution via NUnit.NumberOfTestWorkers=1.
        // Must use --max-parallelism 1 so all sequential tests run in the same batch/process.
        var result = RunTool($"\"{_dummyNUnitProjectPath}\" --batch-size 100 --max-parallelism 1 --workers 1 --idle-timeout 10 --retries 0");

        Assert.AreEqual(0, result.ExitCode,
            $"Expected exit code 0 — NUnit SequentialCheck tests likely detected parallel execution.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void TestList_RunsFromString_SkipsDiscovery()
    {
        var testList = "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult|DummyTestProject.Arithmetic.BasicMathTests.Subtraction_ReturnsCorrectResult|DummyTestProject.Arithmetic.BasicMathTests.Multiplication_ReturnsCorrectResult";

        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list \"{testList}\"");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Skipping test discovery");
        StringAssert.Contains(result.Stderr, "Total: 3 tests");
        Assert.IsFalse(result.Stderr.Contains("Discovered"), $"Discovery should be skipped.\nStderr:\n{result.Stderr}");
        // Verify ##ptr logger output is present — tests must be reported
        Assert.IsTrue(result.Stdout.Contains("##ptr[Passed|FQN=DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult"),
            $"Expected ##ptr output for Addition test.\nStdout:\n{result.Stdout}");
        Assert.IsTrue(result.Stdout.Contains("##ptr[Passed|FQN=DummyTestProject.Arithmetic.BasicMathTests.Subtraction_ReturnsCorrectResult"),
            $"Expected ##ptr output for Subtraction test.\nStdout:\n{result.Stdout}");
        Assert.IsTrue(result.Stdout.Contains("##ptr[Passed|FQN=DummyTestProject.Arithmetic.BasicMathTests.Multiplication_ReturnsCorrectResult"),
            $"Expected ##ptr output for Multiplication test.\nStdout:\n{result.Stdout}");
        // Verify progress tracking works in stderr
        StringAssert.Contains(result.Stderr, "3 passed");
    }

    [TestMethod]
    public void TestList_EmptyString_FallsBackToDiscovery()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --max-tests 5 --retries 0 --test-list \"\"");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 70 tests");
        Assert.IsFalse(result.Stderr.Contains("Test list: provided"),
            $"Banner should not show test list for empty string.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void TestList_NoValue_FallsBackToDiscovery()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --max-tests 5 --retries 0 --test-list");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 70 tests");
        Assert.IsFalse(result.Stderr.Contains("Test list: provided"),
            $"Banner should not show test list when no value given.\nStderr:\n{result.Stderr}");
    }

    [TestMethod]
    public void TestList_IgnoresFilterExpression()
    {
        var testList = "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult";

        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list \"{testList}\" --filter-expression \"FullyQualifiedName~Arithmetic\"");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Skipping test discovery");
        StringAssert.Contains(result.Stderr, "--filter-expression ignored");
        StringAssert.Contains(result.Stderr, "Total: 1 tests");
        // Verify ##ptr logger output is present
        Assert.IsTrue(result.Stdout.Contains("##ptr[Passed|FQN=DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult"),
            $"Expected ##ptr output for test.\nStdout:\n{result.Stdout}");
        StringAssert.Contains(result.Stderr, "1 passed");
    }

    [TestMethod]
    public void TestList_WithRetries_RetryStillWorks()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), "parallel_test_runner_fail_once.marker");
        if (File.Exists(markerPath))
            File.Delete(markerPath);

        var testList = "DummyTestProject.Arithmetic.TransientFailureTests.TransientFailure_FailsOnceThenPasses|DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult";

        var result = RunTool(
            $"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 1 --idle-timeout 10 --test-list \"{testList}\"",
            environmentOverrides: new Dictionary<string, string> { ["FAIL_ONCE"] = "1" });

        Assert.AreEqual(0, result.ExitCode, $"Expected exit code 0 (retry should recover) but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Skipping test discovery");
        StringAssert.Contains(result.Stderr, "retry)");
        // Verify ##ptr logger output is present after retry
        Assert.IsTrue(result.Stdout.Contains("##ptr[Passed|FQN=DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult"),
            $"Expected ##ptr output for Addition test.\nStdout:\n{result.Stdout}");
    }

    [TestMethod]
    public void TestList_NonMatchingFqns_ExitCode2()
    {
        var testList = "Nonexistent.Namespace.FakeClass.FakeTest|Another.Fake.Test";

        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list \"{testList}\"");

        Assert.AreEqual(2, result.ExitCode, $"Expected exit code 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "zero tests were executed");
        StringAssert.Contains(result.Stderr, "Verify the fully-qualified test names are correct");
    }

    [TestMethod]
    public void TestList_VstestFilterSyntax_ExitCode2WithValidationError()
    {
        // The user's actual broken case: vstest TestCaseFilter expression accidentally
        // pasted into --test-list. Should fail validation upfront, not slip through to
        // dotnet test as junk filters.
        var badInput = "(FullNameMatchesRegex '(\\.RequestingPolicyDetails$)|(\\.GetQuoteAfterCopy$)')";

        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list \"{badInput}\"");

        Assert.AreEqual(2, result.ExitCode, $"Expected exit code 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "ERROR: --test-list contains values that don't look like fully-qualified test names");
    }

    [TestMethod]
    public void TestList_PartiallyValidInput_ExitCode2ListsBadSegments()
    {
        var mixed = "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult|bad input here|DummyTestProject.Arithmetic.BasicMathTests.Subtraction_ReturnsCorrectResult";

        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list \"{mixed}\"");

        Assert.AreEqual(2, result.ExitCode, $"Expected exit code 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "bad input here");
    }

    [TestMethod]
    public void TestListFile_AllDummyTests_RunsSuccessfully()
    {
        var fqns = new[]
        {
            "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult",
            "DummyTestProject.Arithmetic.BasicMathTests.Subtraction_ReturnsCorrectResult",
            "DummyTestProject.Arithmetic.BasicMathTests.Multiplication_ReturnsCorrectResult",
        };
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, fqns);

            var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list-file \"{path}\"");

            Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
            StringAssert.Contains(result.Stderr, "Skipping test discovery");
            StringAssert.Contains(result.Stderr, $"Test list: provided from {path}");
            StringAssert.Contains(result.Stderr, "Total: 3 tests");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void TestListFile_VeryLongList_OsInvocationSucceeds()
    {
        // 250 unique FQN-shaped strings, ~80 chars each = ~20k chars.
        // Far past the 16k cmd.exe limit and approaching the 32k CreateProcess limit.
        // The FQNs are syntactically valid (validator passes) but match nothing in the assembly.
        // Expected exit: 2 (zero tests executed) — the *point* is that we got that far without an OS-level launch failure.
        var path = Path.GetTempFileName();
        try
        {
            var lines = new List<string>();
            for (var i = 0; i < 250; i++)
                lines.Add($"VeryLongFakeNamespace_PaddingForTheCommandLineLengthTest.SubNs{i:D3}.FakeTestClass.FakeTestMethod_PaddedSuffix_{i:D3}");
            File.WriteAllLines(path, lines);
            Assert.IsTrue(new FileInfo(path).Length > 16000, "Test setup invariant: file must exceed 16k char threshold");

            var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list-file \"{path}\"");

            Assert.AreEqual(2, result.ExitCode, $"Expected exit 2 (zero tests executed), got {result.ExitCode}.\nStderr:\n{result.Stderr}");
            StringAssert.Contains(result.Stderr, "zero tests were executed");
            Assert.IsFalse(result.Stderr.Contains("filename or extension is too long"),
                "OS-level launch failure detected — the long list was passed via argv instead of via the file");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void TestListFile_AndTestList_BothProvided_ExitCode2()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult");
            var result = RunTool($"\"{_dummyProjectPath}\" --retries 0 --test-list \"Some.Test.Name\" --test-list-file \"{path}\"");
            Assert.AreEqual(2, result.ExitCode, $"Expected exit 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
            StringAssert.Contains(result.Stderr, "mutually exclusive");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void TestListFile_MissingFile_ExitCode2()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "definitely_not_a_real_file_" + Guid.NewGuid() + ".txt");
        var result = RunTool($"\"{_dummyProjectPath}\" --retries 0 --test-list-file \"{fakePath}\"");
        Assert.AreEqual(2, result.ExitCode, $"Expected exit 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "path not found");
    }

    [TestMethod]
    public void TestListFile_InvalidContent_ExitCode2()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "(FullNameMatchesRegex '(\\.X$)')");
            var result = RunTool($"\"{_dummyProjectPath}\" --retries 0 --test-list-file \"{path}\"");
            Assert.AreEqual(2, result.ExitCode, $"Expected exit 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
            StringAssert.Contains(result.Stderr, "ERROR: --test-list contains values that don't look like fully-qualified test names");
        }
        finally { File.Delete(path); }
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
        if (environmentOverrides is null || !environmentOverrides.ContainsKey("FAIL_DISPLAY_NAME_TESTS"))
            process.StartInfo.Environment.Remove("FAIL_DISPLAY_NAME_TESTS");

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
