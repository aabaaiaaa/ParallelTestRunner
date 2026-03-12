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
        StringAssert.Contains(result.Stderr, "Discovered 20 tests");
    }

    [TestMethod]
    public void RunWithBatching_AllTestsExecute()
    {
        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 5 --max-parallelism 2");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Discovered 20 tests");
        // With batch size 5 and 20 tests, expect 4 batches
        StringAssert.Contains(result.Stderr, "Created 4 batches");
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

        // Make sure FAIL_TESTS is not set unless explicitly provided
        if (environmentOverrides is null || !environmentOverrides.ContainsKey("FAIL_TESTS"))
            process.StartInfo.Environment.Remove("FAIL_TESTS");

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
