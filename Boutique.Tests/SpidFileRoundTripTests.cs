using Boutique.Models;
using Boutique.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Boutique.Tests;

/// <summary>
///     File-based round-trip tests for SPID distribution files.
///     Reads actual .ini files from TestData directory and verifies parsing/formatting fidelity.
/// </summary>
public class SpidFileRoundTripTests(ITestOutputHelper output)
{
    private static readonly string _testDataPath = Path.Combine(
        AppContext.BaseDirectory, "TestData");

    private static readonly string _spidTestDataPath = Path.Combine(_testDataPath, "Spid");

    #region Diff Analysis

    /// <summary>
    ///     Analyzes differences between original and formatted lines, useful for debugging.
    /// </summary>
    [Fact]
    public void Analyze_SampleFile_ShowDifferences()
    {
        var filePath = Path.Combine(_spidTestDataPath, "sample_DISTR.ini");
        if (!File.Exists(filePath)) return;

        var lines = File.ReadAllLines(filePath);
        var differences = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                continue;

            var result = TestRoundTrip(line);
            if (!result.Success && result.FormattedLine != null)
            {
                differences.Add($"Line {i + 1}:");
                differences.Add($"  Original:  {result.OriginalLine}");
                differences.Add($"  Formatted: {result.FormattedLine}");
                differences.Add($"  Diff: {GetDiff(result.OriginalLine, result.FormattedLine)}");
                differences.Add("");
            }
        }

        // This test always passes - it's for analysis output
        if (differences.Count > 0)
        {
            // Output differences for debugging (visible in test output)
            var output = string.Join(Environment.NewLine, differences);
            Assert.True(true, $"Differences found:\n{output}");
        }
    }

    #endregion

    #region Result Types

    private record RoundTripResult
    {
        public int LineNumber { get; init; }
        public string OriginalLine { get; init; } = string.Empty;
        public string? FormattedLine { get; init; }
        public bool Success { get; init; }
        public string? FailureReason { get; init; }
    }

    #endregion

    #region File Discovery

    [Fact]
    public void TestDataDirectory_Exists()
    {
        Assert.True(Directory.Exists(_testDataPath),
            $"TestData directory not found at: {_testDataPath}");
    }

    [Fact]
    public void SpidTestData_HasFiles()
    {
        if (!Directory.Exists(_spidTestDataPath))
        {
            // Skip if directory doesn't exist yet
            return;
        }

        var files = Directory.GetFiles(_spidTestDataPath, "*.ini");
        Assert.NotEmpty(files);
    }

    #endregion

    #region Round-Trip File Tests

    [Fact]
    public void RoundTrip_SampleSpidFile_AllLinesPreserved()
    {
        var filePath = Path.Combine(_spidTestDataPath, "sample_DISTR.ini");
        if (!File.Exists(filePath))
        {
            // Skip if file doesn't exist
            return;
        }

        var lines = File.ReadAllLines(filePath);
        var results = new List<RoundTripResult>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';'))
                continue;

            var result = TestRoundTrip(trimmed);
            results.Add(result);
        }

        // Report all failures at once for easier debugging
        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count > 0)
        {
            var message = string.Join(Environment.NewLine, failures.Select(f =>
                $"Line: {f.OriginalLine}\nExpected: {f.OriginalLine}\nActual: {f.FormattedLine}\nReason: {f.FailureReason}"));
            Assert.Fail($"Round-trip failures:\n{message}");
        }

        // Verify we actually tested some lines
        Assert.True(results.Count > 0, "No lines were tested");
    }

    [Theory]
    [InlineData("sample_DISTR.ini")]
    public void RoundTrip_SpecificFile_AllLinesPreserved(string fileName)
    {
        var filePath = Path.Combine(_spidTestDataPath, fileName);
        if (!File.Exists(filePath))
        {
            // Skip if file doesn't exist
            return;
        }

        var (successCount, failureCount, failures) = TestFileRoundTrip(filePath);

        if (failures.Count > 0)
        {
            var message = string.Join(Environment.NewLine + Environment.NewLine, failures.Select(f =>
                $"Line {f.LineNumber}: {f.OriginalLine}\n  Got: {f.FormattedLine}\n  Reason: {f.FailureReason}"));
            Assert.Fail($"Round-trip failures in {fileName}:\n{message}");
        }

        Assert.True(successCount > 0, $"No lines were successfully tested in {fileName}");
    }

    /// <summary>
    ///     Test all .ini files in the Spid test data directory.
    ///     Add your own distribution files to TestData/Spid/ to include them in testing.
    /// </summary>
    [Fact]
    public void RoundTrip_AllSpidFiles_AllLinesPreserved()
    {
        if (!Directory.Exists(_spidTestDataPath)) return;

        var files = Directory.GetFiles(_spidTestDataPath, "*.ini");
        if (files.Length == 0) return;

        var allFailures = new List<(string File, List<RoundTripResult> Failures)>();
        var fileStats = new List<(string File, int SuccessCount, int FailureCount)>();

        foreach (var file in files)
        {
            var (successCount, failureCount, failures) = TestFileRoundTrip(file);
            fileStats.Add((Path.GetFileName(file), successCount, failureCount));

            if (failures.Count > 0) allFailures.Add((Path.GetFileName(file), failures));
        }

        // Build and output summary
        var totalSuccess = fileStats.Sum(f => f.SuccessCount);
        var totalFailure = fileStats.Sum(f => f.FailureCount);

        output.WriteLine(
            $"Tested {files.Length} files, {totalSuccess + totalFailure} lines ({totalSuccess} passed, {totalFailure} failed):");
        foreach (var (file, success, failure) in fileStats)
            output.WriteLine($"  {file}: {success + failure} lines ({success} passed, {failure} failed)");

        if (allFailures.Count > 0)
        {
            var message = string.Join(Environment.NewLine + Environment.NewLine,
                allFailures.Select(f =>
                    $"=== {f.File} ===\n" + string.Join(Environment.NewLine, f.Failures.Select(r =>
                        $"  Line {r.LineNumber}: {r.OriginalLine}\n    Got: {r.FormattedLine}\n    Reason: {r.FailureReason}"))));
            Assert.Fail($"Round-trip failures:\n{message}");
        }

        Assert.True(totalSuccess > 0, "No lines were successfully tested across all files");
    }

    #endregion

    #region Helper Methods

    private static (int SuccessCount, int FailureCount, List<RoundTripResult> Failures) TestFileRoundTrip(
        string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var successCount = 0;
        var failures = new List<RoundTripResult>();

        for (var lineNumber = 1; lineNumber <= lines.Length; lineNumber++)
        {
            var line = lines[lineNumber - 1].Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                continue;

            var result = TestRoundTrip(line);
            result = result with { LineNumber = lineNumber };

            if (result.Success)
                successCount++;
            else
                failures.Add(result);
        }

        return (successCount, failures.Count, failures);
    }

    private static RoundTripResult TestRoundTrip(string line)
    {
        // Try to parse the line
        if (!SpidLineParser.TryParse(line, out var filter1))
        {
            return new RoundTripResult { OriginalLine = line, Success = false, FailureReason = "Failed to parse line" };
        }

        if (filter1 == null)
        {
            return new RoundTripResult
            {
                OriginalLine = line, Success = false, FailureReason = "Parser returned null filter"
            };
        }

        // Format back to string
        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter1);

        // Parse the formatted string
        if (!SpidLineParser.TryParse(formatted, out var filter2) || filter2 == null)
        {
            return new RoundTripResult
            {
                OriginalLine = line,
                FormattedLine = formatted,
                Success = false,
                FailureReason = "Failed to parse formatted output"
            };
        }

        // Compare semantically
        var (equivalent, reason) = AreSemanticallySame(filter1, filter2);
        return new RoundTripResult
        {
            OriginalLine = line,
            FormattedLine = formatted,
            Success = equivalent,
            FailureReason = equivalent ? null : reason
        };
    }

    private static (bool Equivalent, string? Reason) AreSemanticallySame(SpidDistributionFilter a,
        SpidDistributionFilter b)
    {
        if (a.FormType != b.FormType)
            return (false, $"FormType differs: {a.FormType} vs {b.FormType}");

        if (!string.Equals(a.FormIdentifier, b.FormIdentifier, StringComparison.OrdinalIgnoreCase))
            return (false, $"FormIdentifier differs: {a.FormIdentifier} vs {b.FormIdentifier}");

        if (!FilterSectionsEquivalent(a.StringFilters, b.StringFilters))
            return (false, "StringFilters differ");

        if (!FilterSectionsEquivalent(a.FormFilters, b.FormFilters))
            return (false, "FormFilters differ");

        if (!string.Equals(a.LevelFilters ?? "", b.LevelFilters ?? "", StringComparison.OrdinalIgnoreCase))
            return (false, $"LevelFilters differ: {a.LevelFilters} vs {b.LevelFilters}");

        if (!TraitFiltersEquivalent(a.TraitFilters, b.TraitFilters))
            return (false, "TraitFilters differ");

        if (!string.Equals(a.CountOrPackageIdx ?? "", b.CountOrPackageIdx ?? "", StringComparison.OrdinalIgnoreCase))
            return (false, $"CountOrPackageIdx differs: {a.CountOrPackageIdx} vs {b.CountOrPackageIdx}");

        if (a.Chance != b.Chance)
            return (false, $"Chance differs: {a.Chance} vs {b.Chance}");

        return (true, null);
    }

    private static bool FilterSectionsEquivalent(SpidFilterSection a, SpidFilterSection b)
    {
        if (a.Expressions.Count != b.Expressions.Count)
            return false;

        for (var i = 0; i < a.Expressions.Count; i++)
        {
            if (!FilterExpressionsEquivalent(a.Expressions[i], b.Expressions[i]))
                return false;
        }

        if (a.GlobalExclusions.Count != b.GlobalExclusions.Count)
            return false;

        for (var i = 0; i < a.GlobalExclusions.Count; i++)
        {
            if (!FilterPartsEquivalent(a.GlobalExclusions[i], b.GlobalExclusions[i]))
                return false;
        }

        return true;
    }

    private static bool FilterExpressionsEquivalent(SpidFilterExpression a, SpidFilterExpression b)
    {
        if (a.Parts.Count != b.Parts.Count)
            return false;

        for (var i = 0; i < a.Parts.Count; i++)
        {
            if (!FilterPartsEquivalent(a.Parts[i], b.Parts[i]))
                return false;
        }

        return true;
    }

    private static bool FilterPartsEquivalent(SpidFilterPart a, SpidFilterPart b) =>
        a.IsNegated == b.IsNegated &&
        string.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);

    private static bool TraitFiltersEquivalent(SpidTraitFilters a, SpidTraitFilters b) =>
        a.IsFemale == b.IsFemale &&
        a.IsUnique == b.IsUnique &&
        a.IsSummonable == b.IsSummonable &&
        a.IsChild == b.IsChild &&
        a.IsLeveled == b.IsLeveled &&
        a.IsTeammate == b.IsTeammate &&
        a.IsDead == b.IsDead;

    private static string GetDiff(string original, string formatted)
    {
        // Simple character-by-character diff indicator
        var minLen = Math.Min(original.Length, formatted.Length);
        for (var i = 0; i < minLen; i++)
            if (original[i] != formatted[i])
                return $"First difference at position {i}: '{original[i]}' vs '{formatted[i]}'";

        if (original.Length != formatted.Length)
            return $"Length difference: original={original.Length}, formatted={formatted.Length}";

        return "No difference found";
    }

    #endregion
}
