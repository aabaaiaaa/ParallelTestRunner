using System.Diagnostics;

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

        var tasks = batches.Select((batch, index) =>
            RunBatchAsync(batch, index, options, semaphore, trackedProcesses, processLock, ct));

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
    /// Runs a single batch without semaphore management. Used by HangDetector for diagnostic re-runs.
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
        return await RunBatchAsync(batch, batchIndex, options, semaphore, trackedProcesses, processLock, ct);
    }

    private static async Task<BatchResult> RunBatchAsync(
        IReadOnlyList<string> batch,
        int batchIndex,
        Options options,
        SemaphoreSlim semaphore,
        List<Process> trackedProcesses,
        object processLock,
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

                        return new BatchResult(batchIndex, batch.Count, -1, TimedOut: true, CapturedOutput: snapshot);
                    }
                }
            }

            var exitCode = await tcs.Task;
            ct.ThrowIfCancellationRequested();

            return new BatchResult(batchIndex, batch.Count, exitCode);
        }
        finally
        {
            semaphore.Release();
        }
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

        // Auto-detect TeamCity
        if (Environment.GetEnvironmentVariable("TEAMCITY_VERSION") is not null)
        {
            args.Add("/TestAdapterPath:.");
            args.Add("/Logger:teamcity");
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
        // at the process level by this tool, not within dotnet test
        args.Add("--");
        args.Add("MSTest.Parallelize.Workers=1");

        return string.Join(" ", args);
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') || value.Contains('"') || value.Contains('|')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
