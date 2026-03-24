using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ParallelTestRunner;

public record BatchResult(int BatchIndex, int TestCount, int ExitCode, bool TimedOut = false, IReadOnlyList<string>? CapturedOutput = null);

public static class TestRunner
{
    private static readonly object ConsoleLock = new();

    /// <summary>
    /// Runs all test batches in parallel, throttled by <paramref name="options"/>.MaxParallelism.
    /// </summary>
    public static async Task<BatchResult[]> RunAllAsync(
        IReadOnlyList<IReadOnlyList<string>> batches,
        Options options,
        CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(options.MaxParallelism);
        var trackedProcesses = new List<Process>();
        var processLock = new object();

        var totalTests = batches.Sum(b => b.Count);
        var progress = new ProgressTracker(batches.Count, totalTests);

        var tasks = batches.Select((batch, index) =>
            RunBatchAsync(batch, index, options, semaphore, trackedProcesses, processLock, progress, ct));

        try
        {
            return await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Kill any still-running child processes
            lock (processLock)
            {
                foreach (var proc in trackedProcesses)
                {
                    try
                    {
                        if (!proc.HasExited)
                            proc.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Process may have exited between check and kill
                    }
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Runs a single batch without semaphore management. Used by RetryOrchestrator for diagnostic re-runs.
    /// </summary>
    internal static async Task<BatchResult> RunSingleBatchAsync(
        IReadOnlyList<string> batch,
        int batchIndex,
        Options options,
        CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(1);
        var trackedProcesses = new List<Process>();
        var processLock = new object();
        return await RunBatchAsync(batch, batchIndex, options, semaphore, trackedProcesses, processLock, null, ct);
    }

    private static async Task<BatchResult> RunBatchAsync(
        IReadOnlyList<string> batch,
        int batchIndex,
        Options options,
        SemaphoreSlim semaphore,
        List<Process> trackedProcesses,
        object processLock,
        ProgressTracker? progress,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            var filterString = TestBatcher.BuildFilterString(batch);
            var arguments = BuildArguments(options, filterString, batchIndex);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>();
            var outputLines = new List<string>();
            var outputLock = new object();
            long lastOutputTicks = Stopwatch.GetTimestamp();

            process.Exited += (_, _) =>
            {
                try
                {
                    tcs.TrySetResult(process.ExitCode);
                }
                catch
                {
                    tcs.TrySetResult(-1);
                }
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Interlocked.Exchange(ref lastOutputTicks, Stopwatch.GetTimestamp());

                    lock (outputLock)
                    {
                        outputLines.Add(e.Data);
                    }

                    // Track individual test pass/fail for progress reporting.
                    // Only count from ##ptr lines (custom logger with FQN) to avoid
                    // double-counting when both ##ptr and display-name lines are present.
                    // Falls back to display-name lines only if no ##ptr lines exist.
                    if (progress is not null)
                    {
                        var ptrMatch = Patterns.PtrLoggerLineRegex().Match(e.Data);
                        if (ptrMatch.Success)
                        {
                            progress.SetPtrLoggerActive();
                            if (ptrMatch.Groups[1].Value == "Passed")
                                progress.IncrementPassed();
                            else if (ptrMatch.Groups[1].Value == "Failed")
                                progress.IncrementFailed();
                        }
                        else if (!progress.IsPtrLoggerActive)
                        {
                            // Fallback to display-name lines for progress if ##ptr not available
                            var match = Patterns.TestResultLineRegex().Match(e.Data);
                            if (match.Success)
                            {
                                if (match.Groups[1].Value == "Passed")
                                    progress.IncrementPassed();
                                else
                                    progress.IncrementFailed();
                            }
                        }
                    }

                    lock (ConsoleLock)
                    {
                        Console.WriteLine(e.Data);
                    }
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Interlocked.Exchange(ref lastOutputTicks, Stopwatch.GetTimestamp());

                    lock (outputLock)
                    {
                        outputLines.Add($"[stderr] {e.Data}");
                    }

                    lock (ConsoleLock)
                    {
                        Console.Error.WriteLine(e.Data);
                    }
                }
            };

            lock (processLock)
            {
                trackedProcesses.Add(process);
            }

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for exit, respecting cancellation
            using var registration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process may have already exited
                }
            });

            // Monitor for idle timeout — kill the batch if no output for IdleTimeout duration
            if (options.IdleTimeout > TimeSpan.Zero)
            {
                var idleCheckInterval = TimeSpan.FromSeconds(Math.Min(5, options.IdleTimeout.TotalSeconds / 2));

                while (!tcs.Task.IsCompleted)
                {
                    var delayTask = Task.Delay(idleCheckInterval, ct);
                    var completedTask = await Task.WhenAny(tcs.Task, delayTask);

                    if (completedTask == tcs.Task)
                        break;

                    var elapsed = Stopwatch.GetElapsedTime(Interlocked.Read(ref lastOutputTicks));
                    if (elapsed >= options.IdleTimeout)
                    {
                        try
                        {
                            if (!process.HasExited)
                                process.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                            // Process may have already exited
                        }

                        List<string> snapshot;
                        lock (outputLock)
                        {
                            snapshot = [.. outputLines];
                        }

                        lock (ConsoleLock)
                        {
                            Console.Error.WriteLine();
                            Console.Error.WriteLine($"  *** Batch {batchIndex} IDLE TIMEOUT — no output for {elapsed.TotalSeconds:F0}s ***");
                        }

                        lock (processLock)
                        {
                            trackedProcesses.Remove(process);
                        }

                        var timedOutResult = new BatchResult(batchIndex, batch.Count, -1, TimedOut: true, CapturedOutput: snapshot);
                        progress?.BatchCompleted(timedOutResult);
                        return timedOutResult;
                    }
                }
            }

            var exitCode = await tcs.Task;

            lock (processLock)
            {
                trackedProcesses.Remove(process);
            }

            ct.ThrowIfCancellationRequested();

            List<string> finalOutput;
            lock (outputLock)
            {
                finalOutput = [.. outputLines];
            }

            var result = new BatchResult(batchIndex, batch.Count, exitCode, CapturedOutput: finalOutput);
            progress?.BatchCompleted(result);
            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Resolves the directory containing the ParallelTestRunner.TestLogger.dll,
    /// which is deployed alongside the tool when packed as a dotnet tool.
    /// </summary>
    internal static string GetLoggerDirectory()
    {
        return AppContext.BaseDirectory;
    }

    private static string BuildArguments(Options options, string filterString, int batchIndex)
    {
        var args = new List<string>
        {
            "test",
            Quote(options.ProjectPath),
            "--no-build",
            "-v", "normal",
            "--filter",
            Quote(filterString)
        };

        // Always add the custom ##ptr logger for accurate FQN tracking
        var loggerDir = GetLoggerDirectory();
        args.Add("--test-adapter-path");
        args.Add(Quote(loggerDir));
        args.Add("--logger");
        args.Add("ParallelTestRunner");

        // Auto-detect TeamCity
        if (Environment.GetEnvironmentVariable("TEAMCITY_VERSION") is not null)
        {
            args.Add("--logger");
            args.Add("teamcity");
        }

        // TRX logging if results directory specified
        if (options.ResultsDirectory is not null)
        {
            args.Add("--logger");
            args.Add(Quote($"trx;LogFileName=batch_{batchIndex}.trx"));
            args.Add("--results-directory");
            args.Add(Quote(options.ResultsDirectory));
        }

        // Append any extra dotnet test args
        foreach (var extra in options.ExtraDotnetTestArgs)
        {
            args.Add(extra);
        }

        // Force sequential execution within each batch — parallelism is managed
        // at the process level by this tool, not within dotnet test.
        // Each framework ignores settings it doesn't recognise, so all three are safe to include.
        args.Add("--");
        args.Add("MSTest.Parallelize.Workers=1");
        args.Add("xUnit.MaxParallelThreads=1");
        args.Add("NUnit.NumberOfTestWorkers=1");

        return string.Join(" ", args);
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') || value.Contains('"') || value.Contains('|')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}

/// <summary>
/// Thread-safe progress tracker that counts individual test passes/failures
/// from VSTest output and reports after each batch completes.
/// </summary>
internal sealed class ProgressTracker
{
    private readonly int _totalBatches;
    private readonly int _totalTests;
    private int _completedBatches;
    private int _passedTests;
    private int _failedTests;
    private int _ptrLoggerActive;

    public ProgressTracker(int totalBatches, int totalTests)
    {
        _totalBatches = totalBatches;
        _totalTests = totalTests;
    }

    public void IncrementPassed() => Interlocked.Increment(ref _passedTests);
    public void IncrementFailed() => Interlocked.Increment(ref _failedTests);

    /// <summary>Marks that ##ptr lines have been seen, disabling display-name fallback counting.</summary>
    public void SetPtrLoggerActive() => Interlocked.Exchange(ref _ptrLoggerActive, 1);

    /// <summary>Returns true if ##ptr lines have been seen from any batch.</summary>
    public bool IsPtrLoggerActive => Volatile.Read(ref _ptrLoggerActive) == 1;

    public void BatchCompleted(BatchResult result)
    {
        var completed = Interlocked.Increment(ref _completedBatches);
        var passed = Volatile.Read(ref _passedTests);
        var failed = Volatile.Read(ref _failedTests);
        var tested = passed + failed;
        var batchesRemaining = _totalBatches - completed;

        var prefix = result.TimedOut
            ? $"  TIMED OUT [{completed}/{_totalBatches} batches] "
            : $"  [{completed}/{_totalBatches} batches] ";

        Console.Error.WriteLine(
            prefix +
            $"{passed} passed | {failed} failed | {tested} executed" +
            (batchesRemaining > 0 ? $" | {batchesRemaining} batches remaining" : ""));
    }
}
