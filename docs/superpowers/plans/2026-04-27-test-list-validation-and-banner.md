# Test-list validation, file input, and banner upgrade — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add strict validation to `--test-list` (catching vstest filter syntax accidentally pasted in), add `--test-list-file` for lists too long for the Windows command line, and upgrade the startup banner with a faded gradient logo and visible tool version.

**Architecture:** Three small, independent changes share `Program.cs` / `Options.cs` / `TestDiscovery.cs`. New `Banner.cs` extracted as a pure helper for testability. All changes are TDD: failing test → minimal implementation → green → commit. Total ~12 commits.

**Tech Stack:** .NET 9, C#, MSTest 3.x, System.CommandLine 2.0.0-beta5, partial regex source generators (`[GeneratedRegex]`), MSTest [DataRow] for parameterised tests where it reduces noise.

**Spec reference:** `docs/superpowers/specs/2026-04-27-test-list-validation-and-banner-design.md`

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `src/ParallelTestRunner/Options.cs` | Modify | Add `TestListFile` field |
| `src/ParallelTestRunner/TestDiscovery.cs` | Modify | Add `TestListValidationFailure` record, `ValidateTestList`, `ParseTestListFile` |
| `src/ParallelTestRunner/Banner.cs` | Create | Pure `BuildBanner(Options, string version)` helper. Single responsibility. |
| `src/ParallelTestRunner/Program.cs` | Modify | New CLI option, mutual exclusion check, validator wiring, version lookup, replace inline banner with `Banner.BuildBanner` call |
| `tests/ParallelTestRunner.Tests/TestDiscoveryParseTests.cs` | Modify | Add unit tests for `ValidateTestList` and `ParseTestListFile` |
| `tests/ParallelTestRunner.Tests/BannerTests.cs` | Create | Unit tests for `BuildBanner` |
| `tests/ParallelTestRunner.Tests/IntegrationTests.cs` | Modify | Add integration tests for invalid input, `--test-list-file`, mutual exclusion, missing file, very long list; update banner-substring assertions |
| `README.md` | Modify | Document `--test-list-file` with PowerShell examples |
| `CLAUDE.md` | Modify | Note new option, new banner, new validator |

---

## Task 1: Add `TestListValidationFailure` record and failing unit tests for `ValidateTestList`

**Files:**
- Modify: `src/ParallelTestRunner/TestDiscovery.cs` (add record stub only)
- Test: `tests/ParallelTestRunner.Tests/TestDiscoveryParseTests.cs`

- [ ] **Step 1: Add the `TestListValidationFailure` record stub to `TestDiscovery.cs`**

Add inside the `TestDiscovery` static class (or alongside it in the same file), at the bottom of the namespace:

```csharp
internal sealed record TestListValidationFailure(int Index, string Segment, string Reason);
```

Also add a stub `ValidateTestList` method that throws `NotImplementedException` so test compilation succeeds:

```csharp
internal static IReadOnlyList<TestListValidationFailure> ValidateTestList(IReadOnlyList<string> segments)
    => throw new NotImplementedException();
```

- [ ] **Step 2: Add the failing unit tests to `TestDiscoveryParseTests.cs`**

Append to the existing `TestDiscoveryParseTests` class:

```csharp
[TestMethod]
public void ValidateTestList_ValidFqn_ReturnsNoFailures()
{
    var failures = TestDiscovery.ValidateTestList(["Ns.Class.Method"]);
    Assert.AreEqual(0, failures.Count);
}

[TestMethod]
public void ValidateTestList_DeepNamespace_ReturnsNoFailures()
{
    var failures = TestDiscovery.ValidateTestList(["A.B.C.D.E.Method"]);
    Assert.AreEqual(0, failures.Count);
}

[TestMethod]
public void ValidateTestList_UnderscoresAndDigits_ReturnsNoFailures()
{
    var failures = TestDiscovery.ValidateTestList(["_Foo.Bar_.Test_1"]);
    Assert.AreEqual(0, failures.Count);
}

[TestMethod]
public void ValidateTestList_VstestFilterExpression_ReturnsFailure()
{
    var failures = TestDiscovery.ValidateTestList(["(FullNameMatchesRegex '(\\.X$)')"]);
    Assert.AreEqual(1, failures.Count);
    Assert.AreEqual(0, failures[0].Index);
}

[TestMethod]
public void ValidateTestList_Parens_ReturnsFailure()
{
    var failures = TestDiscovery.ValidateTestList(["Ns.Class.Method()"]);
    Assert.AreEqual(1, failures.Count);
    StringAssert.Contains(failures[0].Reason.ToLowerInvariant(), "paren");
}

[TestMethod]
public void ValidateTestList_SingleQuotes_ReturnsFailure()
{
    var failures = TestDiscovery.ValidateTestList(["'Ns.Class.Method'"]);
    Assert.AreEqual(1, failures.Count);
}

[TestMethod]
public void ValidateTestList_EqualsSign_ReturnsFailure()
{
    var failures = TestDiscovery.ValidateTestList(["FullyQualifiedName=Ns.Class.M"]);
    Assert.AreEqual(1, failures.Count);
}

[TestMethod]
public void ValidateTestList_RegexAnchor_ReturnsFailure()
{
    var failures = TestDiscovery.ValidateTestList(["Ns.Class.Method$"]);
    Assert.AreEqual(1, failures.Count);
}

[TestMethod]
public void ValidateTestList_Spaces_ReturnsFailure()
{
    var failures = TestDiscovery.ValidateTestList(["Ns Class Method"]);
    Assert.AreEqual(1, failures.Count);
}

[TestMethod]
public void ValidateTestList_BareIdentifierNoDots_ReturnsFailure()
{
    var failures = TestDiscovery.ValidateTestList(["MyTest"]);
    Assert.AreEqual(1, failures.Count);
    StringAssert.Contains(failures[0].Reason.ToLowerInvariant(), "dot");
}

[TestMethod]
public void ValidateTestList_LeadingDigit_ReturnsFailure()
{
    var failures = TestDiscovery.ValidateTestList(["1Ns.Class.Method"]);
    Assert.AreEqual(1, failures.Count);
}

[TestMethod]
public void ValidateTestList_PartialFailures_ReportsOnlyBadOnes()
{
    var failures = TestDiscovery.ValidateTestList(["Good.Class.M", "bad input", "Also.Good.M"]);
    Assert.AreEqual(1, failures.Count);
    Assert.AreEqual(1, failures[0].Index);
    Assert.AreEqual("bad input", failures[0].Segment);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "ClassName=ParallelTestRunner.Tests.TestDiscoveryParseTests&Name~ValidateTestList" --nologo`
Expected: 12 tests fail (with `NotImplementedException`)

- [ ] **Step 4: Commit**

```bash
git add src/ParallelTestRunner/TestDiscovery.cs tests/ParallelTestRunner.Tests/TestDiscoveryParseTests.cs
git commit -m "test: add failing unit tests for ValidateTestList"
```

---

## Task 2: Implement `ValidateTestList`

**Files:**
- Modify: `src/ParallelTestRunner/TestDiscovery.cs`

- [ ] **Step 1: Replace the `NotImplementedException` stub with the real implementation**

Add this generated regex declaration alongside the existing `DllPathRegex()` in the partial class:

```csharp
[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$")]
private static partial Regex FqnShapeRegex();
```

Replace the stub `ValidateTestList` with:

```csharp
internal static IReadOnlyList<TestListValidationFailure> ValidateTestList(IReadOnlyList<string> segments)
{
    var failures = new List<TestListValidationFailure>();
    var regex = FqnShapeRegex();

    for (var i = 0; i < segments.Count; i++)
    {
        var segment = segments[i];

        if (string.IsNullOrWhiteSpace(segment))
        {
            failures.Add(new TestListValidationFailure(i, segment, "empty segment"));
            continue;
        }

        if (regex.IsMatch(segment))
            continue;

        failures.Add(new TestListValidationFailure(i, segment, DescribeFailure(segment)));
    }

    return failures;
}

private static string DescribeFailure(string segment)
{
    if (segment.Contains('('))   return "contains parenthesis '(' — looks like a filter expression, not an FQN";
    if (segment.Contains(')'))   return "contains parenthesis ')' — looks like a filter expression, not an FQN";
    if (segment.Contains('\''))  return "contains single quote — looks like a filter expression, not an FQN";
    if (segment.Contains('='))   return "contains '=' — looks like a filter expression (e.g. FullyQualifiedName=...)";
    if (segment.Contains('~'))   return "contains '~' — looks like a filter expression";
    if (segment.Contains('$'))   return "contains regex anchor '$'";
    if (segment.Contains('^'))   return "contains regex anchor '^'";
    if (segment.Contains(' '))   return "contains spaces — FQNs do not contain spaces";
    if (!segment.Contains('.'))  return "no dots — expected at least Namespace.Class.Method";
    if (char.IsDigit(segment[0])) return "starts with a digit — identifiers must start with a letter or underscore";
    return "not a valid dotted identifier (Namespace.Class.Method shape)";
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "ClassName=ParallelTestRunner.Tests.TestDiscoveryParseTests&Name~ValidateTestList" --nologo`
Expected: 12 tests pass

- [ ] **Step 3: Commit**

```bash
git add src/ParallelTestRunner/TestDiscovery.cs
git commit -m "feat: implement strict FQN validation for --test-list"
```

---

## Task 3: Wire validator into `Program.cs` + integration test

**Files:**
- Modify: `src/ParallelTestRunner/Program.cs:198-209` (after `parsedTestList` is computed, before discovery)
- Modify: `tests/ParallelTestRunner.Tests/IntegrationTests.cs`

- [ ] **Step 1: Add the failing integration test to `IntegrationTests.cs`**

Append to the `IntegrationTests` class:

```csharp
[TestMethod]
public void TestList_VstestFilterSyntax_ExitCode2WithValidationError()
{
    // The user's actual broken case: vstest TestCaseFilter expression accidentally
    // pasted into --test-list. Should fail validation upfront, not slip through to
    // dotnet test as junk filters.
    var badInput = "(FullNameMatchesRegex '(\\.RequestingPolicyDetails$)|(\\.GetQuoteAfterCopy$)')";

    var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list \"{badInput}\"");

    Assert.AreEqual(2, result.ExitCode, $"Expected exit code 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
    StringAssert.Contains(result.Stderr, "ERROR: --test-list contains values that don't look like fully-qualified test names");
}

[TestMethod]
public void TestList_PartiallyValidInput_ExitCode2ListsBadSegments()
{
    var mixed = "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult|bad input here|DummyTestProject.Arithmetic.BasicMathTests.Subtraction_ReturnsCorrectResult";

    var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list \"{mixed}\"");

    Assert.AreEqual(2, result.ExitCode, $"Expected exit code 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
    StringAssert.Contains(result.Stderr, "bad input here");
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "Name~TestList_VstestFilterSyntax|Name~TestList_PartiallyValidInput" --nologo`
Expected: both fail (the tool currently accepts the bad input and runs through to discovery / zero-tests-executed)

- [ ] **Step 3: Wire the validator into `Program.cs`**

Find this block in `Program.cs` (around lines 198-209):

```csharp
var parsedTestList = TestDiscovery.ParseTestList(options.TestList);
if (parsedTestList.Count > 0)
{
    tests = parsedTestList;
    Console.Error.WriteLine("  Skipping test discovery — using provided test list");
```

Insert the validator right **after** `var parsedTestList = TestDiscovery.ParseTestList(options.TestList);` and **before** the `if (parsedTestList.Count > 0)`:

```csharp
var parsedTestList = TestDiscovery.ParseTestList(options.TestList);

if (parsedTestList.Count > 0)
{
    var validationFailures = TestDiscovery.ValidateTestList(parsedTestList);
    if (validationFailures.Count > 0)
    {
        Console.Error.WriteLine("ERROR: --test-list contains values that don't look like fully-qualified test names:");
        foreach (var failure in validationFailures)
            Console.Error.WriteLine($"  [{failure.Index}] {failure.Segment} — {failure.Reason}");
        Console.Error.WriteLine("Expected format: Namespace.Class.Method (one or more dots, no spaces, parens, quotes, or regex syntax).");
        toolExitCode = 2;
        return;
    }

    tests = parsedTestList;
    Console.Error.WriteLine("  Skipping test discovery — using provided test list");
```

(Note: the existing `if (parsedTestList.Count > 0)` block stays — we just inject the validator at the top of it.)

- [ ] **Step 4: Run all tests to verify they pass and nothing regressed**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "Name~TestList" --nologo`
Expected: all `TestList_*` tests pass (including the new two and the existing ones like `TestList_NonMatchingFqns_ExitCode2` which uses valid-shape FQNs and should continue to work)

- [ ] **Step 5: Commit**

```bash
git add src/ParallelTestRunner/Program.cs tests/ParallelTestRunner.Tests/IntegrationTests.cs
git commit -m "feat: validate --test-list segments and exit 2 on invalid input"
```

---

## Task 4: Add failing unit tests for `ParseTestListFile`

**Files:**
- Modify: `src/ParallelTestRunner/TestDiscovery.cs` (stub only)
- Test: `tests/ParallelTestRunner.Tests/TestDiscoveryParseTests.cs`

- [ ] **Step 1: Add the stub method to `TestDiscovery.cs`**

```csharp
internal static IReadOnlyList<string> ParseTestListFile(string path)
    => throw new NotImplementedException();
```

- [ ] **Step 2: Add the failing tests to `TestDiscoveryParseTests.cs`**

Append:

```csharp
[TestMethod]
public void ParseTestListFile_NewlineSeparated_Parses()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "A.B.C\nD.E.F\n");
        var result = TestDiscovery.ParseTestListFile(path);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("A.B.C", result[0]);
        Assert.AreEqual("D.E.F", result[1]);
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void ParseTestListFile_PipeSeparated_Parses()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "A.B.C|D.E.F");
        var result = TestDiscovery.ParseTestListFile(path);
        Assert.AreEqual(2, result.Count);
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void ParseTestListFile_MixedDelimiters_Parses()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "A.B.C|D.E.F\nG.H.I");
        var result = TestDiscovery.ParseTestListFile(path);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("A.B.C", result[0]);
        Assert.AreEqual("D.E.F", result[1]);
        Assert.AreEqual("G.H.I", result[2]);
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void ParseTestListFile_TrimsWhitespace()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "  A.B.C  \n  D.E.F  ");
        var result = TestDiscovery.ParseTestListFile(path);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("A.B.C", result[0]);
        Assert.AreEqual("D.E.F", result[1]);
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void ParseTestListFile_HandlesUtf8Bom()
{
    var path = Path.GetTempFileName();
    try
    {
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes("A.B.C\nD.E.F"));
        File.WriteAllBytes(path, bytes.ToArray());
        var result = TestDiscovery.ParseTestListFile(path);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("A.B.C", result[0]);
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void ParseTestListFile_Deduplicates()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "A.B.C\nA.B.C\nD.E.F");
        var result = TestDiscovery.ParseTestListFile(path);
        Assert.AreEqual(2, result.Count);
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void ParseTestListFile_SkipsEmptySegments()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "A.B.C\n\n||D.E.F\n   \n");
        var result = TestDiscovery.ParseTestListFile(path);
        Assert.AreEqual(2, result.Count);
    }
    finally { File.Delete(path); }
}

[TestMethod]
[ExpectedException(typeof(FileNotFoundException))]
public void ParseTestListFile_MissingFile_Throws()
{
    TestDiscovery.ParseTestListFile(Path.Combine(Path.GetTempPath(), "definitely_not_a_real_file_" + Guid.NewGuid() + ".txt"));
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "Name~ParseTestListFile" --nologo`
Expected: 8 tests fail

- [ ] **Step 4: Commit**

```bash
git add src/ParallelTestRunner/TestDiscovery.cs tests/ParallelTestRunner.Tests/TestDiscoveryParseTests.cs
git commit -m "test: add failing unit tests for ParseTestListFile"
```

---

## Task 5: Implement `ParseTestListFile`

**Files:**
- Modify: `src/ParallelTestRunner/TestDiscovery.cs`

- [ ] **Step 1: Replace the stub with the implementation**

```csharp
/// <summary>
/// Reads a file whose content is split on both newlines and '|' into a list of FQN segments.
/// Trims, skips blanks, deduplicates. Strips a leading UTF-8 BOM if present.
/// </summary>
/// <exception cref="FileNotFoundException">When <paramref name="path"/> does not exist.</exception>
internal static IReadOnlyList<string> ParseTestListFile(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException($"--test-list-file path not found: {path}", path);

    var content = File.ReadAllText(path, System.Text.Encoding.UTF8);

    // File.ReadAllText with explicit UTF8 encoding strips the BOM, but be defensive.
    if (content.Length > 0 && content[0] == '﻿')
        content = content[1..];

    var seen = new HashSet<string>(StringComparer.Ordinal);
    var result = new List<string>();

    foreach (var line in content.Split('\n'))
    {
        foreach (var segment in line.Split('|'))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
                result.Add(trimmed);
        }
    }

    return result;
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "Name~ParseTestListFile" --nologo`
Expected: 8 tests pass

- [ ] **Step 3: Commit**

```bash
git add src/ParallelTestRunner/TestDiscovery.cs
git commit -m "feat: implement ParseTestListFile with newline/pipe delimiter support"
```

---

## Task 6: Add `--test-list-file` option, mutual exclusion, and integration tests

**Files:**
- Modify: `src/ParallelTestRunner/Options.cs`
- Modify: `src/ParallelTestRunner/Program.cs`
- Modify: `tests/ParallelTestRunner.Tests/IntegrationTests.cs`

- [ ] **Step 1: Add failing integration tests**

Append to `IntegrationTests`:

```csharp
[TestMethod]
public void TestListFile_AllDummyTests_RunsSuccessfully()
{
    // Discover the real FQNs first so we don't hardcode them.
    var discoveryResult = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-tests 1 --retries 0");
    Assert.AreEqual(0, discoveryResult.ExitCode, "Pre-discovery probe failed");

    // Build a file with all 70 known DummyTestProject FQNs by running discovery against the project.
    // Easiest: use --test-list-file with FQNs that we know match. We'll seed three known-passing ones.
    var fqns = new[]
    {
        "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult",
        "DummyTestProject.Arithmetic.BasicMathTests.Subtraction_ReturnsCorrectResult",
        "DummyTestProject.Arithmetic.BasicMathTests.Multiplication_ReturnsCorrectResult",
    };
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllLines(path, fqns);

        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list-file \"{path}\"");

        Assert.AreEqual(0, result.ExitCode, $"Tool failed:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "Skipping test discovery");
        StringAssert.Contains(result.Stderr, $"Test list: provided from {path}");
        StringAssert.Contains(result.Stderr, "Total: 3 tests");
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void TestListFile_VeryLongList_OsInvocationSucceeds()
{
    // 250 unique FQN-shaped strings, ~80 chars each = ~20k chars.
    // Far past the 16k cmd.exe limit and approaching the 32k CreateProcess limit.
    // The FQNs are syntactically valid (validator passes) but match nothing in the assembly.
    // Expected exit: 2 (zero tests executed) — the *point* is that we got that far without an OS-level launch failure.
    var path = Path.GetTempFileName();
    try
    {
        var lines = new List<string>();
        for (var i = 0; i < 250; i++)
            lines.Add($"VeryLongFakeNamespace_PaddingForTheCommandLineLengthTest.SubNs{i:D3}.FakeTestClass.FakeTestMethod_PaddedSuffix_{i:D3}");
        File.WriteAllLines(path, lines);
        Assert.IsTrue(new FileInfo(path).Length > 16000, "Test setup invariant: file must exceed 16k char threshold");

        var result = RunTool($"\"{_dummyProjectPath}\" --batch-size 100 --max-parallelism 1 --retries 0 --test-list-file \"{path}\"");

        Assert.AreEqual(2, result.ExitCode, $"Expected exit 2 (zero tests executed), got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "zero tests were executed");
        Assert.IsFalse(result.Stderr.Contains("filename or extension is too long"),
            "OS-level launch failure detected — the long list was passed via argv instead of via the file");
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void TestListFile_AndTestList_BothProvided_ExitCode2()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult");
        var result = RunTool($"\"{_dummyProjectPath}\" --retries 0 --test-list \"Some.Test.Name\" --test-list-file \"{path}\"");
        Assert.AreEqual(2, result.ExitCode, $"Expected exit 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "mutually exclusive");
    }
    finally { File.Delete(path); }
}

[TestMethod]
public void TestListFile_MissingFile_ExitCode2()
{
    var fakePath = Path.Combine(Path.GetTempPath(), "definitely_not_a_real_file_" + Guid.NewGuid() + ".txt");
    var result = RunTool($"\"{_dummyProjectPath}\" --retries 0 --test-list-file \"{fakePath}\"");
    Assert.AreEqual(2, result.ExitCode, $"Expected exit 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
    StringAssert.Contains(result.Stderr, "path not found");
}

[TestMethod]
public void TestListFile_InvalidContent_ExitCode2()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "(FullNameMatchesRegex '(\\.X$)')");
        var result = RunTool($"\"{_dummyProjectPath}\" --retries 0 --test-list-file \"{path}\"");
        Assert.AreEqual(2, result.ExitCode, $"Expected exit 2 but got {result.ExitCode}.\nStderr:\n{result.Stderr}");
        StringAssert.Contains(result.Stderr, "ERROR: --test-list contains values that don't look like fully-qualified test names");
    }
    finally { File.Delete(path); }
}
```

- [ ] **Step 2: Run new tests to verify they fail**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "Name~TestListFile" --nologo`
Expected: 5 tests fail (the option doesn't exist yet so the args will be unmatched and ignored, leading to discovery running)

- [ ] **Step 3: Add `TestListFile` field to `Options.cs`**

Replace `Options.cs` content:

```csharp
namespace ParallelTestRunner;

public record Options(
    string ProjectPath,
    int BatchSize = 50,
    int MaxParallelism = 0,
    int Workers = 4,
    int MaxTests = 0,
    int Retries = 2,
    bool AutoRetry = false,
    string[]? ExtraDotnetTestArgs = null,
    string? ResultsDirectory = null,
    string? FilterExpression = null,
    string? TestList = null,
    string? TestListFile = null,
    TimeSpan IdleTimeout = default)
{
    public int MaxParallelism { get; init; } = MaxParallelism > 0
        ? MaxParallelism
        : Math.Max(1, Environment.ProcessorCount / 2);

    public string[] ExtraDotnetTestArgs { get; init; } = ExtraDotnetTestArgs ?? [];
}
```

- [ ] **Step 4: Add `--test-list-file` option to `Program.cs`**

In the option declarations block (after `testListOption` around line 117), add:

```csharp
var testListFileOption = new Option<string?>("--test-list-file")
{
    Description = "Path to a file containing fully-qualified test names (one per line, or pipe-delimited). Mutually exclusive with --test-list.",
    Arity = ArgumentArity.ZeroOrOne
};
```

Add it to the `rootCommand` initializer block (around line 119-134):

```csharp
testListOption,
testListFileOption,
```

In the `SetAction` lambda (around line 150), retrieve the value:

```csharp
var testList = parseResult.GetValue(testListOption);
var testListFile = parseResult.GetValue(testListFileOption);
```

Pass it into the `Options` record (around line 172):

```csharp
var options = new Options(
    ProjectPath: project!,
    BatchSize: batchSize,
    MaxParallelism: maxParallelism,
    Workers: workers,
    MaxTests: maxTests,
    Retries: retries,
    AutoRetry: autoRetry,
    ExtraDotnetTestArgs: extraArgs,
    ResultsDirectory: resultsDir,
    FilterExpression: filterExpression,
    TestList: testList,
    TestListFile: testListFile,
    IdleTimeout: idleTimeout > 0 ? TimeSpan.FromSeconds(idleTimeout) : TimeSpan.Zero);
```

- [ ] **Step 5: Add mutual exclusion + file parsing logic in `Program.cs`**

Insert this **immediately after** the `Options options = new Options(...);` block and **before** `PrintBanner(options);`:

```csharp
// Mutual exclusion: --test-list and --test-list-file cannot both be provided.
if (!string.IsNullOrWhiteSpace(options.TestList) && !string.IsNullOrWhiteSpace(options.TestListFile))
{
    Console.Error.WriteLine("ERROR: --test-list and --test-list-file are mutually exclusive. Use one or the other.");
    toolExitCode = 2;
    return;
}
```

Then update the test-list resolution block (around line 198) to load from file when `TestListFile` is set. Replace:

```csharp
var parsedTestList = TestDiscovery.ParseTestList(options.TestList);
```

with:

```csharp
IReadOnlyList<string> parsedTestList;
string? testListSource = null;
if (!string.IsNullOrWhiteSpace(options.TestListFile))
{
    try
    {
        parsedTestList = TestDiscovery.ParseTestListFile(options.TestListFile);
        testListSource = options.TestListFile;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        toolExitCode = 2;
        return;
    }
}
else
{
    parsedTestList = TestDiscovery.ParseTestList(options.TestList);
}
```

Then in the existing `if (parsedTestList.Count > 0)` block (which already contains the Step 3 validator), update the banner-style log to mention the source file when present:

```csharp
if (parsedTestList.Count > 0)
{
    var validationFailures = TestDiscovery.ValidateTestList(parsedTestList);
    if (validationFailures.Count > 0)
    {
        // ... existing error output
        toolExitCode = 2;
        return;
    }

    tests = parsedTestList;
    Console.Error.WriteLine("  Skipping test discovery — using provided test list");
    if (testListSource is not null)
        Console.Error.WriteLine($"  Source: {testListSource}");
    if (options.FilterExpression is not null)
        Console.Error.WriteLine("  --filter-expression ignored (not applicable when --test-list/--test-list-file is provided)");
    // ... rest unchanged (loop over tests, print "Total: N tests")
}
```

- [ ] **Step 6: Update `PrintBanner` test-list line**

Find the section in `PrintBanner` that prints `Test list: provided (N tests)` (around line 351-353). Update to include the source path when from a file:

```csharp
var bannerTestList = !string.IsNullOrWhiteSpace(options.TestListFile)
    ? TestDiscovery.ParseTestListFile(options.TestListFile)
    : TestDiscovery.ParseTestList(options.TestList);
if (bannerTestList.Count > 0)
{
    if (!string.IsNullOrWhiteSpace(options.TestListFile))
        Console.Error.WriteLine($"  Test list: provided from {options.TestListFile} ({bannerTestList.Count} tests)");
    else
        Console.Error.WriteLine($"  Test list: provided ({bannerTestList.Count} tests)");
}
```

(Note: this re-parses the file in `PrintBanner` for display. Cheap because the file is small — it's only "long" by argv standards, not by file standards. We accept that small cost for simpler code.)

- [ ] **Step 7: Run all `TestList*` tests to verify pass**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "Name~TestList" --nologo`
Expected: all `TestList_*` and `TestListFile_*` tests pass

- [ ] **Step 8: Commit**

```bash
git add src/ParallelTestRunner/Options.cs src/ParallelTestRunner/Program.cs tests/ParallelTestRunner.Tests/IntegrationTests.cs
git commit -m "feat: add --test-list-file for long lists, mutually exclusive with --test-list"
```

---

## Task 7: Update README with PowerShell examples

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Find the existing `--test-list` documentation in `README.md`**

Run: `grep -n "test-list" README.md` to locate the section.

- [ ] **Step 2: Add a new subsection immediately after the existing `--test-list` documentation**

Append (replace placeholder section with the actual file location):

````markdown
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
````

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: document --test-list-file with PowerShell examples"
```

---

## Task 8: Add failing unit tests for `Banner.BuildBanner`

**Files:**
- Create: `src/ParallelTestRunner/Banner.cs` (stub only)
- Create: `tests/ParallelTestRunner.Tests/BannerTests.cs`

- [ ] **Step 1: Create the stub `Banner.cs`**

```csharp
namespace ParallelTestRunner;

internal static class Banner
{
    internal static string BuildBanner(Options options, string version)
        => throw new NotImplementedException();
}
```

- [ ] **Step 2: Create `BannerTests.cs` with failing tests**

```csharp
namespace ParallelTestRunner.Tests;

[TestClass]
public class BannerTests
{
    private static Options DefaultOptions() => new(
        ProjectPath: "test.csproj",
        BatchSize: 50,
        MaxParallelism: 4,
        Workers: 4,
        IdleTimeout: TimeSpan.FromSeconds(60));

    [TestMethod]
    public void BuildBanner_ContainsVersion()
    {
        var output = Banner.BuildBanner(DefaultOptions(), "1.2.3");
        StringAssert.Contains(output, "v1.2.3");
    }

    [TestMethod]
    public void BuildBanner_FallsBackToUnknown_WhenVersionNullOrEmpty()
    {
        var output = Banner.BuildBanner(DefaultOptions(), "");
        StringAssert.Contains(output, "vunknown");
    }

    [TestMethod]
    public void BuildBanner_ContainsGradient()
    {
        var output = Banner.BuildBanner(DefaultOptions(), "1.0.0");
        StringAssert.Contains(output, "░▒▓");
    }

    [TestMethod]
    public void BuildBanner_ContainsAnsiShadowParallelFingerprint()
    {
        var output = Banner.BuildBanner(DefaultOptions(), "1.0.0");
        // First line of "Parallel" in ANSI Shadow font
        StringAssert.Contains(output, "██████╗  █████╗ ██████╗  █████╗ ██╗     ██╗     ███████╗██╗");
    }

    [TestMethod]
    public void BuildBanner_ContainsAnsiShadowTestRunnerFingerprint()
    {
        var output = Banner.BuildBanner(DefaultOptions(), "1.0.0");
        // First line of "Test Runner" in ANSI Shadow font
        StringAssert.Contains(output, "████████╗███████╗███████╗████████╗");
    }

    [TestMethod]
    public void BuildBanner_RetriesShown_WhenAutoRetryFalse()
    {
        var opts = DefaultOptions() with { Retries = 5, AutoRetry = false };
        var output = Banner.BuildBanner(opts, "1.0.0");
        StringAssert.Contains(output, "Retries: 5");
    }

    [TestMethod]
    public void BuildBanner_AutoRetryEnabled_WhenAutoRetryTrue()
    {
        var opts = DefaultOptions() with { AutoRetry = true };
        var output = Banner.BuildBanner(opts, "1.0.0");
        StringAssert.Contains(output, "Auto-retry: enabled");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "ClassName=ParallelTestRunner.Tests.BannerTests" --nologo`
Expected: 7 tests fail

- [ ] **Step 4: Commit**

```bash
git add src/ParallelTestRunner/Banner.cs tests/ParallelTestRunner.Tests/BannerTests.cs
git commit -m "test: add failing unit tests for BuildBanner"
```

---

## Task 9: Implement `Banner.BuildBanner` and wire it into `Program.cs`

**Files:**
- Modify: `src/ParallelTestRunner/Banner.cs`
- Modify: `src/ParallelTestRunner/Program.cs`

- [ ] **Step 1: Implement `BuildBanner`**

Replace `Banner.cs` content:

```csharp
using System.Text;

namespace ParallelTestRunner;

internal static class Banner
{
    private static readonly string[] TitleLines =
    [
        @"   ░▒▓ ██████╗  █████╗ ██████╗  █████╗ ██╗     ██╗     ███████╗██╗",
        @"   ░▒▓ ██╔══██╗██╔══██╗██╔══██╗██╔══██╗██║     ██║     ██╔════╝██║",
        @"   ░▒▓ ██████╔╝███████║██████╔╝███████║██║     ██║     █████╗  ██║",
        @"   ░▒▓ ██╔═══╝ ██╔══██║██╔══██╗██╔══██║██║     ██║     ██╔══╝  ██║",
        @"   ░▒▓ ██║     ██║  ██║██║  ██║██║  ██║███████╗███████╗███████╗███████╗",
        @"   ░▒▓ ╚═╝     ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝╚══════╝╚══════╝╚══════╝╚══════╝",
        @"   ░▒▓ ████████╗███████╗███████╗████████╗    ██████╗ ██╗   ██╗███╗   ██╗███╗   ██╗███████╗██████╗",
        @"   ░▒▓ ╚══██╔══╝██╔════╝██╔════╝╚══██╔══╝    ██╔══██╗██║   ██║████╗  ██║████╗  ██║██╔════╝██╔══██╗",
        @"   ░▒▓    ██║   █████╗  ███████╗   ██║       ██████╔╝██║   ██║██╔██╗ ██║██╔██╗ ██║█████╗  ██████╔╝",
        @"   ░▒▓    ██║   ██╔══╝  ╚════██║   ██║       ██╔══██╗██║   ██║██║╚██╗██║██║╚██╗██║██╔══╝  ██╔══██╗",
        @"   ░▒▓    ██║   ███████╗███████║   ██║       ██║  ██║╚██████╔╝██║ ╚████║██║ ╚████║███████╗██║  ██║",
        @"   ░▒▓    ╚═╝   ╚══════╝╚══════╝   ╚═╝       ╚═╝  ╚═╝ ╚═════╝ ╚═╝  ╚═══╝╚═╝  ╚═══╝╚══════╝╚═╝  ╚═╝",
    ];

    internal static string BuildBanner(Options options, string version)
    {
        var sb = new StringBuilder();
        sb.AppendLine();

        var versionDisplay = string.IsNullOrWhiteSpace(version) ? "unknown" : version;

        for (var i = 0; i < TitleLines.Length; i++)
        {
            if (i == TitleLines.Length - 1)
                sb.AppendLine($"{TitleLines[i]}   v{versionDisplay}");
            else
                sb.AppendLine(TitleLines[i]);
        }

        sb.AppendLine();
        sb.AppendLine($"  Detected cores: {Environment.ProcessorCount}");
        sb.AppendLine($"  Chosen parallelism: {options.MaxParallelism}");
        sb.AppendLine($"  Workers per process: {options.Workers}");
        sb.AppendLine($"  Idle timeout: {(options.IdleTimeout > TimeSpan.Zero ? $"{options.IdleTimeout.TotalSeconds:F0}s" : "none")}");
        sb.AppendLine(options.AutoRetry ? "  Auto-retry: enabled" : $"  Retries: {options.Retries}");

        var bannerTestList = !string.IsNullOrWhiteSpace(options.TestListFile)
            ? TestDiscovery.ParseTestListFile(options.TestListFile)
            : TestDiscovery.ParseTestList(options.TestList);
        if (bannerTestList.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(options.TestListFile))
                sb.AppendLine($"  Test list: provided from {options.TestListFile} ({bannerTestList.Count} tests)");
            else
                sb.AppendLine($"  Test list: provided ({bannerTestList.Count} tests)");
        }
        else if (options.FilterExpression is not null)
        {
            sb.AppendLine($"  Filter: {options.FilterExpression}");
        }

        sb.AppendLine($"  Results dir: {options.ResultsDirectory}");

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Run unit tests to verify they pass**

Run: `dotnet test tests/ParallelTestRunner.Tests --filter "ClassName=ParallelTestRunner.Tests.BannerTests" --nologo`
Expected: 7 tests pass

- [ ] **Step 3: Replace the inline `PrintBanner` in `Program.cs` with a call to `Banner.BuildBanner`**

Add `using System.Reflection;` to the top of `Program.cs`.

Replace the entire `static void PrintBanner(Options options)` method (around lines 331-358) with:

```csharp
static void PrintBanner(Options options)
{
    var version = typeof(Options).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? typeof(Options).Assembly.GetName().Version?.ToString()
                  ?? "unknown";

    // Strip git-hash suffix sometimes appended by SourceLink (e.g. "1.1.2+abcdef0")
    var plusIndex = version.IndexOf('+');
    if (plusIndex > 0)
        version = version[..plusIndex];

    Console.Error.Write(Banner.BuildBanner(options, version));
    Console.Error.WriteLine();
}
```

- [ ] **Step 4: Run the full test suite to check for regressions**

Run: `dotnet test tests/ParallelTestRunner.Tests --nologo`
Expected: all tests pass except possibly some integration tests with hardcoded substring assertions about the OLD banner. Note any failures.

- [ ] **Step 5: Update integration tests with old banner substrings**

Search for failing assertions referencing old banner text (the bare `____` figlet output is the old fingerprint). Run:

```bash
grep -n "Discovered\|Parallel Test Runner\|____\|Detected cores" tests/ParallelTestRunner.Tests/IntegrationTests.cs
```

Update any assertions that check banner-specific content. Most existing assertions check for things like `"Discovered 70 tests"` and `"Detected cores"` which still appear in the new banner — they should pass unchanged. If any DO fail, replace the substring with one that exists in the new banner (e.g. `"v"` followed by version, or `"░▒▓"`).

- [ ] **Step 6: Run full test suite again**

Run: `dotnet test tests/ParallelTestRunner.Tests --nologo`
Expected: all tests pass.

- [ ] **Step 7: Manually verify the banner**

Run: `dotnet run --project src/ParallelTestRunner -- tests/DummyTestProject/DummyTestProject.csproj --max-tests 1 --retries 0`

Visually confirm:
- The gradient `░▒▓` appears on the left of every title line
- "Parallel" sits on top, "Test Runner" sits below
- `v1.1.2` (or whatever the current version is in the csproj) appears at the end of the bottom title row

- [ ] **Step 8: Commit**

```bash
git add src/ParallelTestRunner/Banner.cs src/ParallelTestRunner/Program.cs tests/ParallelTestRunner.Tests/IntegrationTests.cs
git commit -m "feat: upgrade banner with ANSI Shadow font, gradient logo, and version"
```

---

## Task 10: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update the Project Structure / `--test-list` section**

Find the existing description of `--test-list` (search for `test-list` in CLAUDE.md). Add a sentence describing the new option and validation behaviour.

In the section that begins:

```
- **Discovery**: Two-step process: first runs ...
```

Find the part that says "**Skippable via `--test-list`**: when a pipe-delimited string of FQN test names is provided, discovery is bypassed entirely and the supplied names are fed directly into batching."

Replace with:

```
- **Skippable via `--test-list` or `--test-list-file`**: when a pipe-delimited string of FQN test names (or a file path containing them) is provided, discovery is bypassed entirely and the supplied names are fed directly into batching. `--test-list-file` is the alternative for lists too long to fit on the Windows command line (cmd.exe ~8K, CreateProcess ~32K). Both options are mutually exclusive — providing both exits 2 with an error.
- **FQN validation**: provided test names must match `Namespace.Class.Method` shape (regex `^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$`). Inputs like vstest TestCaseFilter expressions (`(FullNameMatchesRegex '...')`) are rejected with exit code 2 before any test execution begins.
```

- [ ] **Step 2: Update the Project Structure file list**

In the "Project Structure" section, update the `Program.cs` line to mention the new option and add `Banner.cs`:

```
src/ParallelTestRunner/          # Main tool (console app, packaged as dotnet tool)
  Program.cs                     # CLI entry point using System.CommandLine, orchestrates pipeline
  Options.cs                     # Immutable record for configuration
  Banner.cs                      # Pure helper that builds the startup banner string (incl. version)
  TestDiscovery.cs               # Two-step discovery: resolves DLL path, then extracts FQN test names; also ParseTestList/ParseTestListFile/ValidateTestList
  ...
```

And update the test file list in `tests/ParallelTestRunner.Tests/`:

- Update the count for `TestDiscoveryParseTests.cs` (was 13 unit tests, now 13 + 12 validator + 8 file = 33)
- Add `BannerTests.cs                  # 7 unit tests for banner construction and version display`
- Update `IntegrationTests.cs` count to reflect added tests (32 + ~7 new = ~39)

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with --test-list-file, validation, and banner notes"
```

---

## Task 11: Final verification

**Files:** none

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test --nologo`
Expected: all tests pass.

- [ ] **Step 2: Build the tool and run with all flags exercised manually**

Run: `dotnet build ParallelTestRunner.sln --nologo`

Then:

```bash
# Standard run
dotnet run --project src/ParallelTestRunner -- tests/DummyTestProject/DummyTestProject.csproj --max-tests 3 --retries 0

# --test-list happy path
dotnet run --project src/ParallelTestRunner -- tests/DummyTestProject/DummyTestProject.csproj --test-list "DummyTestProject.Arithmetic.BasicMathTests.Addition_ReturnsCorrectResult" --retries 0

# --test-list invalid (must exit 2)
dotnet run --project src/ParallelTestRunner -- tests/DummyTestProject/DummyTestProject.csproj --test-list "(FullNameMatchesRegex 'foo')" --retries 0
echo "Exit: $?"
```

Expected: each runs the new banner; the invalid test-list run prints the validation error and exits 2.

- [ ] **Step 3: If `dotnet test` reports a flaky integration test, retry once.** Tests like `IdleTimeout_DetectsAndIsolatesHangingTest` rely on real timing. If a single retry doesn't fix it, investigate.

- [ ] **Step 4: Done — no commit (verification only).**

---

## Self-Review

Spec coverage check (against `2026-04-27-test-list-validation-and-banner-design.md`):
- §1 Validator → Tasks 1, 2 ✓
- §1 Wired into Program.cs → Task 3 ✓
- §1 Existing zero-tests-executed path → covered by existing test `TestList_NonMatchingFqns_ExitCode2`; no new task needed (noted in plan §1)
- §2 ParseTestListFile → Tasks 4, 5 ✓
- §2 --test-list-file option + mutual exclusion + integration tests → Task 6 ✓
- §2 README → Task 7 ✓
- §3 Banner → Tasks 8, 9 ✓
- File-by-file summary → all files touched in plan ✓
- CLAUDE.md → Task 10 ✓

Type consistency: `TestListValidationFailure(int Index, string Segment, string Reason)` used consistently across Tasks 1, 2, 3. `ParseTestListFile` signature `IReadOnlyList<string> ParseTestListFile(string)` consistent across Tasks 4, 5, 6, 9. `BuildBanner(Options, string)` consistent across Tasks 8, 9.

Placeholder scan: No "TBD", "implement later", or "similar to Task N" placeholders. All code blocks present in full. Exact file paths and commands throughout.
