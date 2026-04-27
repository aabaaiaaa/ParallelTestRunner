# CLAUDE.md

## Project Overview

**Parallel Test Runner** — a .NET dotnet tool that runs test suites faster by splitting them across multiple isolated `dotnet test` processes running in parallel, with configurable in-process parallelism (`--workers`, default 4) within each process. Discovers tests via `dotnet vstest --ListFullyQualifiedTests`, batches them respecting a 7000-character filter string limit, and supports MSTest, xUnit, and NUnit. Designed for CI pipelines with smart retry orchestration, hang detection, and TeamCity `##teamcity` service message forwarding.

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
  Banner.cs                      # Pure helper that builds the startup banner string (title art + gradient + version + config lines)
  TestDiscovery.cs               # Two-step discovery: resolves DLL path, then extracts FQN test names. Also ParseTestList, ParseTestListFile (file-based input), and ValidateTestList (FQN shape check).
  TestBatcher.cs                 # Splits tests into batches, respects filter length limit
  AutoTuner.cs                   # Calculates optimal batch size and parallelism for --auto-tune flag
  TestRunner.cs                  # Runs batches in parallel with SemaphoreSlim throttling, quiet console (##ptr lines only), TRX always enabled
  RetryOrchestrator.cs           # Unified rescue/retry/hang-detection loop — all work types run concurrently to maximise slot utilisation
  ResultCollator.cs              # Summarises results, determines exit code, reports slow/hanging/persistent failures
  Patterns.cs                    # Shared regex patterns for parsing ##ptr logger and VSTest output

src/ParallelTestRunner.TestLogger/  # Custom VSTest logger (ships with the tool)
  PtrLogger.cs                   # Emits ##ptr[Outcome|FQN=...|Name=...] lines for real-time FQN tracking

tests/ParallelTestRunner.Tests/  # MSTest unit + integration tests
  TestBatcherTests.cs            # 8 unit tests for batching logic
  TestDiscoveryParseTests.cs     # 13 + 12 validator + 8 file = 33 unit tests for discovery output parsing, --test-list parsing, FQN shape validation, and --test-list-file parsing
  ResultCollatorTests.cs         # 13 unit tests for result collation and exit codes
  AutoTunerTests.cs              # 11 unit tests for auto-tuning logic
  RetryOrchestratorTests.cs      # 15 unit tests for smart retry orchestration, hang detection, and FQN matching
  TestRunnerUnitTests.cs         # 19 unit tests for test runner argument building and logger validation
  BannerTests.cs                 # 7 unit tests for banner construction (version, gradient, ANSI Shadow fingerprints)
  IntegrationTests.cs            # 32 + 2 validation + 5 test-list-file = 39 integration tests (discovery, batching, exit codes, hang detection, sequential execution, retries, auto-retry, TeamCity, custom logger, --test-list, --test-list-file, FQN shape rejection)

tests/DummyTestProject/          # MSTest fixture project with 70 unique FQN test methods across 7 namespaces
  DummyTests.cs                  # Includes parameterised, slow, conditionally-failing, transient-failing, concurrency-detecting, long-named, and display-name tests

tests/DummyTestProject.XUnit/   # xUnit fixture project for verifying --workers override with xUnit.MaxParallelThreads
tests/DummyTestProject.NUnit/   # NUnit fixture project for verifying --workers override with NUnit.NumberOfTestWorkers
```

## Architecture

The execution pipeline flows: **CLI parsing → Test discovery → Batching → Parallel execution → Smart retry orchestration → Result collation**.

- **Discovery**: Two-step process: first runs `dotnet test --list-tests --no-build` to resolve the test assembly DLL path from the `"Test run for <path>.dll"` output line, then runs `dotnet vstest <dll> --ListFullyQualifiedTests` to extract fully-qualified test names into a temp file. FQNs naturally deduplicate parameterised test variants (the FQN is the base method name, and `FullyQualifiedName=` matches all variants). **Skippable via `--test-list` or `--test-list-file`**: both options bypass discovery entirely and feed the supplied FQN names directly into batching. `--test-list` accepts a pipe-delimited string inline; `--test-list-file` reads names from a file (one per line or pipe-delimited) and is the alternative for lists too long for the Windows command line (cmd.exe ~8K, CreateProcess ~32K). The two options are mutually exclusive — providing both exits with code 2. Either option takes priority over `--filter-expression`, which is ignored with a warning when supplied alongside (since it's a discovery-phase filter with nothing to apply to). All segments are validated against the `Namespace.Class.Method` shape (regex `^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$`); inputs like vstest filter expressions are rejected with exit 2 before any test execution. After execution, the tool also validates that at least one test was actually executed; if zero `##ptr` results are reported, it exits with code 2 and an error indicating the FQN names may not match tests in the assembly.
- **Batching**: Chunks tests by batch size, then auto-splits any chunk whose `FullyQualifiedName=...|FullyQualifiedName=...` filter string exceeds `MaxFilterLength` (7000 chars).
- **Execution**: `SemaphoreSlim(maxParallelism)` throttles concurrent `dotnet test` processes. In-process parallelism is controlled by `--workers` (default 4), which sets `MSTest.Parallelize.Workers`, `xUnit.MaxParallelThreads`, and `NUnit.NumberOfTestWorkers` for each process. Use `--workers 1` to force sequential execution within batches for suites with shared-state contention. Each process runs with `-v normal` verbosity to ensure per-test output keeps the idle timeout alive. Console output is quiet — only `##ptr` result lines and `##teamcity` service messages are printed; all other process output is captured internally for retry/hang detection. TRX result files are always generated (auto-created temp directory or `--results-dir` override). Process lifecycle is wrapped in `TaskCompletionSource` for async-friendly waiting. A `ProgressTracker` counts individual test passes/failures from `##ptr` logger output lines and prints a summary after each batch completes.
- **Custom Logger**: `ParallelTestRunner.TestLogger` is a custom VSTest `ITestLoggerWithParameters` that subscribes to `TestResult` events and emits `##ptr[Outcome|FQN=Ns.Class.Method|Name=Display Name]` lines to stdout in real-time. This solves the critical problem where VSTest's human-readable output uses display names (e.g. "Accept a cancellation quote") that differ from the FQNs used for filtering (e.g. `Ns.Features.CancellationFeature.AcceptACancellationQuote`). The logger DLL ships alongside the tool via a project reference and is discovered at runtime using `AppContext.BaseDirectory` as the test adapter path.
- **Retry Orchestration**: `RetryOrchestrator` runs a unified work loop that combines rescue runs, solo hanger testing, and failure retries into each round to maximise parallel slot utilisation. After the initial run, tests are classified into: passed, failed, suspected hangers, and never-ran (tests behind a hanger in a timed-out batch). The loop builds a combined work pool each round — rescue batches (not counted as retries), solo hanger batches (with 3x extended timeout to distinguish slow tests from true hangers), and retry batches — all executed in a single `RunAllAsync` call. Solo hanger tests run concurrently with other work via `Task.WhenAll` using separate options with the extended timeout. Tests that pass with the extended timeout are reported as "slow tests" with a suggestion to increase `--idle-timeout`. Per-test retry counts ensure each test gets up to `--retries` actual retry attempts (rescue runs don't count). With `--auto-retry`, retries continue as long as at least one test recovers per round.
- **Cancellation**: `CancellationTokenSource` propagates Ctrl+C through all layers, killing spawned process trees gracefully.
- **TeamCity**: Auto-detected via `TEAMCITY_VERSION` env var — appends `--logger teamcity` when present. Works alongside the built-in `##ptr` logger.

## Key Constants

| Constant | Value | Location |
|---|---|---|
| `MaxFilterLength` | 7000 | TestBatcher.cs |
| Default batch size | 50 | Options.cs |
| Default max parallelism | `ProcessorCount / 2` (min 1) | Options.cs |
| Default workers | 4 | Options.cs |
| Discovery DLL path pattern | `"Test run for <path>.dll"` | TestDiscovery.cs |
| Filter format | `FullyQualifiedName=Ns.Cls.Test1\|FullyQualifiedName=Ns.Cls.Test2` | TestBatcher.cs |
| `##ptr` line format | `##ptr[Outcome\|FQN=Ns.Cls.Method\|Name=Display Name]` | PtrLogger.cs |
| Logger adapter path | `AppContext.BaseDirectory` | TestRunner.cs |
| Default idle timeout | 60s | Program.cs |
| Default retries | 2 | Options.cs |
| Solo hanger timeout multiplier | 3x idle timeout | RetryOrchestrator.cs |
| Default TRX results dir | `%TEMP%/ParallelTestRunner/run_<timestamp>` | Program.cs |
| Banner gradient | `░▒▓` | Banner.cs |
| Banner title font | ANSI Shadow | Banner.cs |
| Tool version display | from `AssemblyInformationalVersionAttribute` | Program.cs |

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

- .NET 9, C#, nullable enabled, implicit usings
- System.CommandLine (v2.0.0-beta5.25306.1) for CLI parsing
- `InternalsVisibleTo` exposes internals to ParallelTestRunner.Tests
- MSTest 3.x for all tests, including `[DataRow]` parameterised tests and `[TestMethod("display name")]` for custom display names
- All `dotnet test` invocations pass `--no-build`, `-v normal`, `--test-adapter-path`, `--logger ParallelTestRunner`, and `--logger trx` — a separate build step is required
- The custom `ParallelTestRunner.TestLogger` is a project reference, so its DLL ships alongside the tool when packed
- DummyTestProject has `[assembly: Parallelize(Workers = 4)]` to verify the tool's `--workers` override works
- DummyTestProject includes tests with `[TestMethod("...")]` display names to verify FQN-vs-display-name divergence handling
- Integration tests auto-locate solution root by searching upward for `ParallelTestRunner.sln`
