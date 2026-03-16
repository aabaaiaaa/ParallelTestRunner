namespace ParallelTestRunner;

public record Options(
    string ProjectPath,
    int BatchSize = 50,
    int MaxParallelism = 0,
    int MaxTests = 0,
    int Retries = 2,
    bool AutoRetry = false,
    string[] ExtraDotnetTestArgs = default!,
    string? ResultsDirectory = null,
    TimeSpan IdleTimeout = default)
{
    public int MaxParallelism { get; init; } = MaxParallelism > 0
        ? MaxParallelism
        : Math.Max(1, Environment.ProcessorCount / 2);

    public string[] ExtraDotnetTestArgs { get; init; } = ExtraDotnetTestArgs ?? [];
}
