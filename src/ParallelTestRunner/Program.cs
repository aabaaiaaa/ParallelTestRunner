using System.CommandLine;
using System.Reflection;
using System.Text;
using ParallelTestRunner;

// Force UTF-8 so the banner's box-drawing and shading chars render correctly across
// hosts whose default code page is CP437/CP1252 (e.g. TeamCity Windows agents).
Console.OutputEncoding = Encoding.UTF8;

var projectArg = new Argument<string>("project")
{
    Description = "Path to the test project or solution"
};

var batchSizeOption = new Option<int>("--batch-size")
{
    Description = "Number of tests per batch",
    DefaultValueFactory = _ => 50
};
batchSizeOption.Validators.Add(result =>
{
    var value = result.GetValue(batchSizeOption);
    if (value < 1)
        result.AddError("--batch-size must be at least 1.");
});

var maxParallelismOption = new Option<int>("--max-parallelism")
{
    Description = "Maximum number of concurrent dotnet test processes",
    DefaultValueFactory = _ => Math.Max(1, Environment.ProcessorCount / 2)
};
maxParallelismOption.Validators.Add(result =>
{
    var value = result.GetValue(maxParallelismOption);
    if (value < 1)
        result.AddError("--max-parallelism must be at least 1.");
});

var workersOption = new Option<int>("--workers")
{
    Description = "Number of test workers (in-process parallelism) per dotnet test process",
    DefaultValueFactory = _ => 4
};
workersOption.Validators.Add(result =>
{
    var value = result.GetValue(workersOption);
    if (value < 1)
        result.AddError("--workers must be at least 1.");
});

var maxTestsOption = new Option<int>("--max-tests")
{
    Description = "Maximum number of tests to run (0 = all)",
    DefaultValueFactory = _ => 0
};
maxTestsOption.Validators.Add(result =>
{
    var value = result.GetValue(maxTestsOption);
    if (value < 0)
        result.AddError("--max-tests must be 0 or greater.");
});

var skipTestsOption = new Option<int>("--skip-tests")
{
    Description = "Number of tests to skip from the start of the discovered list",
    DefaultValueFactory = _ => 0
};
skipTestsOption.Validators.Add(result =>
{
    var value = result.GetValue(skipTestsOption);
    if (value < 0)
        result.AddError("--skip-tests must be 0 or greater.");
});

var resultsDirOption = new Option<string?>("--results-dir")
{
    Description = "Directory for .trx result files (default: auto-generated temp directory)"
};

var idleTimeoutOption = new Option<int>("--idle-timeout")
{
    Description = "Kill a batch if no output is received for this many seconds (0 = no timeout)",
    DefaultValueFactory = _ => 60
};
idleTimeoutOption.Validators.Add(result =>
{
    var value = result.GetValue(idleTimeoutOption);
    if (value < 0)
        result.AddError("--idle-timeout must be 0 or greater.");
});

var retriesOption = new Option<int>("--retries")
{
    Description = "Number of times to retry failed batches (0 = no retries)",
    DefaultValueFactory = _ => 2
};
retriesOption.Validators.Add(result =>
{
    var value = result.GetValue(retriesOption);
    if (value < 0)
        result.AddError("--retries must be 0 or greater.");
});

var autoTuneOption = new Option<bool>("--auto-tune")
{
    Description = "Auto-tune batch size and parallelism based on test count and CPU cores"
};

var autoRetryOption = new Option<bool>("--auto-retry")
{
    Description = "Keep retrying failed batches as long as at least one recovers per round (overrides --retries)"
};

var filterExpressionOption = new Option<string?>("--filter-expression")
{
    Description = "VSTest filter expression applied during discovery (e.g. \"TestCategory=Smoke\", \"TestCategory!=LongRunning\")"
};

var testListOption = new Option<string?>("--test-list")
{
    Description = "Pipe-delimited fully-qualified test names to run directly, skipping discovery. Takes priority over --filter-expression. (e.g. \"Ns.Class.Test1|Ns.Class.Test2\")",
    Arity = ArgumentArity.ZeroOrOne
};

var testListFileOption = new Option<string?>("--test-list-file")
{
    Description = "Path to a file containing fully-qualified test names (one per line, or pipe-delimited). Mutually exclusive with --test-list.",
    Arity = ArgumentArity.ZeroOrOne
};

var rootCommand = new RootCommand("Parallel Test Runner — discover, batch, and run dotnet tests in parallel")
{
    projectArg,
    batchSizeOption,
    maxParallelismOption,
    workersOption,
    maxTestsOption,
    skipTestsOption,
    resultsDirOption,
    idleTimeoutOption,
    retriesOption,
    autoTuneOption,
    autoRetryOption,
    filterExpressionOption,
    testListOption,
    testListFileOption,
};

// Treat unmatched tokens as extra dotnet test args (passed after --)
rootCommand.TreatUnmatchedTokensAsErrors = false;

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.Error.WriteLine("Cancellation requested — stopping test processes...");
};

var toolExitCode = 0;

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var project = parseResult.GetValue(projectArg);
    var batchSize = parseResult.GetValue(batchSizeOption);
    var maxParallelism = parseResult.GetValue(maxParallelismOption);
    var workers = parseResult.GetValue(workersOption);
    var maxTests = parseResult.GetValue(maxTestsOption);
    var skipTests = parseResult.GetValue(skipTestsOption);
    var resultsDir = parseResult.GetValue(resultsDirOption);
    var idleTimeout = parseResult.GetValue(idleTimeoutOption);
    var retries = parseResult.GetValue(retriesOption);
    var autoTune = parseResult.GetValue(autoTuneOption);
    var autoRetry = parseResult.GetValue(autoRetryOption);
    var filterExpression = parseResult.GetValue(filterExpressionOption);
    var testList = parseResult.GetValue(testListOption);
    var testListFile = parseResult.GetValue(testListFileOption);
    var extraArgs = parseResult.UnmatchedTokens.ToArray();

    // Always create a results directory for TRX output — use temp if not specified
    resultsDir ??= Path.Combine(Path.GetTempPath(), "ParallelTestRunner");
    resultsDir = Path.Combine(resultsDir, $"run_{DateTime.UtcNow:yyyyMMddTHHmmss}");
    Directory.CreateDirectory(resultsDir);

    var options = new Options(
        ProjectPath: project!,
        BatchSize: batchSize,
        MaxParallelism: maxParallelism,
        Workers: workers,
        MaxTests: maxTests,
        Retries: retries,
        AutoRetry: autoRetry,
        ExtraDotnetTestArgs: extraArgs,
        ResultsDirectory: resultsDir,
        FilterExpression: filterExpression,
        TestList: testList,
        TestListFile: testListFile,
        IdleTimeout: idleTimeout > 0 ? TimeSpan.FromSeconds(idleTimeout) : TimeSpan.Zero);

    // Mutual exclusion: --test-list and --test-list-file cannot both be provided.
    if (!string.IsNullOrWhiteSpace(options.TestList) && !string.IsNullOrWhiteSpace(options.TestListFile))
    {
        Console.Error.WriteLine("ERROR: --test-list and --test-list-file are mutually exclusive. Use one or the other.");
        toolExitCode = 2;
        return;
    }

    PrintBanner(options);

    // Validate project path exists
    if (!File.Exists(options.ProjectPath))
    {
        Console.Error.WriteLine($"ERROR: Project file not found: {options.ProjectPath}");
        toolExitCode = 2;
        return;
    }

    // Step 1: Discover tests (or load from --test-list / --test-list-file)
    IReadOnlyList<string> tests;
    IReadOnlyList<string> parsedTestList;
    string? testListSource = null;
    if (!string.IsNullOrWhiteSpace(options.TestListFile))
    {
        try
        {
            parsedTestList = TestDiscovery.ParseTestListFile(options.TestListFile);
            testListSource = options.TestListFile;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            toolExitCode = 2;
            return;
        }
    }
    else
    {
        parsedTestList = TestDiscovery.ParseTestList(options.TestList);
    }
    if (parsedTestList.Count > 0)
    {
        var validationFailures = TestDiscovery.ValidateTestList(parsedTestList);
        if (validationFailures.Count > 0)
        {
            Console.Error.WriteLine("ERROR: --test-list contains values that don't look like fully-qualified test names:");
            foreach (var failure in validationFailures)
                Console.Error.WriteLine($"  [{failure.Index}] {failure.Segment} — {failure.Reason}");
            Console.Error.WriteLine("Expected format: Namespace.Class.Method (one or more dots, no spaces, parens, quotes, or regex syntax).");
            toolExitCode = 2;
            return;
        }

        tests = parsedTestList;
        Console.Error.WriteLine("  Skipping test discovery — using provided test list");
        if (testListSource is not null)
            Console.Error.WriteLine($"  Source: {testListSource}");
        if (options.FilterExpression is not null)
            Console.Error.WriteLine("  --filter-expression ignored (not applicable when --test-list/--test-list-file is provided)");
        Console.Error.WriteLine("  Tests to rerun:");
        foreach (var test in tests)
            Console.Error.WriteLine($"    - {test}");
        Console.Error.WriteLine($"  Total: {tests.Count} tests");
    }
    else
    {
        try
        {
            tests = await TestDiscovery.DiscoverAsync(options.ProjectPath, options.ExtraDotnetTestArgs, options.FilterExpression, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Discovery failed: {ex.Message}");
            toolExitCode = 2;
            return;
        }

        Console.Error.WriteLine($"  Discovered {tests.Count} tests");
    }

    // Skip tests if --skip-tests was specified
    if (skipTests > 0)
    {
        tests = tests.Skip(skipTests).ToList();
        Console.Error.WriteLine($"  Skipped first {skipTests} tests, {tests.Count} remaining");
    }

    // Truncate if --max-tests was specified
    if (options.MaxTests > 0 && tests.Count > options.MaxTests)
    {
        tests = tests.Take(options.MaxTests).ToList();
        Console.Error.WriteLine($"  Limited to {tests.Count} tests (--max-tests {options.MaxTests})");
    }

    if (tests.Count == 0)
    {
        Console.Error.WriteLine("  No tests to run after applying skip/max-tests filters.");
        toolExitCode = 2;
        return;
    }

    // Auto-tune batch size and parallelism if --auto-tune is specified
    if (autoTune && tests.Count > 0)
    {
        var avgNameLength = tests.Average(t => (double)t.Length);
        var (autoBatchSize, autoParallelism) = AutoTuner.Calculate(
            tests.Count, Environment.ProcessorCount, avgNameLength);

        var batchSizeExplicit = parseResult.GetResult(batchSizeOption)?.Implicit == false;
        var parallelismExplicit = parseResult.GetResult(maxParallelismOption)?.Implicit == false;

        if (batchSizeExplicit || parallelismExplicit)
        {
            Console.Error.WriteLine($"  Auto-tune recommendation: batch-size={autoBatchSize}, parallelism={autoParallelism}");
            Console.Error.WriteLine($"    Reason: {tests.Count} tests across {autoParallelism} CPU slots (cores/2), 2x batches for load balancing");
            var overrides = new List<string>();
            if (batchSizeExplicit) overrides.Add($"--batch-size {options.BatchSize}");
            if (parallelismExplicit) overrides.Add($"--max-parallelism {options.MaxParallelism}");
            Console.Error.WriteLine($"    You specified: {string.Join(", ", overrides)} (keeping your values)");
        }
        else
        {
            options = options with { BatchSize = autoBatchSize, MaxParallelism = autoParallelism };
            Console.Error.WriteLine($"  Auto-tuned: batch-size={autoBatchSize}, parallelism={autoParallelism}");
        }
    }

    // Step 2: Batch tests
    var batches = TestBatcher.CreateBatches(tests, options.BatchSize);
    Console.Error.WriteLine($"  Created {batches.Count} batches (batch size: {options.BatchSize})");
    Console.Error.WriteLine();

    // Step 3: Run batches in parallel
    var results = await TestRunner.RunAllAsync(batches, options, cancellationToken);

    // Validate the custom ##ptr logger is loaded — without it, retry and hang detection cannot work
    if (!TestRunner.ValidateLoggerOutput(results))
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("ERROR: The ParallelTestRunner.TestLogger was not loaded by the test host.");
        Console.Error.WriteLine("       Test results cannot be accurately matched to FQNs for retry or hang detection.");
        Console.Error.WriteLine("       Ensure the logger DLL is present alongside the tool and --test-adapter-path is correct.");
        toolExitCode = 2;
        return;
    }

    // When --test-list was provided, verify that tests actually executed.
    // dotnet test exits 0 when a filter matches nothing, which would silently report PASSED.
    if (parsedTestList.Count > 0)
    {
        var ptrRegex = Patterns.PtrLoggerLineRegex();
        var executedCount = results
            .Where(r => r.CapturedOutput is not null)
            .SelectMany(r => r.CapturedOutput!)
            .Count(line => ptrRegex.IsMatch(line));

        if (executedCount == 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("ERROR: --test-list provided but zero tests were executed.");
            Console.Error.WriteLine("       The FQN names may not match tests in the assembly.");
            Console.Error.WriteLine("       Verify the fully-qualified test names are correct.");
            toolExitCode = 2;
            return;
        }
    }

    // Step 3.5: Smart retry with integrated hang detection
    RetryResult? retryResult = null;
    if ((options.AutoRetry || options.Retries > 0) && results.Any(r => r.ExitCode != 0))
    {
        retryResult = await RetryOrchestrator.RunAsync(results, batches, options, cancellationToken);
    }

    // Step 4: Collate results
    toolExitCode = ResultCollator.Collate(results, retryResult);

    // Always show the TRX results directory
    Console.Error.WriteLine($"  TRX results: {resultsDir}");
});

var config = new CommandLineConfiguration(rootCommand);
var parseExitCode = await config.InvokeAsync(args, cts.Token);
return parseExitCode != 0 ? parseExitCode : toolExitCode;

static void PrintBanner(Options options)
{
    var version = typeof(Options).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? typeof(Options).Assembly.GetName().Version?.ToString()
                  ?? "unknown";

    // Strip git-hash suffix sometimes appended by SourceLink (e.g. "1.1.2+abcdef0")
    var plusIndex = version.IndexOf('+');
    if (plusIndex > 0)
        version = version[..plusIndex];

    Console.Error.Write(Banner.BuildBanner(options, version));
    Console.Error.WriteLine();
}
