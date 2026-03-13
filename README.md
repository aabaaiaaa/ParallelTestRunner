# Parallel Test Runner

A .NET 10 dotnet tool that discovers MSTest tests, splits them into configurable batches, and runs multiple `dotnet test` processes in parallel. Designed for TeamCity CI, where `##teamcity` service messages are forwarded in real-time.

## Installation

Pack the tool first:

```bash
dotnet pack src/ParallelTestRunner
```

### Global install

```bash
dotnet tool install --global ParallelTestRunner --add-source src/ParallelTestRunner/nupkg
```

### Local tool manifest

```bash
dotnet new tool-manifest
dotnet tool install ParallelTestRunner --add-source src/ParallelTestRunner/nupkg
```

### From a NuGet feed (if published)

```bash
dotnet tool install --global ParallelTestRunner
```

## Usage

### As an installed tool

```bash
# Global install
parallel-test-runner <path-to-test-project>

# Local tool manifest
dotnet tool run parallel-test-runner <path-to-test-project>
```

### Without installing (dev inner loop)

```bash
dotnet run --project src/ParallelTestRunner -- <path-to-test-project>
```

### Examples

```bash
# Run with custom batch size and parallelism
parallel-test-runner MyTests.csproj --batch-size 10 --max-parallelism 4

# Pass extra args through to dotnet test
parallel-test-runner MyTests.csproj -- --configuration Release --no-restore

# Write .trx result files
parallel-test-runner MyTests.csproj --results-dir ./TestResults
```

## TeamCity Build Step Configuration

The tool auto-detects TeamCity via the `TEAMCITY_VERSION` environment variable. When detected, it automatically appends `/TestAdapterPath:. /Logger:teamcity` so `##teamcity` service messages flow to the build log — no manual flag needed.

The tool always passes `--no-build` to `dotnet test`, so a separate build step is required.

### Recommended build steps

**Step 1 — Build**

```bash
dotnet build MySolution.sln
```

**Step 2 — Install tool**

```bash
dotnet tool install --global ParallelTestRunner --add-source <feed-or-path>
```

Or restore from a local tool manifest:

```bash
dotnet tool restore
```

**Step 3 — Run tests**

```bash
parallel-test-runner MyTests.csproj --batch-size 50 --max-parallelism 4
```

### Complete sample build step script

```bash
dotnet build MySolution.sln --configuration Release
dotnet tool install --global ParallelTestRunner --add-source ./nupkg
parallel-test-runner MyTests.csproj --batch-size 50 --max-parallelism 4
```

## Configuration Reference

| Option | Type | Default | Description |
|---|---|---|---|
| `<project>` | string | *(required)* | Path to the test project or solution |
| `--batch-size` | int | `50` | Number of tests per batch (minimum 1) |
| `--max-parallelism` | int | `CPU cores / 2` | Maximum concurrent `dotnet test` processes (minimum 1) |
| `--results-dir` | string | *(none)* | Directory for `.trx` result files |
| `-- <args>` | string[] | *(none)* | Extra arguments passed through to `dotnet test` |

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | All tests passed |
| `1` | One or more test failures |
| `2` | Infrastructure error (discovery failure, zero tests found) |

## Environment Variables

| Variable | Description |
|---|---|
| `TEAMCITY_VERSION` | When set, the tool automatically appends `/TestAdapterPath:. /Logger:teamcity` to enable TeamCity service message output |
| `FAIL_TESTS` | Used by `DummyTestProject` to trigger deliberate test failures for testing failure scenarios |
