using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ParallelTestRunner;

public static partial class TestDiscovery
{
    // Matches "Test run for <path>.dll" to extract the DLL path from dotnet test output
    [GeneratedRegex(@"^Test run for (.+\.dll)")]
    private static partial Regex DllPathRegex();

    /// <summary>
    /// Discovers tests by first resolving the test DLL path via <c>dotnet test --list-tests</c>,
    /// then extracting fully-qualified test names via <c>dotnet vstest --ListFullyQualifiedTests</c>.
    /// </summary>
    /// <returns>A deduplicated list of fully-qualified test names.</returns>
    public static async Task<IReadOnlyList<string>> DiscoverAsync(
        string projectPath, string[] extraArgs, CancellationToken ct)
    {
        // Step 1: Run dotnet test --list-tests to discover the DLL path
        var dllPath = await ResolveDllPathAsync(projectPath, extraArgs, ct);
        Console.Error.WriteLine($"  Resolved test assembly: {dllPath}");

        // Step 2: Use dotnet vstest to get fully-qualified test names
        var tests = await DiscoverFqnTestsAsync(dllPath, ct);

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
        var tcs = new TaskCompletionSource<int>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdoutLines.Enqueue(e.Data);
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
            catch { /* process may have already exited */ }
        });

        var exitCode = await tcs.Task;
        ct.ThrowIfCancellationRequested();

        if (exitCode != 0)
            throw new InvalidOperationException(
                $"dotnet test --list-tests exited with code {exitCode}.");

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
        string dllPath, CancellationToken ct)
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var args = $"vstest \"{dllPath}\" --ListFullyQualifiedTests --ListTestsTargetPath:\"{tempFile}\"";

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
                catch { /* process may have already exited */ }
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
            catch { /* best effort cleanup */ }
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
}
