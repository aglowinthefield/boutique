using Boutique.Models;
using Boutique.Utilities;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Boutique.Tests;

/// <summary>
///     Semantic round-trip tests for SPID distribution files.
///     These tests compare parsed structures semantically rather than string equality,
///     which catches cases where formatting differs but meaning is preserved.
///     For string-equality round-trip tests, see SpidFullFlowTests.
/// </summary>
public class SpidFileRoundTripTests(ITestOutputHelper output)
{
    private static readonly string TestDataPath = Path.Combine(
        AppContext.BaseDirectory, "TestData");

    private static readonly string SpidTestDataPath = Path.Combine(TestDataPath, "Spid");

    #region Semantic Round-Trip Tests

    [Fact]
    public void SemanticRoundTrip_SampleFile_AllLinesSemanticallySame()
    {
        var filePath = Path.Combine(SpidTestDataPath, "sample_DISTR.ini");
        if (!File.Exists(filePath))
        {
            return;
        }

        var (successCount, _, failures) = TestFileSemanticRoundTrip(filePath);

        if (failures.Count > 0)
        {
            var message = string.Join(Environment.NewLine + Environment.NewLine, failures.Select(f =>
                $"Line {f.LineNumber}: {f.OriginalLine}\n  Got: {f.FormattedLine}\n  Reason: {f.FailureReason}"));
            failures.Should().BeEmpty($"all lines should be semantically equivalent:\n{message}");
        }

        output.WriteLine($"Tested {successCount} lines successfully");
        successCount.Should().BeGreaterThan(0, "at least one line should be tested");
    }

    [Fact]
    public void SemanticRoundTrip_MagecoreFile_AllLinesSemanticallySame()
    {
        var filePath = Path.Combine(SpidTestDataPath, "Magecore_General_DISTR.ini");
        if (!File.Exists(filePath))
        {
            return;
        }

        var (successCount, _, failures) = TestFileSemanticRoundTrip(filePath);

        if (failures.Count > 0)
        {
            var message = string.Join(Environment.NewLine + Environment.NewLine, failures.Take(10).Select(f =>
                $"Line {f.LineNumber}: {f.OriginalLine}\n  Got: {f.FormattedLine}\n  Reason: {f.FailureReason}"));
            failures.Should().BeEmpty($"all lines should be semantically equivalent ({failures.Count} total):\n{message}");
        }

        output.WriteLine($"Tested {successCount} lines successfully");
        successCount.Should().BeGreaterThan(100, "Magecore file should have 100+ lines");
    }

    #endregion

    #region Diff Analysis (Debugging)

    [Fact]
    public void Analyze_AllFiles_ShowDifferences()
    {
        if (!Directory.Exists(SpidTestDataPath))
        {
            return;
        }

        var allDifferences = new List<string>();

        foreach (var file in Directory.GetFiles(SpidTestDataPath, "*.ini"))
        {
            var lines = File.ReadAllLines(file);
            var fileName = Path.GetFileName(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                {
                    continue;
                }

                var result = TestSemanticRoundTrip(line);
                if (!result.Success && result.FormattedLine != null && result.FormattedLine != line)
                {
                    allDifferences.Add($"{fileName}:{i + 1}:");
                    allDifferences.Add($"  Original:  {result.OriginalLine}");
                    allDifferences.Add($"  Formatted: {result.FormattedLine}");
                    allDifferences.Add($"  Reason: {result.FailureReason}");
                    allDifferences.Add("");
                }
            }
        }

        if (allDifferences.Count > 0)
        {
            output.WriteLine("String differences found (may be semantically equivalent):");
            foreach (var diff in allDifferences.Take(50))
            {
                output.WriteLine(diff);
            }
        }
        else
        {
            output.WriteLine("No differences found - all round-trips are identical");
        }
    }

    #endregion

    #region Helper Methods

    private static (int SuccessCount, int FailureCount, List<RoundTripResult> Failures) TestFileSemanticRoundTrip(
        string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var successCount = 0;
        var failures = new List<RoundTripResult>();

        for (var lineNumber = 1; lineNumber <= lines.Length; lineNumber++)
        {
            var line = lines[lineNumber - 1].Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
            {
                continue;
            }

            var result = TestSemanticRoundTrip(line);
            result = result with { LineNumber = lineNumber };

            if (result.Success)
            {
                successCount++;
            }
            else
            {
                failures.Add(result);
            }
        }

        return (successCount, failures.Count, failures);
    }

    private static RoundTripResult TestSemanticRoundTrip(string line)
    {
        if (!SpidLineParser.TryParse(line, out var filter1))
        {
            return new RoundTripResult { OriginalLine = line, Success = false, FailureReason = "Failed to parse line" };
        }

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter1!);

        if (!SpidLineParser.TryParse(formatted, out var filter2))
        {
            return new RoundTripResult
            {
                OriginalLine = line,
                FormattedLine = formatted,
                Success = false,
                FailureReason = "Failed to parse formatted output"
            };
        }

        var (equivalent, reason) = AreSemanticallySame(filter1!, filter2!);
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
        {
            return (false, $"FormType differs: {a.FormType} vs {b.FormType}");
        }

        if (!string.Equals(a.FormIdentifier, b.FormIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"FormIdentifier differs: {a.FormIdentifier} vs {b.FormIdentifier}");
        }

        if (!FilterSectionsEquivalent(a.StringFilters, b.StringFilters))
        {
            return (false, "StringFilters differ");
        }

        if (!FilterSectionsEquivalent(a.FormFilters, b.FormFilters))
        {
            return (false, "FormFilters differ");
        }

        if (!string.Equals(a.LevelFilters ?? "", b.LevelFilters ?? "", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"LevelFilters differ: {a.LevelFilters} vs {b.LevelFilters}");
        }

        if (!TraitFiltersEquivalent(a.TraitFilters, b.TraitFilters))
        {
            return (false, "TraitFilters differ");
        }

        if (!string.Equals(a.CountOrPackageIdx ?? "", b.CountOrPackageIdx ?? "", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"CountOrPackageIdx differs: {a.CountOrPackageIdx} vs {b.CountOrPackageIdx}");
        }

        if (a.Chance != b.Chance)
        {
            return (false, $"Chance differs: {a.Chance} vs {b.Chance}");
        }

        return (true, null);
    }

    private static bool FilterSectionsEquivalent(SpidFilterSection a, SpidFilterSection b)
    {
        if (a.Expressions.Count != b.Expressions.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Expressions.Count; i++)
        {
            if (!FilterExpressionsEquivalent(a.Expressions[i], b.Expressions[i]))
            {
                return false;
            }
        }

        if (a.GlobalExclusions.Count != b.GlobalExclusions.Count)
        {
            return false;
        }

        for (var i = 0; i < a.GlobalExclusions.Count; i++)
        {
            if (!FilterPartsEquivalent(a.GlobalExclusions[i], b.GlobalExclusions[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FilterExpressionsEquivalent(SpidFilterExpression a, SpidFilterExpression b)
    {
        if (a.Parts.Count != b.Parts.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Parts.Count; i++)
        {
            if (!FilterPartsEquivalent(a.Parts[i], b.Parts[i]))
            {
                return false;
            }
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
}
