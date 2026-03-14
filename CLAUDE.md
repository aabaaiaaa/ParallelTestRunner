# CLAUDE.md

## Project Overview

**Parallel Test Runner** — a .NET 10 dotnet tool that discovers MSTest tests via `dotnet test --list-tests`, splits them into batches respecting a 7000-character filter string limit, and runs multiple `dotnet test` processes in parallel. Designed for TeamCity CI with real-time `##teamcity` service message forwarding.

## Build & Test

```bash
# Build the solution
dotnet build ParallelTestRunner.sln

# Run tests
dotnet test tests/ParallelTestRunner.Tests

# Run the tool (dev inner loop)
dotnet run --project src/ParallelTestRunner -- <path-to-test-project>

# Pack as dotnet tool
dotnet pack src/ParallelTestRunner
```

## Project Structure

```
src/ParallelTestRunner/          # Main tool (console app, packaged as dotnet tool)
  Program.cs                     # CLI entry point using System.CommandLine, orchestrates pipeline
  Options.cs                     # Immutable record for configuration
  TestDiscovery.cs               # Runs `dotnet test --list-tests --no-build`, parses output
  TestBatcher.cs                 # Splits tests into batches, respects filter length limit
  AutoTuner.cs                   # Calculates optimal batch size and parallelism for --auto flag
  TestRunner.cs                  # Runs batches in parallel with SemaphoreSlim throttling
  HangDetector.cs                # Binary-split hang detection for timed-out batches
  ResultCollator.cs              # Summarises results, determines exit code

tests/ParallelTestRunner.Tests/  # MSTest unit + integration tests
  TestBatcherTests.cs            # 8 unit tests for batching logic
  TestDiscoveryParseTests.cs     # 8 unit tests for discovery output parsing
  ResultCollatorTests.cs         # 7 unit tests for result collation and exit codes
  AutoTunerTests.cs              # 10 unit tests for auto-tuning logic
  HangDetectorTests.cs           # 6 unit tests for binary-split hang detection
  IntegrationTests.cs            # 13 integration tests (discovery, batching, exit codes, hang detection, sequential execution, retries)

tests/DummyTestProject/          # Fixture project with 66 unique test methods across 6 namespaces
  DummyTests.cs                  # Includes parameterised, slow, conditionally-failing, transient-failing, concurrency-detecting, and long-named tests
```

## Architecture

The execution pipeline flows: **CLI parsing → Test discovery → Batching → Parallel execution → Hang detection (if timeouts) → Retries (if failures) → Result collation**.

- **Discovery**: Spawns `dotnet test --list-tests --no-build`, parses output after the sentinel line `"The following Tests are available:"`. Deduplicates parameterised tests using source-generated regex to strip `(...)` suffixes.
- **Batching**: Chunks tests by batch size, then auto-splits any chunk whose `FullyQualifiedName~...|FullyQualifiedName~...` filter string exceeds `MaxFilterLength` (7000 chars).
- **Execution**: `SemaphoreSlim(maxParallelism)` throttles concurrent `dotnet test` processes. Tests within each batch are forced to run sequentially (`-- MSTest.Parallelize.Workers=1`) — parallelism is achieved by running multiple isolated processes, not by in-process test parallelisation. Each process runs with `-v normal` verbosity to ensure per-test output keeps the idle timeout alive. Each process uses event-based async output (OutputDataReceived/ErrorDataReceived) with lock-protected console writes to prevent interleaving. Process lifecycle is wrapped in `TaskCompletionSource` for async-friendly waiting.
- **Hang Detection**: After execution, any timed-out batches are fed into `HangDetector` which recursively binary-splits test lists and re-runs them to isolate specific hanging tests. Max recursion depth of 10. Accepts a `Func<>` for the batch runner to enable unit testing with fakes.
- **Retries**: After hang detection, all failed batches (exit code != 0, including timed-out) are retried up to `Retries` times. Each retry round re-runs failures through `RunAllAsync` with the same parallelism. Results are replaced in-place. Early exit if all failures recover.
- **Cancellation**: `CancellationTokenSource` propagates Ctrl+C through all layers, killing spawned process trees gracefully.
- **TeamCity**: Auto-detected via `TEAMCITY_VERSION` env var — appends `/TestAdapterPath:. /Logger:teamcity` when present.

## Key Constants

| Constant | Value | Location |
|---|---|---|
| `MaxFilterLength` | 7000 | TestBatcher.cs |
| Default batch size | 50 | Options.cs |
| Default max parallelism | `ProcessorCount / 2` (min 1) | Options.cs |
| Discovery sentinel | `"The following Tests are available:"` | TestDiscovery.cs |
| Filter format | `Name~Test1\|Name~Test2` | TestBatcher.cs |
| Default idle timeout | 60s | Program.cs |
| Default retries | 2 | Options.cs |
| Hang detection max depth | 10 | HangDetector.cs |

## Exit Codes

- `0` — all tests passed
- `1` — one or more batch failures
- `2` — infrastructure error (discovery failure, zero tests found)

## Environment Variables

| Variable | Purpose |
|---|---|
| `TEAMCITY_VERSION` | Enables TeamCity logger adapter automatically |
| `FAIL_TESTS` | DummyTestProject: triggers deliberate failures for testing |
| `HANG_TEST` | DummyTestProject: triggers a 30-second blocking test for hang detection testing |
| `FAIL_ONCE` | DummyTestProject: triggers a transient failure (fails first run, passes on retry via temp file marker) |

## Conventions

- .NET 10, C#, nullable enabled, implicit usings
- System.CommandLine (v2.0.0-beta5) for CLI parsing
- `InternalsVisibleTo` exposes internals to ParallelTestRunner.Tests
- MSTest 3.x for all tests, including `[DataRow]` parameterised tests
- All `dotnet test` invocations pass `--no-build` and `-v normal` — a separate build step is required
- DummyTestProject has `[assembly: Parallelize(Workers = 4)]` to verify the tool's `Workers=1` override works
- Integration tests auto-locate solution root by searching upward for `ParallelTestRunner.sln`
