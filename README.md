# Parallel Test Runner

A .NET dotnet tool that runs test suites faster by splitting them across multiple isolated `dotnet test` processes running in parallel, with configurable in-process parallelism within each process. Works with **MSTest**, **xUnit**, and **NUnit**.

This solves a common problem with test execution: by distributing tests across multiple processes, each with their own configurable worker count (`--workers`, default 4), you get both process-level isolation and in-process parallelism. For suites with shared-state contention, use `--workers 1` to force sequential execution within each process while still achieving parallelism at the process level.

**Important:** The tool always passes `--no-build` to `dotnet test`. You must build your test project before running the tool.

Designed for CI pipelines with built-in smart retry orchestration, hang detection, and TeamCity `##teamcity` service message forwarding.

## Installation

### Global install

```bash
dotnet tool install --global ParallelTestRunner
```

### Local tool manifest

```bash
dotnet new tool-manifest
dotnet tool install ParallelTestRunner
```

## Usage

### As an installed tool

```bash
# Global install
parallel-test-runner <path-to-test-project>

# Local tool manifest
dotnet tool run parallel-test-runner <path-to-test-project>
```

### Quick start

For most projects, `--auto-tune` and `--auto-retry` are all you need. The tool will figure out batch sizes and parallelism based on your CPU and test count:

```bash
parallel-test-runner MyTests.csproj --auto-tune --auto-retry
```

If you need to override a specific setting, pass it alongside `--auto-tune` — the tool will use your value and show what it would have recommended:

```bash
parallel-test-runner MyTests.csproj --auto-tune --auto-retry --max-parallelism 4
```

### Integration / API tests

Integration tests typically make HTTP calls or hit databases, with individual tests completing in under 30 seconds. These are well suited to high parallelism — the bottleneck is usually the backend services, not CPU.

```bash
# Let auto-tune pick batch size and parallelism (good default for most projects)
parallel-test-runner MyIntegrationTests.csproj --auto-tune --auto-retry

# If tests are fast (<5s each), the default 60s idle timeout is fine.
# For slower API calls (10-30s), increase the timeout:
parallel-test-runner MyIntegrationTests.csproj --auto-tune --auto-retry --idle-timeout 120

# Filter to a specific test category
parallel-test-runner MyIntegrationTests.csproj --auto-tune --auto-retry --filter-expression "TestCategory=Smoke"

# Run a subset to validate the pipeline before committing to a full run
parallel-test-runner MyIntegrationTests.csproj --auto-tune --max-tests 50 --retries 0
```

**When to override auto-tune:**
- If your backend can only handle a limited number of concurrent connections, cap parallelism: `--max-parallelism 4`
- If tests share a database and you see transient conflicts, reduce batch size to spread load: `--batch-size 10`

### End-to-end / UI / browser tests

UI tests launch browsers and interact with web pages, so each test takes significantly longer (1-10 minutes) and consumes more resources. Each parallel process opens its own browser instance, so parallelism should be lower than for API tests.

```bash
# Start with auto-tune but cap parallelism to avoid overwhelming the machine.
# 3-5 parallel browsers is a good starting point for most CI agents:
parallel-test-runner MyUITests.csproj --auto-tune --auto-retry --max-parallelism 3 --idle-timeout 600

# For very slow UI tests (multi-step workflows), increase the idle timeout further.
# The timeout resets on every line of test output, so a test actively producing
# SpecFlow step output won't be killed — only truly hanging tests:
parallel-test-runner MyUITests.csproj --auto-tune --auto-retry --max-parallelism 3 --idle-timeout 900

# Filter to a specific category of UI tests
parallel-test-runner MyUITests.csproj --auto-tune --auto-retry --max-parallelism 3 --idle-timeout 600 --filter-expression "TestCategory=WebCoreTest"

# Run a small batch first to validate the browser setup works with parallelism
parallel-test-runner MyUITests.csproj --max-tests 5 --max-parallelism 2 --batch-size 1 --retries 0 --idle-timeout 600
```

**When to override auto-tune:**
- **Always set `--max-parallelism`** — auto-tune doesn't know tests launch browsers, so it will over-allocate. Start with 3-5 and adjust based on your CI agent's RAM and CPU.
- **Always increase `--idle-timeout`** — the default 60s is too short for UI tests. Use 600s (10 minutes) as a baseline; increase if tests involve long page loads or complex workflows.
- If tests share a browser profile or singleton resource, ensure each process gets its own isolated instance (e.g. unique Playwright user data directories). The tool runs separate `dotnet test` processes, but the test project must support multiple instances running simultaneously.

### CI rerun with known failing tests

When you already know the exact test FQNs to rerun (e.g. from a TeamCity API or previous run), use `--test-list` to skip discovery entirely and run them directly:

```bash
# Rerun specific tests by FQN (pipe-delimited)
parallel-test-runner MyTests.csproj --test-list "Ns.Features.LoginFeature.ValidLogin|Ns.Features.LoginFeature.InvalidPassword" --retries 3

# In a TeamCity/CI rerun build step (PowerShell)
parallel-test-runner MyTests.csproj --test-list "$qualifiedTests" --auto-retry --idle-timeout 300
```

`--test-list` takes priority — `--filter-expression` is ignored with a warning if both are provided. If an empty string is passed, the tool falls back to normal discovery. If the provided FQNs don't match any tests in the assembly, the tool exits with code 2.

### `--test-list-file <path>`

For lists too long to fit on the command line — Windows `cmd.exe` is limited to ~8000 characters and `CreateProcess` to ~32,767 — pass test names via a file. Each line, or each segment between `|`, is treated as one fully-qualified test name. Mutually exclusive with `--test-list`.

#### PowerShell example: build a list and write it to a file

```powershell
$tests = @(
    'MyApp.Tests.PolicyFeature.AcceptCancellationQuote',
    'MyApp.Tests.PolicyFeature.RenewalRiskExclusion'
)
$tests | Out-File -FilePath rerun.txt -Encoding utf8

parallel-test-runner .\MyTests.csproj --test-list-file rerun.txt
```

#### PowerShell example: rerun a list from a previous CI job

If a previous build produced `failed-tests.txt` (one FQN per line), rerun those failures:

```powershell
parallel-test-runner .\MyTests.csproj --test-list-file failed-tests.txt
```

#### Notes

- Each FQN must match the shape `Namespace.Class.Method` (with at least one dot). Filter expressions like `(FullNameMatchesRegex '...')` will be rejected with an error and exit code 2.
- File content is treated case-sensitively, the same as `--test-list`.
- UTF-8 with or without a BOM is supported.

### Other examples

```bash
# Run a subset of tests
parallel-test-runner MyTests.csproj --skip-tests 100 --max-tests 50

# Fixed retry count instead of auto-retry
parallel-test-runner MyTests.csproj --auto-tune --retries 3

# Force sequential execution within each process (for shared-state contention)
parallel-test-runner MyTests.csproj --auto-tune --auto-retry --workers 1

# Write .trx result files to a specific directory (TRX is always generated; this overrides the default temp location)
parallel-test-runner MyTests.csproj --auto-tune --auto-retry --results-dir ./TestResults

# Pass extra args through to dotnet test (note: --no-build is always passed by the tool)
parallel-test-runner MyTests.csproj --auto-tune -- --no-restore

# Exclude a test category
parallel-test-runner MyTests.csproj --auto-tune --auto-retry --filter-expression "TestCategory!=LongRunning"

# Combine category filter with other options
parallel-test-runner MyTests.csproj --auto-tune --auto-retry --filter-expression "TestCategory=Smoke&Priority=1"
```

## How It Works

The execution pipeline flows: **CLI parsing → Test discovery → Batching → Parallel execution → Smart retry orchestration → Result collation**.

- **Discovery**: Two-step process — first runs `dotnet test --list-tests --no-build` to resolve the test assembly DLL path, then runs `dotnet vstest --ListFullyQualifiedTests` to extract fully-qualified test names. Using FQNs ensures exact matching during filtering and naturally deduplicates parameterised test variants. Discovery can be skipped entirely with `--test-list` (see below).
- **Batching**: Splits tests into chunks by batch size. Any chunk whose `FullyQualifiedName=...|FullyQualifiedName=...` filter string exceeds 7000 characters is automatically sub-split.
- **Parallel execution**: A `SemaphoreSlim` throttles concurrent `dotnet test` processes. In-process parallelism is controlled by `--workers` (default 4), which sets the worker count for MSTest, xUnit, and NUnit via their respective runsettings properties. Use `--workers 1` to force sequential execution within each process for suites with shared-state contention. Console output is kept quiet — only `##ptr` test result lines and `##teamcity` service messages are printed. All other process output (build messages, step details, etc.) is captured internally for retry/hang detection but not displayed. TRX result files are always generated for failure diagnostics. Live progress is reported after each batch completes, showing running totals of passed/failed/executed tests.
- **Custom test logger**: A built-in VSTest logger (`ParallelTestRunner.TestLogger`) emits structured `##ptr` lines to stdout in real-time as each test completes. Each line contains both the fully-qualified name (FQN) and display name, enabling accurate matching even when test frameworks use human-readable display names that differ from the FQN used for filtering. The logger is automatically registered via `--test-adapter-path` and `--logger ParallelTestRunner` on every `dotnet test` invocation.
- **Smart retry orchestration**: The retry orchestrator runs a unified work loop that combines rescue runs, solo hanger testing, and failure retries into each round to maximise parallel slot utilisation. After the initial run, tests are classified as: passed, failed, suspected hangers, or never-ran (tests behind a hanger in a timed-out batch). Each round builds a combined work pool from all pending work types and executes them together, ensuring all parallel slots stay busy. Tests that never ran get rescue runs (not counted as retries) — every test is guaranteed at least one confirmed outcome. Suspected hangers are tested individually with an extended timeout (3x the normal idle timeout) to distinguish truly hanging tests from slow ones. Tests that pass with the extended timeout are reported as "slow tests" with a suggestion to increase `--idle-timeout`. Per-test retry counts ensure each failed test gets up to `--retries` actual retry attempts. With `--auto-retry`, retries continue as long as at least one test recovers per round — useful for flaky tests caused by external factors.
- **Cancellation**: Ctrl+C propagates through all layers, stopping spawned process trees gracefully.

## TeamCity Build Step Configuration

The tool auto-detects TeamCity via the `TEAMCITY_VERSION` environment variable. When detected, it automatically appends `--logger teamcity` so `##teamcity` service messages flow to the build log — no manual flag needed. This works alongside the tool's built-in `##ptr` logger.

**Prerequisite:** Your test project must have the `TeamCity.VSTest.TestAdapter` NuGet package installed for the TeamCity logger to work. Without it, `dotnet test` will fail with "Could not find a test logger with FriendlyName 'teamcity'".

### Recommended build steps

**Step 1 — Build**

```bash
dotnet build MySolution.sln
```

**Step 2 — Install tool**

```bash
dotnet tool install --global ParallelTestRunner
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
dotnet tool install --global ParallelTestRunner
parallel-test-runner MyTests.csproj --auto-tune --auto-retry
```

## Configuration Reference

| Option | Type | Default | Description |
|---|---|---|---|
| `<project>` | string | *(required)* | Path to the test project or solution |
| `--batch-size` | int | `50` | Number of tests per batch (minimum 1) |
| `--max-parallelism` | int | `CPU cores / 2` | Maximum concurrent `dotnet test` processes (minimum 1) |
| `--workers` | int | `4` | Number of test workers (in-process parallelism) per `dotnet test` process (minimum 1). Use `1` for suites with shared-state contention. |
| `--max-tests` | int | `0` | Maximum number of tests to run (0 = all) |
| `--skip-tests` | int | `0` | Number of tests to skip from the start of the discovered list |
| `--auto-tune` | bool | `false` | Auto-tune batch size and parallelism based on test count and CPU cores |
| `--idle-timeout` | int | `60` | Kill a batch if no output is received for this many seconds (0 = no timeout). Suspected hangers are retested solo with 3x this timeout. |
| `--retries` | int | `2` | Number of times to retry each failed test (0 = no retries). Rescue runs for tests that never completed don't count toward this limit. |
| `--auto-retry` | bool | `false` | Keep retrying failed tests as long as at least one recovers per round (overrides `--retries`) |
| `--filter-expression` | string | *(none)* | VSTest filter expression applied during discovery (e.g. `"TestCategory=Smoke"`). Ignored when `--test-list` is provided. |
| `--test-list` | string | *(none)* | Pipe-delimited fully-qualified test names to run directly, skipping discovery (e.g. `"Ns.Class.Test1\|Ns.Class.Test2"`). Takes priority over `--filter-expression`. Empty or omitted falls back to normal discovery. Exits with code 2 if provided FQNs match no tests. Exits with code 2 if values don't look like FQNs (e.g. filter syntax pasted by mistake). |
| `--test-list-file` | path | *(none)* | Path to a file with FQN test names (one per line, or pipe-delimited). For lists too long for the command line. Mutually exclusive with `--test-list`. |
| `--results-dir` | string | *auto temp dir* | Directory for `.trx` result files. TRX is always generated; defaults to `%TEMP%/ParallelTestRunner/run_<timestamp>` if not specified. |
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
