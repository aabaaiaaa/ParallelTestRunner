using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace ParallelTestRunner.TestLogger;

/// <summary>
/// Custom VSTest logger that emits structured ##ptr lines to stdout in real-time.
/// Each line contains the test outcome, fully qualified name, and display name,
/// enabling the ParallelTestRunner tool to accurately match test results back to
/// the FQNs used for filtering — even when display names differ from FQNs.
/// </summary>
[FriendlyName("ParallelTestRunner")]
[ExtensionUri("logger://ParallelTestRunner")]
public class PtrLogger : ITestLoggerWithParameters
{
    /// <summary>
    /// Line prefix used by the tool to identify structured logger output.
    /// </summary>
    public const string Prefix = "##ptr";

    public void Initialize(TestLoggerEvents events, string testRunDirectory)
    {
        events.TestResult += OnTestResult;
    }

    public void Initialize(TestLoggerEvents events, Dictionary<string, string?> parameters)
    {
        events.TestResult += OnTestResult;
    }

    private static void OnTestResult(object? sender, TestResultEventArgs e)
    {
        var outcome = e.Result.Outcome switch
        {
            TestOutcome.Passed => "Passed",
            TestOutcome.Failed => "Failed",
            TestOutcome.Skipped => "Skipped",
            TestOutcome.NotFound => "NotFound",
            _ => "None"
        };

        var fqn = e.Result.TestCase.FullyQualifiedName;
        var displayName = e.Result.TestCase.DisplayName;

        // Use pipe-delimited format: ##ptr[Outcome|FQN=...|Name=...]
        // Pipe is safe as a delimiter because FQNs and display names don't contain pipes.
        Console.WriteLine($"{Prefix}[{outcome}|FQN={fqn}|Name={displayName}]");
    }
}
