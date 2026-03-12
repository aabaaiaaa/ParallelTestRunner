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

var maxParallelismOption = new Option<int>("--max-parallelism")
{
    Description = "Maximum number of concurrent dotnet test processes",
    DefaultValueFactory = _ => Math.Max(1, Environment.ProcessorCount / 2)
};

var resultsDirOption = new Option<string?>("--results-dir")
{
    Description = "Directory for .trx result files"
};

var rootCommand = new RootCommand("Parallel Test Runner — discover, batch, and run dotnet tests in parallel")
{
    projectArg,
    batchSizeOption,
    maxParallelismOption,
    resultsDirOption,
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

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var project = parseResult.GetValue(projectArg);
    var batchSize = parseResult.GetValue(batchSizeOption);
    var maxParallelism = parseResult.GetValue(maxParallelismOption);
    var resultsDir = parseResult.GetValue(resultsDirOption);
    var extraArgs = parseResult.UnmatchedTokens.ToArray();

    var options = new Options(
        ProjectPath: project!,
        BatchSize: batchSize,
        MaxParallelism: maxParallelism,
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
        Environment.Exit(2);
        return;
    }

    Console.Error.WriteLine($"  Discovered {tests.Count} tests");

    // Step 2: Batch tests
    var batches = TestBatcher.CreateBatches(tests, options.BatchSize);
    Console.Error.WriteLine($"  Created {batches.Count} batches (batch size: {options.BatchSize})");
    Console.Error.WriteLine();

    // Step 3: Run batches in parallel
    var results = await TestRunner.RunAllAsync(batches, options, cancellationToken);

    // Step 4: Collate results and exit
    var exitCode = ResultCollator.Collate(results);
    Environment.Exit(exitCode);
});

var config = new CommandLineConfiguration(rootCommand);
return await config.InvokeAsync(args, cts.Token);

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
