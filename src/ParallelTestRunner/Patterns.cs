using System.Text.RegularExpressions;

namespace ParallelTestRunner;

/// <summary>
/// Shared regex patterns used across the pipeline.
/// </summary>
public static partial class Patterns
{
    /// <summary>
    /// Matches "  Passed TestName [Xs]" or "  Failed TestName [Xs]" from VSTest normal verbosity.
    /// Group 1 = status (Passed/Failed), Group 2 = test name.
    /// </summary>
    [GeneratedRegex(@"^\s+(Passed|Failed)\s+(.+?)\s+\[")]
    public static partial Regex TestResultLineRegex();
}
