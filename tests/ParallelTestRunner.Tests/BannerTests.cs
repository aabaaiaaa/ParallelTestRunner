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
