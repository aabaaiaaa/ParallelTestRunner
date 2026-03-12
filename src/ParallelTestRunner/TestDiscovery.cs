using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ParallelTestRunner;

public static partial class TestDiscovery
{
    private const string Sentinel = "The following Tests are available:";

    [GeneratedRegex(@"\(.*\)$")]
    private static partial Regex ParameterSuffixRegex();

    /// <summary>
    /// Discovers tests by running <c>dotnet test --list-tests --no-build</c> and parsing the output.
    /// </summary>
    /// <returns>A deduplicated list of fully-qualified test names.</returns>
    public static async Task<IReadOnlyList<string>> DiscoverAsync(
        string projectPath, string[] extraArgs, CancellationToken ct)
    {
        var args = BuildArguments(projectPath, extraArgs);

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

        var stdoutLines = new List<string>();
        var tcs = new TaskCompletionSource<int>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdoutLines.Add(e.Data);
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

        var tests = ParseDiscoveryOutput(stdoutLines);

        if (tests.Count == 0)
            throw new InvalidOperationException(
                "Zero tests discovered. Verify the project contains test methods and has been built.");

        return tests;
    }

    private static string BuildArguments(string projectPath, string[] extraArgs)
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

    internal static IReadOnlyList<string> ParseDiscoveryOutput(List<string> lines)
    {
        var sentinelIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimEnd() == Sentinel)
            {
                sentinelIndex = i;
                break;
            }
        }

        if (sentinelIndex < 0)
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        for (var i = sentinelIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Lines after the sentinel are indented test names
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            // Deduplicate parameterized tests by stripping (...) suffix
            var deduped = ParameterSuffixRegex().Replace(trimmed, "");

            if (seen.Add(deduped))
                result.Add(deduped);
        }

        return result;
    }
}
