# CLAUDE.md

## Project Overview

**Parallel Test Runner** — a .NET 10 dotnet tool that discovers MSTest tests via `dotnet vstest --ListFullyQualifiedTests`, splits them into batches respecting a 7000-character filter string limit, and runs multiple `dotnet test` processes in parallel. Designed for TeamCity CI with real-time `##teamcity` service message forwarding.

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
  TestDiscovery.cs               # Two-step discovery: resolves DLL path, then extracts FQN test names
  TestBatcher.cs                 # Splits tests into batches, respects filter length limit
  AutoTuner.cs                   # Calculates optimal batch size and parallelism for --auto-tune flag
  TestRunner.cs                  # Runs batches in parallel with SemaphoreSlim throttling, live progress
  RetryOrchestrator.cs           # Smart retry with integrated hang detection for timed-out batches
  ResultCollator.cs              # Summarises results, determines exit code
  Patterns.cs                    # Shared regex patterns for parsing ##ptr logger and VSTest output

src/ParallelTestRunner.TestLogger/  # Custom VSTest logger (ships with the tool)
  PtrLogger.cs                   # Emits ##ptr[Outcome|FQN=...|Name=...] lines for real-time FQN tracking

tests/ParallelTestRunner.Tests/  # MSTest unit + integration tests
  TestBatcherTests.cs            # 9 unit tests for batching logic
  TestDiscoveryParseTests.cs     # 6 unit tests for discovery output parsing
  ResultCollatorTests.cs         # 7 unit tests for result collation and exit codes
  AutoTunerTests.cs              # 10 unit tests for auto-tuning logic
  RetryOrchestratorTests.cs      # 15 unit tests for smart retry orchestration, hang detection, and FQN matching
  IntegrationTests.cs            # 20 integration tests (discovery, batching, exit codes, hang detection, sequential execution, retries, auto-retry, TeamCity, custom logger)

tests/DummyTestProject/          # Fixture project with 70 unique FQN test methods across 7 namespaces
  DummyTests.cs                  # Includes parameterised, slow, conditionally-failing, transient-failing, concurrency-detecting, long-named, and display-name tests
```

## Architecture

The execution pipeline flows: **CLI parsing → Test discovery → Batching → Parallel execution → Smart retry orchestration → Result collation**.

- **Discovery**: Two-step process: first runs `dotnet test --list-tests --no-build` to resolve the test assembly DLL path from the `"Test run for <path>.dll"` output line, then runs `dotnet vstest <dll> --ListFullyQualifiedTests` to extract fully-qualified test names into a temp file. FQNs naturally deduplicate parameterised test variants (the FQN is the base method name, and `FullyQualifiedName=` matches all variants).
- **Batching**: Chunks tests by batch size, then auto-splits any chunk whose `FullyQualifiedName=...|FullyQualifiedName=...` filter string exceeds `MaxFilterLength` (7000 chars).
- **Execution**: `SemaphoreSlim(maxParallelism)` throttles concurrent `dotnet test` processes. Tests within each batch are forced to run sequentially (`-- MSTest.Parallelize.Workers=1`) — parallelism is achieved by running multiple isolated processes, not by in-process test parallelisation. Each process runs with `-v normal` verbosity to ensure per-test output keeps the idle timeout alive. Each process uses event-based async output (OutputDataReceived/ErrorDataReceived) with lock-protected console writes to prevent interleaving. Process lifecycle is wrapped in `TaskCompletionSource` for async-friendly waiting. A `ProgressTracker` counts individual test passes/failures from `##ptr` logger output lines and prints a summary after each batch completes.
- **Custom Logger**: `ParallelTestRunner.TestLogger` is a custom VSTest `ITestLoggerWithParameters` that subscribes to `TestResult` events and emits `##ptr[Outcome|FQN=Ns.Class.Method|Name=Display Name]` lines to stdout in real-time. This solves the critical problem where VSTest's human-readable output uses display names (e.g. "Accept a cancellation quote") that differ from the FQNs used for filtering (e.g. `Ns.Features.CancellationFeature.AcceptACancellationQuote`). The logger DLL ships alongside the tool via a project reference and is discovered at runtime using `AppContext.BaseDirectory` as the test adapter path.
- **Retry Orchestration**: `RetryOrchestrator` handles both retries and hang detection in a single phase. When a batch times out, its `##ptr` output is parsed to identify completed tests (by exact FQN match) and the suspected hanging test (first test with no result). Suspected hangers are set aside; remaining unrun/failed tests retry immediately at full parallelism. After failure retries complete, suspected hangers are tested solo — if they time out again, they're confirmed as hanging and permanently excluded. Tests that pass solo are marked as resolved and never retried again. Only failed tests are retried — passing tests within a failed batch are identified by FQN and excluded from retry. With `--auto-retry`, retries continue as long as at least one batch recovers per round.
- **Cancellation**: `CancellationTokenSource` propagates Ctrl+C through all layers, killing spawned process trees gracefully.
- **TeamCity**: Auto-detected via `TEAMCITY_VERSION` env var — appends `--logger teamcity` when present. Works alongside the built-in `##ptr` logger.

## Key Constants

| Constant | Value | Location |
|---|---|---|
| `MaxFilterLength` | 7000 | TestBatcher.cs |
| Default batch size | 50 | Options.cs |
| Default max parallelism | `ProcessorCount / 2` (min 1) | Options.cs |
| Discovery DLL path pattern | `"Test run for <path>.dll"` | TestDiscovery.cs |
| Filter format | `FullyQualifiedName=Ns.Cls.Test1\|FullyQualifiedName=Ns.Cls.Test2` | TestBatcher.cs |
| `##ptr` line format | `##ptr[Outcome\|FQN=Ns.Cls.Method\|Name=Display Name]` | PtrLogger.cs |
| Logger adapter path | `AppContext.BaseDirectory` | TestRunner.cs |
| Default idle timeout | 60s | Program.cs |
| Default retries | 2 | Options.cs |

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
| `FAIL_DISPLAY_NAME_TESTS` | DummyTestProject: triggers failure on tests with custom display names differing from FQNs |

## Conventions

- .NET 10, C#, nullable enabled, implicit usings
- System.CommandLine (v2.0.0-beta5) for CLI parsing
- `InternalsVisibleTo` exposes internals to ParallelTestRunner.Tests
- MSTest 3.x for all tests, including `[DataRow]` parameterised tests and `[TestMethod("display name")]` for custom display names
- All `dotnet test` invocations pass `--no-build`, `-v normal`, `--test-adapter-path`, and `--logger ParallelTestRunner` — a separate build step is required
- The custom `ParallelTestRunner.TestLogger` is a project reference, so its DLL ships alongside the tool when packed
- DummyTestProject has `[assembly: Parallelize(Workers = 4)]` to verify the tool's `Workers=1` override works
- DummyTestProject includes tests with `[TestMethod("...")]` display names to verify FQN-vs-display-name divergence handling
- Integration tests auto-locate solution root by searching upward for `ParallelTestRunner.sln`
