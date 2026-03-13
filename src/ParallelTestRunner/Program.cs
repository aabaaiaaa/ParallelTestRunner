using System.CommandLine;
using ParallelTestRunner;

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

var resultsDirOption = new Option<string?>("--results-dir")
{
    Description = "Directory for .trx result files"
};

var autoOption = new Option<bool>("--auto")
{
    Description = "Auto-tune batch size and parallelism based on test count and CPU cores"
};

var rootCommand = new RootCommand("Parallel Test Runner — discover, batch, and run dotnet tests in parallel")
{
    projectArg,
    batchSizeOption,
    maxParallelismOption,
    maxTestsOption,
    resultsDirOption,
    autoOption,
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
    var maxTests = parseResult.GetValue(maxTestsOption);
    var resultsDir = parseResult.GetValue(resultsDirOption);
    var auto = parseResult.GetValue(autoOption);
    var extraArgs = parseResult.UnmatchedTokens.ToArray();

    var options = new Options(
        ProjectPath: project!,
        BatchSize: batchSize,
        MaxParallelism: maxParallelism,
        MaxTests: maxTests,
        ExtraDotnetTestArgs: extraArgs,
        ResultsDirectory: resultsDir);

    PrintBanner(options);

    // Step 1: Discover tests
    IReadOnlyList<string> tests;
    try
    {
        tests = await TestDiscovery.DiscoverAsync(options.ProjectPath, options.ExtraDotnetTestArgs, cancellationToken);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Discovery failed: {ex.Message}");
        toolExitCode = 2;
        return;
    }

    Console.Error.WriteLine($"  Discovered {tests.Count} tests");

    // Truncate if --max-tests was specified
    if (options.MaxTests > 0 && tests.Count > options.MaxTests)
    {
        tests = tests.Take(options.MaxTests).ToList();
        Console.Error.WriteLine($"  Limited to {tests.Count} tests (--max-tests {options.MaxTests})");
    }

    // Auto-tune batch size and parallelism if --auto is specified
    if (auto && tests.Count > 0)
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

    // Step 4: Collate results
    toolExitCode = ResultCollator.Collate(results);
});

var config = new CommandLineConfiguration(rootCommand);
var parseExitCode = await config.InvokeAsync(args, cts.Token);
return parseExitCode != 0 ? parseExitCode : toolExitCode;

static void PrintBanner(Options options)
{
    const string banner = """

        ____                 _ _      _   _____         _     ____
       |  _ \ __ _ _ __ __ _| | | ___| | |_   _|__  ___| |_  |  _ \ _   _ _ __  _ __   ___ _ __
       | |_) / _` | '__/ _` | | |/ _ \ |   | |/ _ \/ __| __| | |_) | | | | '_ \| '_ \ / _ \ '__|
       |  __/ (_| | | | (_| | | |  __/ |   | |  __/\__ \ |_  |  _ <| |_| | | | | | | |  __/ |
       |_|   \__,_|_|  \__,_|_|_|\___|_|   |_|\___||___/\__| |_| \_\\__,_|_| |_|_| |_|\___|_|

    """;

    Console.Error.WriteLine(banner);
    Console.Error.WriteLine($"  Detected cores: {Environment.ProcessorCount}");
    Console.Error.WriteLine($"  Chosen parallelism: {options.MaxParallelism}");
    Console.Error.WriteLine();
}
