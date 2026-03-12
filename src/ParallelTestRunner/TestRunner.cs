using System.Diagnostics;

namespace ParallelTestRunner;

public record BatchResult(int BatchIndex, int TestCount, int ExitCode);

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
                            proc.Kill();
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
                        process.Kill();
                }
                catch
                {
                    // Process may have already exited
                }
            });

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

        return string.Join(" ", args);
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') || value.Contains('"') || value.Contains('|')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
