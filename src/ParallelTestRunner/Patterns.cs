using System.Text.RegularExpressions;

namespace ParallelTestRunner;

/// <summary>
/// Shared regex patterns used across the pipeline.
/// </summary>
public static partial class Patterns
{
    /// <summary>
    /// Matches "  Passed TestName [Xs]" or "  Failed TestName [Xs]" from VSTest normal verbosity.
    /// Group 1 = status (Passed/Failed), Group 2 = test name (display name, not FQN).
    /// Used only for progress counting (pass/fail totals), not for FQN matching.
    /// </summary>
    [GeneratedRegex(@"^\s+(Passed|Failed)\s+(.+?)\s+\[")]
    public static partial Regex TestResultLineRegex();

    /// <summary>
    /// Matches ##ptr lines emitted by the custom ParallelTestRunner.TestLogger.
    /// Format: ##ptr[Outcome|FQN=Ns.Class.Method|Name=Display Name]
    /// Group 1 = outcome (Passed/Failed/Skipped/NotFound/None), Group 2 = FQN, Group 3 = display name.
    /// </summary>
    [GeneratedRegex(@"^##ptr\[(Passed|Failed|Skipped|NotFound|None)\|FQN=(.+?)\|Name=(.+?)\]$")]
    public static partial Regex PtrLoggerLineRegex();
}
