# update1 — Code Review Fixes

Bug fixes, correctness improvements, and minor cleanups identified during code review.

### TASK-001: Fix AutoTuner filter prefix length
- **Status**: done
- **Priority**: high
- **Dependencies**: none
- **Description**: `AutoTuner.cs:6` defines `FilterPrefixLength = 5` with comment `"Name~"`, but `TestBatcher` uses `"FullyQualifiedName="` (19 chars). Change `FilterPrefixLength` to 19 and update the comment. Update existing `AutoTunerTests` to verify batch sizes respect the actual `FullyQualifiedName=` prefix length.

### TASK-002: Include CapturedOutput on all batch results
- **Status**: pending
- **Priority**: high
- **Dependencies**: none
- **Description**: `TestRunner.cs:241` only includes `CapturedOutput` in the `BatchResult` for timed-out batches. When `RetryOrchestrator` processes non-timed-out failed batches (line 152), `CapturedOutput` is null, so `ParseTimedOutOutput` returns empty lists and **every test in the batch is marked as failed — even ones that passed**. This causes passed tests to be unnecessarily re-run, directly undermining the tool's core purpose of only retrying failures. Fix by including `CapturedOutput: outputLines` on all batch results, not just timed-out ones. Add `RetryOrchestratorTests` that verify only genuinely failed tests from a non-timed-out failed batch are retried, and that passed tests are not re-run.

### TASK-003: Fix fuzzy test-name matching in ParseTimedOutOutput
- **Status**: pending
- **Priority**: high
- **Dependencies**: TASK-002
- **Description**: `RetryOrchestrator.cs:308-309` uses bidirectional `Contains` to match test names from VSTest output to batch test FQNs. This is fragile — if a batch contains `Foo.Bar` and `Foo.Bar.Baz`, the output line for `Foo.Bar.Baz` could match `Foo.Bar` first. Since tests are FQNs and VSTest output includes the FQN, replace with exact match (falling back to ends-with if needed). Add `RetryOrchestratorTests` with similarly-named tests (e.g. `Ns.Foo.Bar` and `Ns.Foo.Bar.Baz`) to verify the correct test is matched.

### TASK-004: Extract duplicate TestResultLineRegex to shared location
- **Status**: pending
- **Priority**: medium
- **Dependencies**: TASK-002, TASK-003
- **Description**: `TestRunner.cs:13` and `RetryOrchestrator.cs:13` both define the same `TestResultLineRegex` pattern independently. Extract to a shared static class (e.g. `Patterns.cs`) and reference from both files to avoid drift. Verify existing tests still pass after the move.

### TASK-005: Remove RegexOptions.Compiled from GeneratedRegex
- **Status**: pending
- **Priority**: low
- **Dependencies**: TASK-004
- **Description**: `RetryOrchestrator.cs:13` specifies `RegexOptions.Compiled` on a `[GeneratedRegex]` attribute. This is ignored since source-generated regexes are already compiled at build time. Remove `RegexOptions.Compiled` to match the other regex declarations in the codebase. No new tests needed.

### TASK-006: Clean up trackedProcesses after process exit
- **Status**: pending
- **Priority**: low
- **Dependencies**: none
- **Description**: `TestRunner.cs:25-26,170-173` — processes are added to `trackedProcesses` but never removed after they exit, holding references to disposed `Process` objects for the lifetime of `RunAllAsync`. Remove processes from the list after they exit. No new tests needed (internal cleanup).

### TASK-007: Fix Options.ExtraDotnetTestArgs default value
- **Status**: pending
- **Priority**: low
- **Dependencies**: none
- **Description**: `Options.cs:10` uses `string[] ExtraDotnetTestArgs = default!` — a null-forgiving operator on a null default. The property initializer on line 18 catches it, but the constructor parameter lies to the nullable analyzer. Change the default to `[]` instead of `default!`. No new tests needed.

### TASK-008: Use DateTime.UtcNow for results directory timestamp
- **Status**: pending
- **Priority**: low
- **Dependencies**: none
- **Description**: `Program.cs:141` uses `DateTime.Now` for the timestamped results subdirectory. Two concurrent runs within the same second could collide, and DST transitions create ambiguity. Switch to `DateTime.UtcNow`. No new tests needed.

### TASK-009: Document dotnet vstest deprecation
- **Status**: pending
- **Priority**: low
- **Dependencies**: none
- **Description**: `TestDiscovery.cs:108` — `dotnet vstest` is deprecated by Microsoft in favour of `dotnet test`, but as of .NET 10 it remains the only way to discover fully-qualified test names (`dotnet test --list-tests` only returns display names). Add an XML doc comment on `DiscoverFqnTestsAsync` explaining this is a known deprecation and should be migrated if a future `dotnet test` version adds FQN discovery. No new tests needed.

### TASK-010: Verify all tests pass
- **Status**: pending
- **Priority**: high
- **Dependencies**: TASK-001, TASK-002, TASK-003, TASK-004, TASK-005, TASK-006, TASK-007, TASK-008, TASK-009
- **Description**: Build the solution (`dotnet build ParallelTestRunner.sln`) and run the full test suite (`dotnet test tests/ParallelTestRunner.Tests`). All existing and newly-added tests must pass. Fix any regressions introduced by earlier tasks before marking complete.
