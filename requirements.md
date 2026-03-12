# Parallel Test Runner - Requirements

## Context

Running `dotnet test` on a large MSTest solution is slow when tests execute sequentially. This tool discovers all tests, splits them into configurable batches, and runs multiple `dotnet test` processes in parallel. It's designed for TeamCity CI, where the TeamCity.VSTest.TestAdapter outputs `##teamcity` service messages that must be forwarded in real-time.

## Architecture

**.NET 10 console app** packaged as a **dotnet tool** using `System.Diagnostics.Process` for subprocess management and `System.CommandLine` for argument parsing. Distributed via `dotnet tool install` — either globally or as a local tool manifest.

### Flow
```
1. Discover tests:  dotnet test <project> --list-tests --no-build
2. Parse output:    Extract FQNs after "The following Tests are available:" sentinel
3. Batch:           Chunk into groups of --batch-size
4. Run parallel:    SemaphoreSlim(--max-parallelism) + Task.WhenAll
5. Forward output:  OutputDataReceived -> lock -> Console.WriteLine (line-safe)
6. Report:          Summary to stderr, exit code reflects pass/fail
```

### Project Structure
```
ParallelTestRunner/
├── ParallelTestRunner.sln
├── src/
│   └── ParallelTestRunner/
│       ├── ParallelTestRunner.csproj
│       ├── Program.cs
│       ├── Options.cs
│       ├── TestDiscovery.cs
│       ├── TestBatcher.cs
│       ├── TestRunner.cs
│       └── ResultCollator.cs
└── tests/
    ├── ParallelTestRunner.Tests/
    │   ├── ParallelTestRunner.Tests.csproj
    │   ├── TestBatcherTests.cs
    │   └── IntegrationTests.cs
    └── DummyTestProject/
        ├── DummyTestProject.csproj
        └── DummyTests.cs
```

---

## Tasks

### TASK-001: Set up solution and project structure
- **Status**: done
- **Priority**: high
- **Dependencies**: none
- **Description**: Create `ParallelTestRunner.sln`, `src/ParallelTestRunner/ParallelTestRunner.csproj` (net10.0, System.CommandLine 2.0.0-beta5), `tests/ParallelTestRunner.Tests/ParallelTestRunner.Tests.csproj` (MSTest, net10.0), and `tests/DummyTestProject/DummyTestProject.csproj` (MSTest, net10.0). Wire all projects into the solution. Enable nullable and implicit usings. Configure `ParallelTestRunner.csproj` as a dotnet tool by adding `<PackAsTool>true</PackAsTool>`, `<ToolCommandName>parallel-test-runner</ToolCommandName>`, and `<PackageOutputPath>./nupkg</PackageOutputPath>`. Include standard NuGet metadata (`PackageId`, `Version`, `Authors`, `Description`).

### TASK-002: Implement CLI options and entry point
- **Status**: done
- **Priority**: high
- **Dependencies**: TASK-001
- **Description**: Create `Options.cs` as a record with: `ProjectPath` (required argument), `BatchSize` (default 50), `MaxParallelism` (default `Math.Max(1, Environment.ProcessorCount / 2)`), `ExtraDotnetTestArgs` (string[]), `ResultsDirectory` (string?). Create `Program.cs` with a `RootCommand` using System.CommandLine that maps CLI args to `Options`. Wire `Console.CancelKeyPress` to a `CancellationTokenSource` for graceful Ctrl+C. On startup, print an ASCII art banner for "Parallel Test Runner" to stderr, followed by detected core count and chosen parallelism. The banner should be a hardcoded string literal — no runtime generation or external dependency needed.

### TASK-003: Implement test discovery
- **Status**: done
- **Priority**: high
- **Dependencies**: TASK-001
- **Description**: Create `TestDiscovery.cs` with `DiscoverAsync(string projectPath, string[] extraArgs, CancellationToken ct)`. Runs `dotnet test <project> --list-tests --no-build [extraArgs]`, captures stdout, forwards stderr to `Console.Error` in real-time. Parses output by finding the sentinel line `"The following Tests are available:"` and collecting subsequent indented non-empty lines (trimmed). Deduplicates parameterized tests by stripping `(...)` suffix. Throws on non-zero exit code or zero tests found.

### TASK-004: Implement test batching
- **Status**: done
- **Priority**: high
- **Dependencies**: TASK-001
- **Description**: Create `TestBatcher.cs` with `CreateBatches(IReadOnlyList<string> testNames, int batchSize)`. Uses `Chunk(batchSize)` to split tests into groups. Post-check: if any batch's filter string (`"FullyQualifiedName=X|FullyQualifiedName=Y|..."`) exceeds 7000 chars, auto-split that batch into smaller chunks to stay within Windows command-line length limits.

### TASK-005: Implement parallel test runner
- **Status**: done
- **Priority**: high
- **Dependencies**: TASK-003, TASK-004
- **Description**: Create `TestRunner.cs` — the core orchestration class. Define `record BatchResult(int BatchIndex, int TestCount, int ExitCode)`. Uses `SemaphoreSlim(maxParallelism)` to throttle concurrent `dotnet test` processes. Accepts a `CancellationToken`; on cancellation, kill running child processes via `Process.Kill()` and propagate cancellation. For each batch: acquires semaphore, builds filter string (`--filter "FullyQualifiedName=X|FullyQualifiedName=Y|..."`), starts a `Process` with `RedirectStandardOutput/Error`, uses `BeginOutputReadLine`/`BeginErrorReadLine` with a shared `lock` around `Console.WriteLine` to ensure line-safe interleaving of TeamCity service messages. Passes `--no-build` on all invocations. Auto-detects TeamCity via `Environment.GetEnvironmentVariable("TEAMCITY_VERSION")` — if set, appends `/TestAdapterPath:. /Logger:teamcity`. Optionally writes `--logger "trx;LogFileName=batch_N.trx"` if results-dir is specified. Returns `BatchResult(batchIndex, testCount, exitCode)`.

### TASK-006: Implement result collation
- **Status**: done
- **Priority**: high
- **Dependencies**: TASK-005
- **Description**: Create `ResultCollator.cs` with `Collate(BatchResult[] results)`. Prints summary to stderr (batch count, total test count, failed batches with details). Returns exit code: 0 if all passed, 1 if any failed.

### TASK-007: Wire orchestration pipeline in Program.cs
- **Status**: done
- **Priority**: high
- **Dependencies**: TASK-002, TASK-003, TASK-004, TASK-005, TASK-006
- **Description**: In the `RootCommand` handler in `Program.cs`, wire the full pipeline: `TestDiscovery.DiscoverAsync` → log test count → `TestBatcher.CreateBatches` → log batch count → `TestRunner.RunAllAsync` → `ResultCollator.Collate` → `Environment.Exit(exitCode)`. Exit code 2 for infrastructure failures (discovery fails, zero tests).

### TASK-008: Create DummyTestProject
- **Status**: done
- **Priority**: medium
- **Dependencies**: TASK-001
- **Description**: Create `tests/DummyTestProject/DummyTests.cs` with exactly 20 MSTest methods across multiple classes and namespaces. Include: passing tests, deliberately failing tests (controlled via `FAIL_TESTS` environment variable), `[DataRow]` parameterized tests, and a couple of slow tests (`Thread.Sleep(2000)`) to verify parallelism provides speedup.

### TASK-009: Unit tests for TestBatcher
- **Status**: done
- **Priority**: medium
- **Dependencies**: TASK-004
- **Description**: Create `TestBatcherTests.cs` with tests for: evenly divisible batching (10 tests, batch size 5 → 2 batches), remainder batching (11 tests, batch size 5 → 3 batches), batch size larger than test count (→ 1 batch), filter string length auto-split when exceeding 7000 char limit. TestBatcher is pure logic with no process dependencies, so unit tests are appropriate here.

### TASK-010: Integration tests
- **Status**: done
- **Priority**: medium
- **Dependencies**: TASK-007, TASK-008
- **Description**: Create `IntegrationTests.cs` that runs the full tool as a process against `DummyTestProject`. DummyTestProject should contain exactly 20 test methods so assertions are deterministic. Tests: discovers correct number of tests (20), runs with `--batch-size 5 --max-parallelism 2` and verifies all tests execute, verifies exit code 0 when all tests pass, runs with `FAIL_TESTS` env var set and verifies exit code 1. Locate DummyTestProject via solution-relative path resolved from the test assembly location.

### TASK-011: Create README.md with usage documentation
- **Status**: done
- **Priority**: medium
- **Dependencies**: TASK-002, TASK-005
- **Description**: Create a `README.md` covering the following sections:

  **1. Installation** — How to install as a dotnet tool:
  - Global: `dotnet tool install --global ParallelTestRunner --add-source ./nupkg`
  - Local (tool manifest): `dotnet new tool-manifest` then `dotnet tool install ParallelTestRunner --add-source ./nupkg`
  - From a NuGet feed (if published): `dotnet tool install --global ParallelTestRunner`
  - How to pack: `dotnet pack src/ParallelTestRunner`

  **2. Local Developer Usage** — How to invoke the tool locally for testing during development. Two approaches:
  - **Installed tool**: `parallel-test-runner <path-to-test-project>` (global) or `dotnet tool run parallel-test-runner <path-to-test-project>` (local manifest)
  - **Without installing** (dev inner loop): `dotnet run --project src/ParallelTestRunner -- <path-to-test-project>`
  - Example with options: `parallel-test-runner MyTests.csproj --batch-size 10 --max-parallelism 4`
  - Passing extra dotnet test args: `parallel-test-runner MyTests.csproj -- --configuration Release --no-restore`

  **3. TeamCity Build Step Configuration** — How to add build steps in TeamCity:
  - **Step 1 (build)**: `dotnet build MySolution.sln` — required because the tool always passes `--no-build`
  - **Step 2 (install tool)**: `dotnet tool install --global ParallelTestRunner --add-source <feed-or-path>` (or restore from a local tool manifest)
  - **Step 3 (run tests)**: `parallel-test-runner MyTests.csproj --batch-size 50 --max-parallelism 4`
  - Document that TeamCity is auto-detected via `TEAMCITY_VERSION` env var (no manual flag needed), which automatically appends `/TestAdapterPath:. /Logger:teamcity` so `##teamcity` service messages flow to the build log natively
  - Include a complete sample build step script

  **4. Configuration Reference** — Table of all CLI options with name, type, default, and description:
  - `<project>` (required) — path to the test project/solution
  - `--batch-size` (int, default 50) — number of tests per batch
  - `--max-parallelism` (int, default `CPU cores / 2`) — max concurrent `dotnet test` processes
  - `--results-dir` (string, optional) — directory for `.trx` result files
  - Extra args (after `--`) — any additional args passed through to `dotnet test`

  **5. Exit Codes** — 0 = all passed, 1 = test failures, 2 = infrastructure error

  **6. Environment Variables** — `TEAMCITY_VERSION` (auto-detection for TeamCity logger), `FAIL_TESTS` (used by DummyTestProject for testing failure scenarios)

### TASK-012: Build and run all tests to verify completion
- **Status**: pending
- **Priority**: high
- **Dependencies**: TASK-009, TASK-010, TASK-011
- **Description**: Run `dotnet build ParallelTestRunner.sln` and `dotnet test ParallelTestRunner.sln` to verify all unit and integration tests pass. Fix any issues found. This is the final verification step to confirm the project is complete and functional.

---

## Key Design Decisions

1. **Line-level locking for stdout**: `OutputDataReceived` fires per-line; a shared `lock` around `Console.WriteLine` prevents interleaving of TeamCity `##teamcity` service messages across concurrent processes.

2. **--no-build always**: Assumes CI has a prior build step. Avoids concurrent build conflicts.

3. **TeamCity auto-detection**: `TEAMCITY_VERSION` env var presence triggers automatic `/TestAdapterPath:. /Logger:teamcity` args.

4. **CPU-based parallelism default**: `Math.Max(1, Environment.ProcessorCount / 2)` — logged to stderr on startup.

5. **Exit codes**: 0 = all passed, 1 = test failures, 2 = infrastructure failure.

6. **Filter length safety**: Auto-split batches if filter string exceeds 7000 chars (Windows cmd limit).

7. **Integration tests over unit tests**: The core value of this tool is process orchestration — spawning `dotnet test`, capturing output, managing parallelism. Over-abstracting behind seams just to enable unit tests would test fake behavior, not real behavior. Unit tests are reserved for pure logic (e.g., TestBatcher). Everything involving process execution is tested end-to-end via integration tests against DummyTestProject, which is fast and fully controlled.

## Error Handling
| Scenario | Behavior |
|---|---|
| Discovery exits non-zero | Forward stderr, exit 2 |
| Zero tests discovered | Error message, exit 2 |
| Batch process fails | Record result, continue other batches |
| Filter too long | Auto-split batch into smaller chunks |
| Ctrl+C | Cancel all in-flight processes via CancellationToken |
