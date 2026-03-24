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

# Auto-tune batch size and parallelism based on test count and CPU cores
parallel-test-runner MyTests.csproj --auto-tune

# Auto-tune with explicit override (shows recommendation but keeps your value)
parallel-test-runner MyTests.csproj --auto-tune --max-parallelism 4

# Run a subset of tests
parallel-test-runner MyTests.csproj --skip-tests 100 --max-tests 50

# Set idle timeout and retry policy
parallel-test-runner MyTests.csproj --idle-timeout 30 --retries 3

# Keep retrying as long as at least one batch recovers per round
parallel-test-runner MyTests.csproj --auto-retry

# Write .trx result files
parallel-test-runner MyTests.csproj --results-dir ./TestResults

# Pass extra args through to dotnet test
parallel-test-runner MyTests.csproj -- --configuration Release --no-restore
```

## How It Works

The execution pipeline flows: **CLI parsing → Test discovery → Batching → Parallel execution → Smart retry orchestration → Result collation**.

- **Discovery**: Two-step process — first runs `dotnet test --list-tests --no-build` to resolve the test assembly DLL path, then runs `dotnet vstest --ListFullyQualifiedTests` to extract fully-qualified test names. Using FQNs ensures exact matching during filtering and naturally deduplicates parameterised test variants.
- **Batching**: Splits tests into chunks by batch size. Any chunk whose `FullyQualifiedName=...|FullyQualifiedName=...` filter string exceeds 7000 characters is automatically sub-split.
- **Parallel execution**: A `SemaphoreSlim` throttles concurrent `dotnet test` processes. Tests within each batch are forced to run sequentially (`MSTest.Parallelize.Workers=1`) — parallelism comes from running multiple isolated processes, not in-process test parallelisation. Live progress is reported after each batch completes, showing running totals of passed/failed/executed tests.
- **Custom test logger**: A built-in VSTest logger (`ParallelTestRunner.TestLogger`) emits structured `##ptr` lines to stdout in real-time as each test completes. Each line contains both the fully-qualified name (FQN) and display name, enabling accurate matching even when test frameworks use human-readable display names that differ from the FQN used for filtering. The logger is automatically registered via `--test-adapter-path` and `--logger ParallelTestRunner` on every `dotnet test` invocation.
- **Smart retry orchestration**: When a batch times out (no output for `--idle-timeout` seconds), its `##ptr` output is parsed to identify which tests completed (by FQN) and which test was likely hanging. The suspected hanger is set aside; remaining unrun and failed tests are retried immediately at full parallelism. After retries, suspected hangers are tested individually — if they time out again solo, they're confirmed as hanging and permanently excluded. Tests that pass solo are marked as resolved and never retried again. Only failed tests within a batch are retried — passing tests are never re-run, thanks to exact FQN matching from the custom logger. With `--auto-retry`, retries continue as long as at least one batch recovers per round — useful for flaky tests caused by external factors.
- **Cancellation**: Ctrl+C propagates through all layers, stopping spawned process trees gracefully.

## TeamCity Build Step Configuration

The tool auto-detects TeamCity via the `TEAMCITY_VERSION` environment variable. When detected, it automatically appends `--logger teamcity` so `##teamcity` service messages flow to the build log — no manual flag needed. This works alongside the tool's built-in `##ptr` logger.

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
parallel-test-runner MyTests.csproj --auto-tune --max-parallelism 4
```

### Complete sample build step script

```bash
dotnet build MySolution.sln --configuration Release
dotnet tool install --global ParallelTestRunner --add-source ./nupkg
parallel-test-runner MyTests.csproj --auto-tune --auto-retry
```

## Configuration Reference

| Option | Type | Default | Description |
|---|---|---|---|
| `<project>` | string | *(required)* | Path to the test project or solution |
| `--batch-size` | int | `50` | Number of tests per batch (minimum 1) |
| `--max-parallelism` | int | `CPU cores / 2` | Maximum concurrent `dotnet test` processes (minimum 1) |
| `--max-tests` | int | `0` | Maximum number of tests to run (0 = all) |
| `--skip-tests` | int | `0` | Number of tests to skip from the start of the discovered list |
| `--auto-tune` | bool | `false` | Auto-tune batch size and parallelism based on test count and CPU cores |
| `--idle-timeout` | int | `60` | Kill a batch if no output is received for this many seconds (0 = no timeout) |
| `--retries` | int | `2` | Number of times to retry failed batches (0 = no retries) |
| `--auto-retry` | bool | `false` | Keep retrying failed batches as long as at least one recovers per round (overrides `--retries`) |
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
| `TEAMCITY_VERSION` | When set, the tool automatically appends `--logger teamcity` to enable TeamCity service message output |
| `FAIL_TESTS` | Used by `DummyTestProject` to trigger deliberate test failures for testing failure scenarios |
| `HANG_TEST` | Used by `DummyTestProject` to trigger a 30-second blocking test for hang detection testing |
| `FAIL_ONCE` | Used by `DummyTestProject` to trigger a transient failure (fails first run, passes on retry) |
| `FAIL_DISPLAY_NAME_TESTS` | Used by `DummyTestProject` to trigger failure on tests with custom display names |
