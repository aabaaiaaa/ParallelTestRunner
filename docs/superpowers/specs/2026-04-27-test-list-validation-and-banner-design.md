# Test-list validation, file-based input, and banner upgrade

**Date:** 2026-04-27
**Scope:** Three independent issues fixed in a single change set.

## Summary

1. **Validate `--test-list` content** — reject input that isn't a list of fully-qualified test names (e.g. vstest TestCaseFilter expressions accidentally pasted in). Today, garbage input falls through to discovery / batching, dotnet test runs with junk filters, zero tests execute, and the user is left without a clear signal that their input was malformed.
2. **Support test lists too long to fit on the command line** — Windows command-line limits (`cmd.exe` ~8K chars; `CreateProcess` ~32K) cause "The filename or extension is too long" failures when passing 16k+ char `--test-list` strings via TeamCity build steps. Provide a file-based input.
3. **Upgrade the startup banner** — bolder ASCII title, mirror-themed gradient logo, and visible tool version so users can confirm which build of the tool ran.

The three are related only in that they share files (`Program.cs`, `Options.cs`, `TestDiscovery.cs`) and follow the same TDD discipline. They can be tested independently.

## Out of scope

- Changing `--test-list` semantics or its delimiter.
- Restructuring discovery, batching, or retry orchestration.
- Cross-platform shell-quoting helpers beyond the documented PowerShell examples.
- Internationalising the banner / error messages.

---

## 1. Test-list validation

### Behaviour

A new function `TestDiscovery.ValidateTestList(IReadOnlyList<string>)` returns a list of failure descriptors:

```csharp
internal sealed record TestListValidationFailure(int Index, string Segment, string Reason);
internal static IReadOnlyList<TestListValidationFailure> ValidateTestList(IReadOnlyList<string> segments);
```

A segment is **valid** iff it matches the regex:

```
^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$
```

This enforces the dotted-identifier shape `Identifier(.Identifier)+` with **at least one dot** (so namespace-qualified types are accepted, but bare method names are not). Names like `Ns.Class.Method` and `Outer.Ns.Inner.Class.Test` pass; the user's actual broken example `(FullNameMatchesRegex '(\.X$)')` fails because of the leading paren.

The `Reason` string is short and points to the first offending feature ("contains regex anchor `$`", "contains parenthesis", "not a dotted identifier", etc.) so the user can see *why* a segment was rejected, not just *that* it was.

### Wiring

In `Program.cs`, immediately after `ParseTestList` (and the new `ParseTestListFile` from §2), and **before** discovery runs:

```csharp
if (parsedTestList.Count > 0)
{
    var failures = TestDiscovery.ValidateTestList(parsedTestList);
    if (failures.Count > 0)
    {
        Console.Error.WriteLine("ERROR: --test-list contains values that don't look like fully-qualified test names:");
        foreach (var f in failures)
            Console.Error.WriteLine($"  [{f.Index}] {f.Segment} — {f.Reason}");
        Console.Error.WriteLine("Expected format: Namespace.Class.Method (one or more dots, no spaces, parens, quotes, or regex syntax).");
        toolExitCode = 2;
        return;
    }
}
```

### Existing zero-tests-executed error path

The existing branch (Program.cs lines 294–311) sets `toolExitCode = 2; return;`, which by the surrounding `parseExitCode != 0 ? parseExitCode : toolExitCode` should propagate as exit code 2. The user reports this isn't happening in TeamCity. The plan:

- Add an integration test that triggers the zero-tests-executed path with a syntactically valid but nonexistent FQN (`Bogus.Class.MissingMethod`) and asserts exit code 2.
- If the test passes immediately, we have regression coverage for free.
- If it fails, we have a concrete reproduction and can debug from there. Most likely culprits: a stray `process.Exited` race in `TestRunner` reporting a successful batch when the process died, or a System.CommandLine quirk with action exit codes. Both are localised.

The validator from this section will catch the user's *actual* failing input upstream of this branch, so even if the underlying bug exists, the symptom they reported (regex-filter input slipping through) will be eliminated.

### Tests

**Unit (`TestDiscoveryParseTests.cs`):**

| Test | Input | Expectation |
|---|---|---|
| `ValidateTestList_RejectsVstestFilterExpression` | `(FullNameMatchesRegex '(\.X$)')` | failure: contains paren |
| `ValidateTestList_RejectsParens` | `Ns.Class.Method()` | failure |
| `ValidateTestList_RejectsSingleQuotes` | `'Ns.Class.Method'` | failure |
| `ValidateTestList_RejectsEqualsAndTilde` | `FullyQualifiedName=Ns.Class.M` | failure |
| `ValidateTestList_RejectsRegexAnchors` | `Ns.Class.Method$` | failure |
| `ValidateTestList_RejectsSpaces` | `Ns Class Method` | failure |
| `ValidateTestList_RejectsBareIdentifier` | `MyTest` | failure (zero dots) |
| `ValidateTestList_RejectsLeadingDigit` | `1Ns.Class.Method` | failure |
| `ValidateTestList_AcceptsValidFqn` | `Ns.Class.Method` | empty failures |
| `ValidateTestList_AcceptsDeepNamespace` | `A.B.C.D.E.Method` | empty failures |
| `ValidateTestList_AcceptsUnderscores` | `_Foo.Bar_.Test_1` | empty failures |
| `ValidateTestList_ReportsAllFailures` | `[Good.Class.M, bad input, Also.Good.M]` | one failure for index 1 only |

**Integration (`IntegrationTests.cs`):**

| Test | Behaviour |
|---|---|
| `InvalidTestList_RegexFilterSyntax_ExitCode2` | `--test-list "(FullNameMatchesRegex '...')"` → exit 2, error message contains "ERROR: --test-list contains values" |
| `InvalidTestList_PartiallyValid_ListsBadSegments` | `--test-list "Good.Class.M\|bad\|Also.Good.M"` → exit 2, output mentions `bad` |
| `ZeroTestsExecuted_FromValidButUnknownFqn_ExitCode2` | `--test-list "Bogus.Class.NotARealMethod"` → exit 2, error contains "zero tests were executed" |

---

## 2. File-based test list input

### Behaviour

New CLI option:

```csharp
var testListFileOption = new Option<string?>("--test-list-file")
{
    Description = "Path to a file containing fully-qualified test names (one per line, or pipe-delimited). Mutually exclusive with --test-list.",
    Arity = ArgumentArity.ZeroOrOne
};
```

Parser: `TestDiscovery.ParseTestListFile(string path)` reads the file and splits on **both** `\n` and `|`, trimming each segment, skipping blanks, deduplicating in order. UTF-8 with optional BOM. Throws `FileNotFoundException` if the path doesn't exist.

Mutual exclusion enforced in `Program.cs` before any work:

```csharp
if (testList is not null && testListFile is not null)
{
    Console.Error.WriteLine("ERROR: --test-list and --test-list-file are mutually exclusive. Use one or the other.");
    toolExitCode = 2;
    return;
}
```

When `--test-list-file` is the source, the banner line reads:

```
  Test list: provided from <path> (N tests)
```

Validation from §1 runs on the parsed segments regardless of source.

### Why a file (and not response files / stdin)

- **File option is explicit** — appears in `--help`, easy to document, easy to test.
- **System.CommandLine `@response-file` support** would also bypass the OS argv limit, but it requires the caller to restructure how they invoke the tool (`parallel-test-runner @args.txt`). A new flag is more direct.
- **Stdin** would require shell-piping, which complicates TeamCity step config.

### Options.cs

Add field:

```csharp
public sealed record Options(
    ...,
    string? TestList,
    string? TestListFile,    // new
    ...
);
```

### Tests

**Unit (`TestDiscoveryParseTests.cs`):**

| Test | Input | Expectation |
|---|---|---|
| `ParseTestListFile_NewlineSeparated` | `"A.B.C\nD.E.F\n"` | `[A.B.C, D.E.F]` |
| `ParseTestListFile_PipeSeparated` | `"A.B.C\|D.E.F"` | `[A.B.C, D.E.F]` |
| `ParseTestListFile_Mixed` | `"A.B.C\|D.E.F\nG.H.I"` | `[A.B.C, D.E.F, G.H.I]` |
| `ParseTestListFile_TrimsWhitespace` | `" A.B.C \n  D.E.F  "` | `[A.B.C, D.E.F]` |
| `ParseTestListFile_HandlesBom` | UTF-8 BOM + content | content parsed correctly, BOM stripped |
| `ParseTestListFile_Deduplicates` | `"A.B.C\nA.B.C"` | `[A.B.C]` |
| `ParseTestListFile_MissingFile_Throws` | nonexistent path | `FileNotFoundException` |

**Integration (`IntegrationTests.cs`):**

| Test | Behaviour |
|---|---|
| `TestListFile_AllDummyTests_RunsSuccessfully` | Write all 70 real DummyTestProject FQNs to a temp file; run with `--test-list-file <path>`; assert exit 0 and that the banner reports 70 tests from the file. Exercises the happy path. |
| `TestListFile_VeryLongList_OsInvocationSucceeds` | Generate ~250 syntactically-valid FQNs (`MyApp.Tests.PaddedNamespace<N>.PaddedClass.PaddedMethod` — 250 × ~80 chars ≈ 20k chars, well past the 16k `cmd.exe` threshold and approaching the 32k `CreateProcess` limit); write to file; run with `--test-list-file <path>`; assert exit code is **2** with the "zero tests executed" message. Failure here would mean the OS-level invocation broke with the long file (it shouldn't — the list is in the file, not on argv). |
| `TestListFile_AndTestList_ExitCode2` | both flags supplied → exit 2 with mutual-exclusion error |
| `TestListFile_MissingPath_ExitCode2` | `--test-list-file does-not-exist.txt` → exit 2 with "path not found" error |
| `TestListFile_InvalidContent_ExitCode2` | file contains `(FullNameMatchesRegex '...')` → exit 2 via the §1 validator |

The `VeryLongList` test is the one that specifically guards the user's reported issue. The padded FQNs pass validation but match nothing in the assembly, so the expected exit code is 2 from the zero-tests-executed branch — the *point* is that we got that far without an OS-level "filename or extension is too long" failure.

### README updates

Add a section near the existing `--test-list` documentation:

````markdown
### `--test-list-file <path>`

For lists too long to fit on the command line (Windows `cmd.exe` is limited to ~8000 characters), pass test names via a file. Each line, or each segment between `|`, is treated as one fully-qualified test name. Mutually exclusive with `--test-list`.

PowerShell:

```powershell
# Build a list and write it to a file (UTF-8)
$tests = @(
    'MyApp.Tests.PolicyFeature.AcceptCancellationQuote',
    'MyApp.Tests.PolicyFeature.RenewalRiskExclusion'
)
$tests | Out-File -FilePath rerun.txt -Encoding utf8

parallel-test-runner .\MyTests.csproj --test-list-file rerun.txt
```

To rerun a list a CI job already produced (e.g. captured failed-test FQNs to `failed-tests.txt`):

```powershell
parallel-test-runner .\MyTests.csproj --test-list-file failed-tests.txt
```

````

---

## 3. Banner upgrade

### Behaviour

Replace `PrintBanner` ASCII with the locked-in design:

```
   ░▒▓ ██████╗  █████╗ ██████╗  █████╗ ██╗     ██╗     ███████╗██╗
   ░▒▓ ██╔══██╗██╔══██╗██╔══██╗██╔══██╗██║     ██║     ██╔════╝██║
   ░▒▓ ██████╔╝███████║██████╔╝███████║██║     ██║     █████╗  ██║
   ░▒▓ ██╔═══╝ ██╔══██║██╔══██╗██╔══██║██║     ██║     ██╔══╝  ██║
   ░▒▓ ██║     ██║  ██║██║  ██║██║  ██║███████╗███████╗███████╗███████╗
   ░▒▓ ╚═╝     ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝╚══════╝╚══════╝╚══════╝╚══════╝
   ░▒▓ ████████╗███████╗███████╗████████╗    ██████╗ ██╗   ██╗███╗   ██╗███╗   ██╗███████╗██████╗
   ░▒▓ ╚══██╔══╝██╔════╝██╔════╝╚══██╔══╝    ██╔══██╗██║   ██║████╗  ██║████╗  ██║██╔════╝██╔══██╗
   ░▒▓    ██║   █████╗  ███████╗   ██║       ██████╔╝██║   ██║██╔██╗ ██║██╔██╗ ██║█████╗  ██████╔╝
   ░▒▓    ██║   ██╔══╝  ╚════██║   ██║       ██╔══██╗██║   ██║██║╚██╗██║██║╚██╗██║██╔══╝  ██╔══██╗
   ░▒▓    ██║   ███████╗███████║   ██║       ██║  ██║╚██████╔╝██║ ╚████║██║ ╚████║███████╗██║  ██║
   ░▒▓    ╚═╝   ╚══════╝╚══════╝   ╚═╝       ╚═╝  ╚═╝ ╚═════╝ ╚═╝  ╚═══╝╚═╝  ╚═══╝╚══════╝╚═╝  ╚═╝   v1.1.2
```

- Title: ANSI Shadow font, two rows ("Parallel" / "Test Runner")
- Left gradient: `░▒▓` (one char each density), 1-space gap before the title's leading `█`
- Version: pulled at runtime via
  ```csharp
  typeof(Options).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
      ?? typeof(Options).Assembly.GetName().Version?.ToString()
      ?? "unknown"
  ```
  rendered as `v<x.y.z>` at the end of the second-row title block, on the same line as its bottom edge.
- Config lines below: unchanged.

### Tests

**Unit:** Banner is printed via a function that's hard to assert against directly (writes to `Console.Error`). Refactor `PrintBanner` so the body is computed by a pure helper `BuildBanner(Options options, string version) -> string`, and `PrintBanner` writes it. Then unit-test `BuildBanner`:

| Test | Expectation |
|---|---|
| `BuildBanner_ContainsVersion` | output contains `v1.2.3` when `version = "1.2.3"` |
| `BuildBanner_ContainsGradient` | output contains `░▒▓` |
| `BuildBanner_ContainsAnsiShadowFingerprint` | output contains a known substring like `██████╗  █████╗ ██████╗  █████╗ ██╗` (Parallel's first line) |
| `BuildBanner_FallsBackToUnknown_WhenVersionNull` | passing `null` version renders `vunknown` |

**Integration:** Update existing `IntegrationTests.cs` assertions that reference the old banner. The new fingerprint string is the gradient `░▒▓` (unique to the new banner) — assert it appears in stderr.

---

## File-by-file summary

| File | Change |
|---|---|
| `src/ParallelTestRunner/Program.cs` | New `--test-list-file` option; mutual-exclusion check; validator wiring; `PrintBanner` rewrite; version lookup |
| `src/ParallelTestRunner/Options.cs` | Add `TestListFile` field |
| `src/ParallelTestRunner/TestDiscovery.cs` | New `ValidateTestList` and `ParseTestListFile` |
| `tests/ParallelTestRunner.Tests/TestDiscoveryParseTests.cs` | New unit tests for validator + file parser |
| `tests/ParallelTestRunner.Tests/IntegrationTests.cs` | New integration tests; update banner-string assertions |
| `README.md` | `--test-list-file` docs + PowerShell example |
| `CLAUDE.md` | Note the new option, new banner format, new validator |

## Implementation order (TDD)

1. Write all unit tests for `ValidateTestList` (red).
2. Implement `ValidateTestList` (green).
3. Wire validator into `Program.cs`; write integration tests for `--test-list` invalid input (red → green).
4. Write integration test for zero-tests-executed exit code 2 (existing path); investigate if it fails.
5. Write unit tests for `ParseTestListFile` (red).
6. Implement `ParseTestListFile` (green).
7. Add `--test-list-file` option to `Program.cs` and `Options.cs`; write integration tests (mutual exclusion, missing file, long list) (red → green).
8. Update README with PowerShell examples.
9. Refactor `PrintBanner` into `BuildBanner` + writer; write unit tests (red); rewrite the ASCII art (green).
10. Update integration test banner assertions; update CLAUDE.md.
11. Run full test suite; verify exit codes; verify TeamCity passthrough still works.
