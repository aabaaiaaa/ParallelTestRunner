using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ParallelTestRunner;

public static partial class TestDiscovery
{
    // Matches "Test run for <path>.dll" to extract the DLL path from dotnet test output
    [GeneratedRegex(@"^Test run for (.+\.dll)")]
    private static partial Regex DllPathRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$")]
    private static partial Regex FqnShapeRegex();

    /// <summary>
    /// Discovers tests by first resolving the test DLL path via <c>dotnet test --list-tests</c>,
    /// then extracting fully-qualified test names via <c>dotnet vstest --ListFullyQualifiedTests</c>.
    /// </summary>
    /// <returns>A deduplicated list of fully-qualified test names.</returns>
    public static async Task<IReadOnlyList<string>> DiscoverAsync(
        string projectPath, string[] extraArgs, string? filterExpression, CancellationToken ct)
    {
        // Step 1: Run dotnet test --list-tests to discover the DLL path
        var dllPath = await ResolveDllPathAsync(projectPath, extraArgs, ct);
        Console.Error.WriteLine($"  Resolved test assembly: {dllPath}");

        // Step 2: Use dotnet vstest to get fully-qualified test names
        var tests = await DiscoverFqnTestsAsync(dllPath, filterExpression, ct);

        if (tests.Count == 0)
            throw new InvalidOperationException(
                "Zero tests discovered. Verify the project contains test methods and has been built.");

        return tests;
    }

    /// <summary>
    /// Runs <c>dotnet test --list-tests --no-build</c> and parses the DLL path from the
    /// "Test run for ..." output line.
    /// </summary>
    private static async Task<string> ResolveDllPathAsync(
        string projectPath, string[] extraArgs, CancellationToken ct)
    {
        var args = BuildListTestsArguments(projectPath, extraArgs);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        var stdoutLines = new ConcurrentQueue<string>();
        var stderrLines = new ConcurrentQueue<string>();
        var tcs = new TaskCompletionSource<int>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdoutLines.Enqueue(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrLines.Enqueue(e.Data);
                Console.Error.WriteLine(e.Data);
            }
        };

        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* process may have already exited */ }
        });

        var exitCode = await tcs.Task;
        ct.ThrowIfCancellationRequested();

        if (exitCode != 0)
        {
            var stderr = string.Join(Environment.NewLine, stderrLines);
            throw new InvalidOperationException(
                $"dotnet test --list-tests exited with code {exitCode}.{(stderr.Length > 0 ? $"\n{stderr}" : "")}");
        }

        // Parse DLL path from "Test run for <path>.dll (...)" line
        var regex = DllPathRegex();
        foreach (var line in stdoutLines)
        {
            var match = regex.Match(line);
            if (match.Success)
                return match.Groups[1].Value;
        }

        throw new InvalidOperationException(
            "Could not determine test assembly path from dotnet test --list-tests output.");
    }

    /// <summary>
    /// Runs <c>dotnet vstest &lt;dll&gt; --ListFullyQualifiedTests</c> to get FQN test names.
    /// </summary>
    /// <remarks>
    /// <c>dotnet vstest</c> is deprecated by Microsoft in favour of <c>dotnet test</c>.
    /// However, as of .NET 10, <c>dotnet test --list-tests</c> only returns display names,
    /// not fully-qualified names. Since FQN filtering is essential for precise batch execution,
    /// we continue to use <c>dotnet vstest --ListFullyQualifiedTests</c> until a future
    /// <c>dotnet test</c> version provides equivalent FQN discovery capability.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> DiscoverFqnTestsAsync(
        string dllPath, string? filterExpression, CancellationToken ct)
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var args = $"vstest \"{dllPath}\" --ListFullyQualifiedTests --ListTestsTargetPath:\"{tempFile}\"";
            if (filterExpression is not null)
                args += $" --TestCaseFilter:\"{filterExpression}\"";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };

            var tcs = new TaskCompletionSource<int>();

            process.OutputDataReceived += (_, e) =>
            {
                // Suppress stdout — FQNs go to the target file
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    Console.Error.WriteLine(e.Data);
            };

            process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var registration = ct.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* process may have already exited */ }
            });

            var exitCode = await tcs.Task;
            ct.ThrowIfCancellationRequested();

            // dotnet vstest --ListFullyQualifiedTests exits with 0 even on success
            // Read the target file for FQN test names
            if (!File.Exists(tempFile))
                throw new InvalidOperationException(
                    "dotnet vstest --ListFullyQualifiedTests did not produce output file.");

            var lines = await File.ReadAllLinesAsync(tempFile, ct);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && seen.Add(trimmed))
                    result.Add(trimmed);
            }

            return result;
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch (IOException) { /* best effort cleanup */ }
        }
    }

    private static string BuildListTestsArguments(string projectPath, string[] extraArgs)
    {
        var parts = new List<string>
        {
            "test",
            $"\"{projectPath}\"",
            "--list-tests",
            "--no-build",
        };

        foreach (var arg in extraArgs)
            parts.Add(arg);

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Parses a pipe-delimited string of fully-qualified test names into a deduplicated list.
    /// Returns an empty list if the input is null, empty, or whitespace-only.
    /// </summary>
    internal static IReadOnlyList<string> ParseTestList(string? testList)
    {
        if (string.IsNullOrWhiteSpace(testList))
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var segment in testList.Split('|'))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    internal static IReadOnlyList<string> ParseTestListFile(string path)
        => throw new NotImplementedException();

    // Kept internal for unit tests that test parsing logic
    internal static IReadOnlyList<string> ParseDiscoveryOutput(List<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    /// <summary>
    /// Validates that each segment in the supplied test list looks like a fully-qualified
    /// test name (dot-separated identifiers) rather than a VSTest filter expression or other
    /// malformed input. Returns a list of failures describing which segments are invalid.
    /// </summary>
    internal static IReadOnlyList<TestListValidationFailure> ValidateTestList(IReadOnlyList<string> segments)
    {
        var failures = new List<TestListValidationFailure>();
        var regex = FqnShapeRegex();

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            if (string.IsNullOrWhiteSpace(segment))
            {
                failures.Add(new TestListValidationFailure(i, segment, "empty segment"));
                continue;
            }

            if (regex.IsMatch(segment))
                continue;

            failures.Add(new TestListValidationFailure(i, segment, DescribeFailure(segment)));
        }

        return failures;
    }

    private static string DescribeFailure(string segment)
    {
        if (segment.Contains('('))   return "contains parenthesis '(' — looks like a filter expression, not an FQN";
        if (segment.Contains(')'))   return "contains parenthesis ')' — looks like a filter expression, not an FQN";
        if (segment.Contains('\''))  return "contains single quote — looks like a filter expression, not an FQN";
        if (segment.Contains('='))   return "contains '=' — looks like a filter expression (e.g. FullyQualifiedName=...)";
        if (segment.Contains('~'))   return "contains '~' — looks like a filter expression";
        if (segment.Contains('$'))   return "contains regex anchor '$'";
        if (segment.Contains('^'))   return "contains regex anchor '^'";
        if (segment.Contains(' '))   return "contains spaces — FQNs do not contain spaces";
        if (!segment.Contains('.'))  return "no dots — expected at least Namespace.Class.Method";
        if (char.IsDigit(segment[0])) return "starts with a digit — identifiers must start with a letter or underscore";
        return "not a valid dotted identifier (Namespace.Class.Method shape)";
    }
}

internal sealed record TestListValidationFailure(int Index, string Segment, string Reason);
