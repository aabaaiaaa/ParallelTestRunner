using System.Text;

namespace ParallelTestRunner;

internal static class Banner
{
    // Slant 3D figlet font, ASCII-only. The gradient ..::;/ slides left one column per
    // row so the trailing '/' tracks the leading '/' of the slant text — the gradient's
    // right edge runs parallel to the text's leading diagonal. Line 1 (the underscores
    // at the top of the letterforms) sits unprefixed. Pure ASCII so the banner renders
    // identically on any console (UTF-8, CP1252, CP437) without code-page coordination.
    private static readonly string[] TitleLines =
    [
        @"                ____                   ____     __   ______          __     ____",
        @"        ..::;/ / __ \____ __________ _/ / /__  / /  /_  __/__  _____/ /_   / __ \__  ______  ____  ___  _____",
        @"       ..::;/ / /_/ / __ `/ ___/ __ `/ / / _ \/ /    / / / _ \/ ___/ __/  / /_/ / / / / __ \/ __ \/ _ \/ ___/",
        @"      ..::;/ / ____/ /_/ / /  / /_/ / / /  __/ /    / / /  __(__  ) /_   / _, _/ /_/ / / / / / / /  __/ /",
        @"     ..::;/ /_/    \__,_/_/   \__,_/_/_/\___/_/    /_/  \___/____/\__/  /_/ |_|\__,_/_/ /_/_/ /_/\___/_/",
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
            ? SafeParseTestListFile(options.TestListFile)
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

    // Banner is best-effort display; the action layer's file-loading branch is the
    // source of truth for file errors and will exit 2 with the proper message.
    private static IReadOnlyList<string> SafeParseTestListFile(string path)
    {
        try { return TestDiscovery.ParseTestListFile(path); }
        catch { return []; }
    }
}
